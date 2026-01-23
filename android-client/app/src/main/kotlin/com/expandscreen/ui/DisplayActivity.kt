package com.expandscreen.ui

import android.content.pm.ActivityInfo
import android.os.Bundle
import android.opengl.GLSurfaceView
import android.view.WindowManager
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.lifecycleScope
import androidx.lifecycle.repeatOnLifecycle
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.expandscreen.core.decoder.EncodedFrame
import com.expandscreen.core.decoder.FrameBufferPool
import com.expandscreen.core.decoder.H264Decoder
import com.expandscreen.core.decoder.VideoDecoderConfig
import com.expandscreen.core.network.ConnectionState
import com.expandscreen.core.network.IncomingMessage
import com.expandscreen.core.network.NetworkManager
import com.expandscreen.core.renderer.GLRenderer
import com.expandscreen.ui.theme.ExpandScreenTheme
import dagger.hilt.android.AndroidEntryPoint
import javax.inject.Inject
import kotlin.math.roundToInt
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import timber.log.Timber

/**
 * Display Activity - Full screen video display
 *
 * This activity displays the decoded video stream from the Windows PC
 * in full-screen mode using OpenGL ES rendering.
 */
@AndroidEntryPoint
class DisplayActivity : ComponentActivity() {

    @Inject lateinit var networkManager: NetworkManager

    private val uiState = MutableStateFlow(DisplayUiState())

    private var glSurfaceView: GLSurfaceView? = null
    private val frameBufferPool = FrameBufferPool()
    private val decoder = H264Decoder()

    private val decoderInitLock = Any()

    @Volatile
    private var decoderInitialized = false

    @Volatile
    private var decoderSurfaceReady = false

    @Volatile
    private var lastVideoWidth = 0

    @Volatile
    private var lastVideoHeight = 0

    private val renderer =
        GLRenderer(
            onDecoderSurfaceReady = { _ ->
                decoderSurfaceReady = true
                synchronized(decoderInitLock) {
                    decoderInitialized = false
                }
            },
        )

    private var collectJob: Job? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        Timber.d("DisplayActivity created")

        // Keep screen on during video display
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)

        // Enable immersive full-screen mode
        setupFullScreen()

        // Disable rotation by default (configurable via intent extra)
        val allowRotation = intent.getBooleanExtra(EXTRA_ALLOW_ROTATION, false)
        applyOrientationPolicy(allowRotation)

        setContent {
            ExpandScreenTheme {
                Surface(modifier = Modifier.fillMaxSize(), color = Color.Black) {
                    DisplayScreen(
                        uiState = uiState,
                        onExit = {
                            networkManager.disconnect()
                            finish()
                        },
                        onToggleHud = { uiState.update { it.copy(showHud = !it.showHud) } },
                        onToggleRotationLock = {
                            val newAllowRotation = !uiState.value.allowRotation
                            uiState.update { it.copy(allowRotation = newAllowRotation) }
                            applyOrientationPolicy(newAllowRotation)
                        },
                        onToggleMenu = { uiState.update { it.copy(showMenu = !it.showMenu) } },
                        onHideMenu = { uiState.update { it.copy(showMenu = false) } },
                        glSurfaceViewProvider = { surfaceView ->
                            glSurfaceView = surfaceView
                            renderer.bindTo(surfaceView)
                        },
                    )
                }
            }
        }

        uiState.update { it.copy(allowRotation = allowRotation) }
    }

    private fun setupFullScreen() {
        WindowCompat.setDecorFitsSystemWindows(window, false)
        WindowInsetsControllerCompat(window, window.decorView).let { controller ->
            controller.hide(WindowInsetsCompat.Type.systemBars())
            controller.systemBarsBehavior =
                WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
        }
    }

    private fun applyOrientationPolicy(allowRotation: Boolean) {
        requestedOrientation =
            if (allowRotation) {
                ActivityInfo.SCREEN_ORIENTATION_FULL_SENSOR
            } else {
                ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE
            }
    }

    override fun onResume() {
        super.onResume()
        Timber.d("DisplayActivity resumed - starting video playback")

        setupFullScreen()
        glSurfaceView?.onResume()

        collectJob?.cancel()
        collectJob =
            lifecycleScope.launch {
                repeatOnLifecycle(Lifecycle.State.STARTED) {
                    launch {
                        networkManager.connectionState.collect { state ->
                            uiState.update { it.copy(connectionState = state) }
                            if (state == ConnectionState.Disconnected) {
                                finish()
                            }
                        }
                    }

                    launch(Dispatchers.Default) {
                        var windowStartMs = System.currentTimeMillis()
                        var framesInWindow = 0

                        networkManager.incomingMessages.collect { message ->
                            if (message !is IncomingMessage.VideoFrame) return@collect

                            val nowMs = System.currentTimeMillis()
                            val latencyMs = (nowMs - message.timestampMs).coerceAtLeast(0).toInt()

                            lastVideoWidth = message.message.width
                            lastVideoHeight = message.message.height
                            renderer.setVideoSize(message.message.width, message.message.height)

                            if (!decoderSurfaceReady) {
                                uiState.update { it.copy(latencyMs = latencyMs) }
                                return@collect
                            }

                            if (!ensureDecoderInitialized(message.message.width, message.message.height)) {
                                uiState.update { it.copy(latencyMs = latencyMs) }
                                return@collect
                            }

                            val encodedFrame =
                                EncodedFrame.fromMessage(
                                    message = message.message,
                                    timestampMs = message.timestampMs,
                                    pool = frameBufferPool,
                                )
                            decoder.enqueueFrame(encodedFrame)

                            framesInWindow += 1
                            val elapsedMs = nowMs - windowStartMs
                            if (elapsedMs >= 1_000L) {
                                val fps = (framesInWindow * 1000f / elapsedMs.toFloat()).roundToInt()
                                uiState.update { it.copy(fps = fps, latencyMs = latencyMs) }
                                framesInWindow = 0
                                windowStartMs = nowMs
                            } else {
                                uiState.update { it.copy(latencyMs = latencyMs) }
                            }
                        }
                    }
                }
            }
    }

    override fun onPause() {
        super.onPause()
        Timber.d("DisplayActivity paused")
        collectJob?.cancel()
        collectJob = null
        glSurfaceView?.onPause()
        decoder.flush()
    }

    override fun onDestroy() {
        super.onDestroy()
        Timber.d("DisplayActivity destroyed - releasing resources")
        collectJob?.cancel()
        collectJob = null

        decoder.release()
        renderer.release()
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (hasFocus) {
            setupFullScreen()
        }
    }

    private fun ensureDecoderInitialized(width: Int, height: Int): Boolean {
        synchronized(decoderInitLock) {
            if (decoderInitialized) return true
            val surface = renderer.decoderSurface ?: return false
            decoder.initialize(
                VideoDecoderConfig(
                    width = width.coerceAtLeast(1),
                    height = height.coerceAtLeast(1),
                    outputSurface = surface,
                ),
            )
            decoderInitialized = true
            return true
        }
    }

    private companion object {
        const val EXTRA_ALLOW_ROTATION = "com.expandscreen.extra.ALLOW_ROTATION"
    }
}

private data class DisplayUiState(
    val fps: Int = 0,
    val latencyMs: Int = 0,
    val connectionState: ConnectionState = ConnectionState.Disconnected,
    val showHud: Boolean = true,
    val showMenu: Boolean = false,
    val allowRotation: Boolean = false,
)

@Composable
private fun DisplayScreen(
    uiState: MutableStateFlow<DisplayUiState>,
    onExit: () -> Unit,
    onToggleHud: () -> Unit,
    onToggleRotationLock: () -> Unit,
    onToggleMenu: () -> Unit,
    onHideMenu: () -> Unit,
    glSurfaceViewProvider: (GLSurfaceView) -> Unit,
) {
    val state by uiState.collectAsStateWithLifecycle()

    val hudBrush =
        Brush.linearGradient(
            0.0f to Color(0xCC0B0F14),
            1.0f to Color(0xCC0E1A16),
        )

    Box(
        modifier =
            Modifier
                .fillMaxSize()
                .background(Color.Black)
                .pointerInput(Unit) {
                    detectTapGestures(
                        onDoubleTap = { onToggleMenu() },
                        onTap = { if (state.showMenu) onHideMenu() },
                    )
                },
    ) {
        AndroidView(
            modifier = Modifier.fillMaxSize(),
            factory = {
                GLSurfaceView(it).also { view -> glSurfaceViewProvider(view) }
            },
        )

        AnimatedVisibility(
            visible = state.showHud && !state.showMenu,
            enter = fadeIn(),
            exit = fadeOut(),
        ) {
            HudPanel(
                modifier =
                    Modifier
                        .align(Alignment.TopStart)
                        .padding(14.dp),
                brush = hudBrush,
                state = state,
            )
        }

        AnimatedVisibility(
            visible = state.showMenu,
            enter = fadeIn(),
            exit = fadeOut(),
        ) {
            MenuPanel(
                modifier =
                    Modifier
                        .align(Alignment.TopCenter)
                        .padding(top = 18.dp),
                brush = hudBrush,
                state = state,
                onExit = onExit,
                onToggleHud = onToggleHud,
                onToggleRotationLock = onToggleRotationLock,
            )
        }
    }
}

@Composable
private fun HudPanel(
    modifier: Modifier,
    brush: Brush,
    state: DisplayUiState,
) {
    val connectionText =
        when (state.connectionState) {
            ConnectionState.Connecting -> "CONNECTING"
            is ConnectionState.Connected -> "CONNECTED"
            ConnectionState.Disconnected -> "DISCONNECTED"
        }

    Column(
        modifier =
            modifier
                .clip(RoundedCornerShape(14.dp))
                .background(brush)
                .padding(horizontal = 12.dp, vertical = 10.dp),
        verticalArrangement = Arrangement.spacedBy(4.dp),
    ) {
        Text(
            text = "EXPANDSCREEN • HUD",
            style = MaterialTheme.typography.labelLarge,
            color = Color(0xFF7CFAC6),
            fontFamily = FontFamily.Monospace,
        )
        Text(
            text = "FPS ${state.fps.toString().padStart(2, '0')}  •  LAT ${state.latencyMs}ms",
            style = MaterialTheme.typography.bodyMedium,
            color = Color(0xFFE9F2FF),
            fontFamily = FontFamily.Monospace,
        )
        Text(
            text = "LINK $connectionText",
            style = MaterialTheme.typography.bodyMedium,
            color = Color(0xFFB9FFEA),
            fontFamily = FontFamily.Monospace,
        )
    }
}

@Composable
private fun MenuPanel(
    modifier: Modifier,
    brush: Brush,
    state: DisplayUiState,
    onExit: () -> Unit,
    onToggleHud: () -> Unit,
    onToggleRotationLock: () -> Unit,
) {
    val connectionText =
        when (state.connectionState) {
            ConnectionState.Connecting -> "Connecting…"
            is ConnectionState.Connected -> "Connected"
            ConnectionState.Disconnected -> "Disconnected"
        }

    Column(
        modifier =
            modifier
                .clip(RoundedCornerShape(18.dp))
                .background(brush)
                .padding(14.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Text(
            text = "Menu",
            style = MaterialTheme.typography.titleMedium,
            color = Color(0xFFE9F2FF),
        )

        Text(
            text = "$connectionText • ${state.fps} FPS • ${state.latencyMs}ms",
            style = MaterialTheme.typography.bodyMedium,
            color = Color(0xFFB9FFEA),
            fontFamily = FontFamily.Monospace,
        )

        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            Button(
                onClick = onToggleHud,
                colors =
                    ButtonDefaults.buttonColors(
                        containerColor = Color(0xFF193227),
                        contentColor = Color(0xFFDCFFF1),
                    ),
            ) {
                Text(if (state.showHud) "Hide HUD" else "Show HUD")
            }

            Button(
                onClick = onToggleRotationLock,
                colors =
                    ButtonDefaults.buttonColors(
                        containerColor = Color(0xFF1C2B3A),
                        contentColor = Color(0xFFE9F2FF),
                    ),
            ) {
                Text(if (state.allowRotation) "Lock Rotation" else "Allow Rotation")
            }

            Button(
                onClick = onExit,
                colors =
                    ButtonDefaults.buttonColors(
                        containerColor = Color(0xFF2A1B1F),
                        contentColor = Color(0xFFFFE8EF),
                    ),
            ) {
                Text("Exit")
            }
        }

        Spacer(modifier = Modifier.size(2.dp))
        Text(
            text = "Double-tap to toggle menu",
            style = MaterialTheme.typography.labelMedium,
            color = Color(0xFF7CFAC6),
            fontFamily = FontFamily.Monospace,
        )
    }
}
