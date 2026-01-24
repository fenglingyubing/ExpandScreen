package com.expandscreen.ui

import android.content.pm.ActivityInfo
import android.os.Bundle
import android.opengl.GLSurfaceView
import android.content.ComponentName
import android.content.Intent
import android.content.ServiceConnection
import android.os.IBinder
import android.view.MotionEvent
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
import androidx.compose.ui.ExperimentalComposeUiApi
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.input.pointer.pointerInteropFilter
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import androidx.core.content.ContextCompat
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.lifecycleScope
import androidx.lifecycle.repeatOnLifecycle
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.expandscreen.core.input.TouchProcessor
import com.expandscreen.core.network.ConnectionState
import com.expandscreen.core.network.NetworkManager
import com.expandscreen.core.renderer.GLRenderer
import com.expandscreen.service.DisplayService
import com.expandscreen.ui.theme.ExpandScreenTheme
import dagger.hilt.android.AndroidEntryPoint
import javax.inject.Inject
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.filterNotNull
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

    private val boundService = MutableStateFlow<DisplayService?>(null)
    @Volatile private var pendingDecoderSurface: android.view.Surface? = null
    @Volatile private var isServiceBound: Boolean = false

    private val renderer =
        GLRenderer(
            onDecoderSurfaceReady = { surface ->
                pendingDecoderSurface = surface
                boundService.value?.setDecoderOutputSurface(surface)
            },
        )

    private var collectJob: Job? = null

    private val touchBatches = Channel<List<com.expandscreen.protocol.TouchEventMessage>>(capacity = 128)
    private var touchSendJob: Job? = null

    private val touchProcessor: TouchProcessor by lazy {
        TouchProcessor(
            screenWidthPxProvider = { getLandscapePixels().first },
            screenHeightPxProvider = { getLandscapePixels().second },
            viewWidthPxProvider = { glSurfaceView?.width ?: getLandscapePixels().first },
            viewHeightPxProvider = { glSurfaceView?.height ?: getLandscapePixels().second },
            videoWidthPxProvider = { uiState.value.videoWidth },
            videoHeightPxProvider = { uiState.value.videoHeight },
        )
    }

    private val serviceConnection =
        object : ServiceConnection {
            override fun onServiceConnected(name: ComponentName?, binder: IBinder?) {
                val local = binder as? DisplayService.DisplayServiceBinder ?: return
                boundService.value = local.getService()
                pendingDecoderSurface?.let { surface -> boundService.value?.setDecoderOutputSurface(surface) }
            }

            override fun onServiceDisconnected(name: ComponentName?) {
                boundService.value = null
            }
        }

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
                            boundService.value?.disconnect()
                            runCatching {
                                startService(DisplayService.disconnectAndStopIntent(this@DisplayActivity))
                            }
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
                        onMotionEvent = { motionEvent -> handleMotionEvent(motionEvent) },
                        glSurfaceViewProvider = { surfaceView ->
                            glSurfaceView = surfaceView
                            renderer.bindTo(surfaceView)
                        },
                    )
                }
            }
        }

        uiState.update { it.copy(allowRotation = allowRotation) }

        touchSendJob?.cancel()
        touchSendJob =
            lifecycleScope.launch(Dispatchers.Default) {
                for (batch in touchBatches) {
                    networkManager.sendTouchEvents(batch)
                }
            }
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

    override fun onStart() {
        super.onStart()
        ContextCompat.startForegroundService(this, DisplayService.startIntent(this))
        isServiceBound =
            bindService(
                Intent(this, DisplayService::class.java),
                serviceConnection,
                BIND_AUTO_CREATE,
            )
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
                    launch(Dispatchers.Default) {
                        boundService.filterNotNull().collect { bound ->
                            bound.state.collect { snapshot ->
                                renderer.setVideoSize(snapshot.videoWidth, snapshot.videoHeight)
                                uiState.update {
                                    it.copy(
                                        fps = snapshot.fps,
                                        latencyMs = snapshot.latencyMs,
                                        connectionState = snapshot.connectionState,
                                        videoWidth = snapshot.videoWidth,
                                        videoHeight = snapshot.videoHeight,
                                        memoryPssMb = snapshot.memoryPssMb,
                                        cpuUsagePercent = snapshot.cpuUsagePercent,
                                        decoderQueuedFrames = snapshot.decoderQueuedFrames,
                                        decoderDroppedFrames = snapshot.decoderDroppedFrames,
                                        decoderSkippedNonKeyFrames = snapshot.decoderSkippedNonKeyFrames,
                                        decoderCodecResets = snapshot.decoderCodecResets,
                                        decoderReconfigures = snapshot.decoderReconfigures,
                                    )
                                }
                                if (snapshot.connectionState == ConnectionState.Disconnected) {
                                    finish()
                                }
                            }
                        }
                    }
                }
            }
    }

    override fun onStop() {
        super.onStop()
        if (isServiceBound) {
            runCatching { unbindService(serviceConnection) }
            isServiceBound = false
        }
        boundService.value = null
    }

    override fun onPause() {
        super.onPause()
        Timber.d("DisplayActivity paused")
        collectJob?.cancel()
        collectJob = null
        glSurfaceView?.onPause()
        boundService.value?.setDecoderOutputSurface(null)
    }

    override fun onDestroy() {
        super.onDestroy()
        Timber.d("DisplayActivity destroyed - releasing resources")
        collectJob?.cancel()
        collectJob = null

        renderer.release()
        runCatching { touchBatches.close() }
        touchSendJob?.cancel()
        touchSendJob = null
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (hasFocus) {
            setupFullScreen()
        }
    }

    private companion object {
        const val EXTRA_ALLOW_ROTATION = "com.expandscreen.extra.ALLOW_ROTATION"
    }

    private fun getLandscapePixels(): Pair<Int, Int> {
        val metrics = resources.displayMetrics
        val w = metrics.widthPixels.coerceAtLeast(1)
        val h = metrics.heightPixels.coerceAtLeast(1)
        return if (w >= h) w to h else h to w
    }

    private fun handleMotionEvent(event: MotionEvent) {
        if (uiState.value.showMenu) return

        val messages = touchProcessor.process(event)
        if (messages.isEmpty()) return

        val isMoveOnly = messages.all { it.action == 1 }
        val ok = touchBatches.trySend(messages).isSuccess
        if (!ok && !isMoveOnly) {
            lifecycleScope.launch { touchBatches.send(messages) }
        }
    }
}

private data class DisplayUiState(
    val fps: Int = 0,
    val latencyMs: Int = 0,
    val videoWidth: Int = 0,
    val videoHeight: Int = 0,
    val memoryPssMb: Int = 0,
    val cpuUsagePercent: Int = 0,
    val decoderQueuedFrames: Int = 0,
    val decoderDroppedFrames: Long = 0,
    val decoderSkippedNonKeyFrames: Long = 0,
    val decoderCodecResets: Long = 0,
    val decoderReconfigures: Long = 0,
    val connectionState: ConnectionState = ConnectionState.Disconnected,
    val showHud: Boolean = true,
    val showMenu: Boolean = false,
    val allowRotation: Boolean = false,
)

@Composable
@OptIn(ExperimentalComposeUiApi::class)
private fun DisplayScreen(
    uiState: MutableStateFlow<DisplayUiState>,
    onExit: () -> Unit,
    onToggleHud: () -> Unit,
    onToggleRotationLock: () -> Unit,
    onToggleMenu: () -> Unit,
    onHideMenu: () -> Unit,
    onMotionEvent: (MotionEvent) -> Unit,
    glSurfaceViewProvider: (GLSurfaceView) -> Unit,
) {
    val state by uiState.collectAsStateWithLifecycle()

    val hudBrush =
        Brush.linearGradient(
            0.0f to Color(0xCC0B0F14),
            1.0f to Color(0xCC0E1A16),
        )

    Box(modifier = Modifier.fillMaxSize().background(Color.Black)) {
        AndroidView(
            modifier =
                Modifier
                    .fillMaxSize()
                    .pointerInput(Unit) {
                        detectTapGestures(
                            onDoubleTap = { onToggleMenu() },
                            onTap = { if (state.showMenu) onHideMenu() },
                        )
                    }
                    .pointerInteropFilter { motionEvent ->
                        if (!state.showMenu) {
                            onMotionEvent(motionEvent)
                        }
                        false
                    },
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
            text = "MEM ${state.memoryPssMb}MB  •  CPU ${state.cpuUsagePercent}%",
            style = MaterialTheme.typography.bodyMedium,
            color = Color(0xFFB8C7D9),
            fontFamily = FontFamily.Monospace,
        )
        Text(
            text =
                "DEC Q${state.decoderQueuedFrames}  DROP ${state.decoderDroppedFrames}  " +
                    "KF ${state.decoderSkippedNonKeyFrames}  RST ${state.decoderCodecResets}",
            style = MaterialTheme.typography.bodyMedium,
            color = Color(0xFFB9FFEA),
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
            text =
                "$connectionText • ${state.fps} FPS • ${state.latencyMs}ms • " +
                    "${state.memoryPssMb}MB • ${state.cpuUsagePercent}% CPU",
            style = MaterialTheme.typography.bodyMedium,
            color = Color(0xFFB9FFEA),
            fontFamily = FontFamily.Monospace,
        )

        Text(
            text =
                "Decoder: Q${state.decoderQueuedFrames} • DROP ${state.decoderDroppedFrames} • " +
                    "SKIP ${state.decoderSkippedNonKeyFrames} • RST ${state.decoderCodecResets} • " +
                    "CFG ${state.decoderReconfigures}",
            style = MaterialTheme.typography.bodySmall,
            color = Color(0xFFB8C7D9),
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
