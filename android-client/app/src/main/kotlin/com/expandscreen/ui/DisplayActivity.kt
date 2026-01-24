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
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.offset
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
import androidx.compose.runtime.mutableStateOf
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
import androidx.compose.ui.unit.IntOffset
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
import com.expandscreen.core.performance.PerformanceMode
import com.expandscreen.core.renderer.GLRenderer
import com.expandscreen.data.repository.PerformancePreset
import com.expandscreen.data.repository.SettingsRepository
import com.expandscreen.service.DisplayService
import com.expandscreen.ui.theme.ExpandScreenTheme
import dagger.hilt.android.AndroidEntryPoint
import javax.inject.Inject
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.filterNotNull
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlin.math.roundToInt
import timber.log.Timber

/**
 * Display Activity - Full screen video display
 *
 * This activity displays the decoded video stream from the Windows PC
 * in full-screen mode using OpenGL ES rendering.
 */
@AndroidEntryPoint
class DisplayActivity : ComponentActivity() {
    companion object {
        const val EXTRA_DEVICE_ID = "com.expandscreen.extra.DEVICE_ID"
        const val EXTRA_CONNECTION_TYPE = "com.expandscreen.extra.CONNECTION_TYPE"
    }

    private val logDeviceId: Long? by lazy {
        intent?.getLongExtra(EXTRA_DEVICE_ID, -1L)?.takeIf { it > 0L }
    }

    private val logConnectionType: String? by lazy {
        intent?.getStringExtra(EXTRA_CONNECTION_TYPE)?.takeIf { it.isNotBlank() }
    }


    @Inject lateinit var networkManager: NetworkManager
    @Inject lateinit var settingsRepository: SettingsRepository

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
    private var settingsJob: Job? = null

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
            minMoveIntervalMsProvider = { uiState.value.performanceMode.touchMinMoveIntervalMs },
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

        val initialSettings = settingsRepository.settings.value
        applyKeepScreenOn(initialSettings.display.keepScreenOn)
        applyFullScreen(initialSettings.display.fullScreen)
        applyOrientationPolicy(initialSettings.display.allowRotation)

        setContent {
            val settings by settingsRepository.settings.collectAsStateWithLifecycle()
            ExpandScreenTheme(
                themeMode = settings.display.themeMode,
                dynamicColor = settings.display.dynamicColor,
            ) {
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
                            settingsRepository.setAllowRotation(newAllowRotation)
                            uiState.update { it.copy(allowRotation = newAllowRotation) }
                        },
                        onToggleMenu = { uiState.update { it.copy(showMenu = !it.showMenu) } },
                        onHideMenu = { uiState.update { it.copy(showMenu = false) } },
                        onSetPerformanceMode = { mode ->
                            uiState.update { it.copy(performanceMode = mode) }
                            applyBrightnessForMode(mode)
                            settingsRepository.setPerformancePreset(mode.toPreset())
                            boundService.value?.setPerformanceMode(mode)
                        },
                        onMotionEvent = { motionEvent -> handleMotionEvent(motionEvent) },
                        glSurfaceViewProvider = { surfaceView ->
                            glSurfaceView = surfaceView
                            renderer.bindTo(surfaceView)
                        },
                    )
                }
            }
        }

        uiState.update { it.copy(allowRotation = initialSettings.display.allowRotation) }
        applyBrightnessForMode(uiState.value.performanceMode)

        touchSendJob?.cancel()
        touchSendJob =
            lifecycleScope.launch(Dispatchers.Default) {
                for (batch in touchBatches) {
                    networkManager.sendTouchEvents(batch)
                }
            }
    }

    private fun setupFullScreen() {
        applyFullScreen(true)
    }

    private fun applyFullScreen(enabled: Boolean) {
        WindowCompat.setDecorFitsSystemWindows(window, !enabled)
        WindowInsetsControllerCompat(window, window.decorView).let { controller ->
            if (enabled) {
                controller.hide(WindowInsetsCompat.Type.systemBars())
                controller.systemBarsBehavior =
                    WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
            } else {
                controller.show(WindowInsetsCompat.Type.systemBars())
            }
        }
    }

    private fun applyKeepScreenOn(enabled: Boolean) {
        if (enabled) {
            window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        } else {
            window.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
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

    private fun applyBrightnessForMode(mode: PerformanceMode) {
        val attrs = window.attributes
        attrs.screenBrightness =
            when (mode) {
                PerformanceMode.PowerSave -> 0.6f
                PerformanceMode.Balanced, PerformanceMode.Performance -> WindowManager.LayoutParams.BRIGHTNESS_OVERRIDE_NONE
            }
        window.attributes = attrs
    }

    override fun onStart() {
        super.onStart()
        val startIntent =
            if (logDeviceId != null && logConnectionType != null) {
                DisplayService.startIntent(this, logDeviceId!!, logConnectionType!!)
            } else {
                DisplayService.startIntent(this)
            }
        ContextCompat.startForegroundService(this, startIntent)
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

        applyFullScreen(settingsRepository.settings.value.display.fullScreen)
        glSurfaceView?.onResume()

        settingsJob?.cancel()
        settingsJob =
            lifecycleScope.launch {
                repeatOnLifecycle(Lifecycle.State.STARTED) {
                    settingsRepository.settings.collectLatest { settings ->
                        applyKeepScreenOn(settings.display.keepScreenOn)
                        applyFullScreen(settings.display.fullScreen)
                        applyOrientationPolicy(settings.display.allowRotation)
                        uiState.update { it.copy(allowRotation = settings.display.allowRotation) }
                    }
                }
            }

        collectJob?.cancel()
        collectJob =
            lifecycleScope.launch {
                repeatOnLifecycle(Lifecycle.State.STARTED) {
                    launch(Dispatchers.Default) {
                        boundService.filterNotNull().collect { bound ->
                            bound.state.collect { snapshot ->
                                renderer.setVideoSize(snapshot.videoWidth, snapshot.videoHeight)
                                val previousMode = uiState.value.performanceMode
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
                                        performanceMode = snapshot.performanceMode,
                                    )
                                }
                                if (previousMode != snapshot.performanceMode) {
                                    lifecycleScope.launch { applyBrightnessForMode(snapshot.performanceMode) }
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
        settingsJob?.cancel()
        settingsJob = null
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
            applyFullScreen(settingsRepository.settings.value.display.fullScreen)
        }
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

    private fun PerformanceMode.toPreset(): PerformancePreset {
        return when (this) {
            PerformanceMode.Performance -> PerformancePreset.Performance
            PerformanceMode.Balanced -> PerformancePreset.Balanced
            PerformanceMode.PowerSave -> PerformancePreset.PowerSave
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
    val performanceMode: PerformanceMode = PerformanceMode.Balanced,
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
    onSetPerformanceMode: (PerformanceMode) -> Unit,
    onMotionEvent: (MotionEvent) -> Unit,
    glSurfaceViewProvider: (GLSurfaceView) -> Unit,
) {
    val state by uiState.collectAsStateWithLifecycle()
    val hudOffset = androidx.compose.runtime.remember { mutableStateOf(IntOffset.Zero) }
    val showRotationToast = androidx.compose.runtime.remember { mutableStateOf(false) }

    androidx.compose.runtime.LaunchedEffect(state.allowRotation) {
        showRotationToast.value = true
        delay(900)
        showRotationToast.value = false
    }

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
                            onLongPress = { onToggleMenu() },
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
            visible = state.connectionState == ConnectionState.Connecting || state.videoWidth <= 0,
            enter = fadeIn(),
            exit = fadeOut(),
        ) {
            DisplayLoadingOverlay(
                modifier = Modifier.align(Alignment.Center),
                state = state,
                brush = hudBrush,
            )
        }

        AnimatedVisibility(
            visible = showRotationToast.value && !state.showMenu,
            enter = fadeIn(),
            exit = fadeOut(),
        ) {
            RotationToast(
                modifier = Modifier.align(Alignment.TopCenter).padding(top = 18.dp),
                brush = hudBrush,
                allowRotation = state.allowRotation,
            )
        }

        AnimatedVisibility(
            visible = state.showHud && !state.showMenu,
            enter = fadeIn(),
            exit = fadeOut(),
        ) {
            HudPanel(
                modifier =
                    Modifier
                        .align(Alignment.TopStart)
                        .padding(14.dp)
                        .offset { hudOffset.value }
                        .pointerInput(Unit) {
                            detectDragGestures { change, dragAmount ->
                                change.consume()
                                hudOffset.value =
                                    hudOffset.value +
                                        IntOffset(
                                            dragAmount.x.roundToInt(),
                                            dragAmount.y.roundToInt(),
                                        )
                            }
                        },
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
                onSetPerformanceMode = onSetPerformanceMode,
            )
        }
    }
}

@Composable
private fun DisplayLoadingOverlay(
    modifier: Modifier,
    state: DisplayUiState,
    brush: Brush,
) {
    val title =
        when (state.connectionState) {
            ConnectionState.Connecting -> "Connecting…"
            is ConnectionState.Connected -> "Starting stream…"
            ConnectionState.Disconnected -> "Disconnected"
        }

    Column(
        modifier =
            modifier
                .clip(RoundedCornerShape(18.dp))
                .background(brush)
                .padding(horizontal = 16.dp, vertical = 14.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Text(
            text = title,
            style = MaterialTheme.typography.titleMedium,
            color = Color(0xFFE9F2FF),
        )
        Text(
            text = "Tip: double-tap or long-press to open menu",
            style = MaterialTheme.typography.bodyMedium,
            color = Color(0xFFB8C7D9),
            fontFamily = FontFamily.Monospace,
        )
    }
}

@Composable
private fun RotationToast(
    modifier: Modifier,
    brush: Brush,
    allowRotation: Boolean,
) {
    val text = if (allowRotation) "Rotation: ALLOWED" else "Rotation: LOCKED"
    Text(
        text = text,
        modifier =
            modifier
                .clip(RoundedCornerShape(999.dp))
                .background(brush)
                .padding(horizontal = 12.dp, vertical = 8.dp),
        style = MaterialTheme.typography.labelLarge,
        color = Color(0xFFB9FFEA),
        fontFamily = FontFamily.Monospace,
    )
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
    onSetPerformanceMode: (PerformanceMode) -> Unit,
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

        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            ModeChip(
                label = "PERF",
                selected = state.performanceMode == PerformanceMode.Performance,
                accent = Color(0xFF7CFAC6),
                onClick = { onSetPerformanceMode(PerformanceMode.Performance) },
            )
            ModeChip(
                label = "BAL",
                selected = state.performanceMode == PerformanceMode.Balanced,
                accent = Color(0xFFE9F2FF),
                onClick = { onSetPerformanceMode(PerformanceMode.Balanced) },
            )
            ModeChip(
                label = "SAVE",
                selected = state.performanceMode == PerformanceMode.PowerSave,
                accent = Color(0xFFFFD166),
                onClick = { onSetPerformanceMode(PerformanceMode.PowerSave) },
            )
        }

        Text(
            text = "Mode: ${state.performanceMode.label} • touch ${state.performanceMode.touchMinMoveIntervalMs}ms • ${state.performanceMode.targetRenderFps}fps cap",
            style = MaterialTheme.typography.labelMedium,
            color = Color(0xFF7CFAC6),
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

@Composable
private fun ModeChip(
    label: String,
    selected: Boolean,
    accent: Color,
    onClick: () -> Unit,
) {
    val bg =
        if (selected) {
            Brush.linearGradient(
                0.0f to accent.copy(alpha = 0.22f),
                1.0f to Color(0xFF0B0F14),
            )
        } else {
            Brush.linearGradient(
                0.0f to Color(0xFF0B0F14).copy(alpha = 0.8f),
                1.0f to Color(0xFF0B0F14),
            )
        }

    Box(
        modifier =
            Modifier
                .clip(RoundedCornerShape(999.dp))
                .background(bg)
                .clickable(onClick = onClick)
                .padding(horizontal = 10.dp, vertical = 6.dp)
    ) {
        Text(
            text = label,
            style = MaterialTheme.typography.labelLarge,
            color = if (selected) accent else Color(0xFFB8C7D9),
            fontFamily = FontFamily.Monospace,
        )
    }
}
