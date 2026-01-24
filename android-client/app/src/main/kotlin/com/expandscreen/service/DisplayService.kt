package com.expandscreen.service

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.Context
import android.content.Intent
import android.os.Binder
import android.os.Build
import android.os.Debug
import android.os.IBinder
import android.os.PowerManager
import android.os.Process
import android.view.Surface
import androidx.core.app.NotificationCompat
import com.expandscreen.core.decoder.EncodedFrame
import com.expandscreen.core.decoder.FrameBufferPool
import com.expandscreen.core.decoder.H264Decoder
import com.expandscreen.core.decoder.VideoDecoderConfig
import com.expandscreen.core.network.ConnectionState
import com.expandscreen.core.network.IncomingMessage
import com.expandscreen.core.network.NetworkManager
import com.expandscreen.core.performance.PerformanceMode
import dagger.hilt.android.AndroidEntryPoint
import javax.inject.Inject
import kotlin.math.roundToInt
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import timber.log.Timber

/**
 * Display Service
 *
 * Foreground service that handles:
 * - Network data reception
 * - Video decoding
 * - Frame rendering coordination
 * - WakeLock management
 */
@AndroidEntryPoint
class DisplayService : Service() {

    @Inject lateinit var networkManager: NetworkManager

    private val binder = DisplayServiceBinder()
    private val serviceScope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
    private val processingMutex = Mutex()

    companion object {
        const val ACTION_START = "com.expandscreen.action.DISPLAY_START"
        const val ACTION_STOP = "com.expandscreen.action.DISPLAY_STOP"
        const val ACTION_DISCONNECT_AND_STOP = "com.expandscreen.action.DISPLAY_DISCONNECT_AND_STOP"

        private const val NOTIFICATION_CHANNEL_ID = "expandscreen_display"
        private const val NOTIFICATION_ID = 1001

        fun startIntent(context: Context): Intent {
            return Intent(context, DisplayService::class.java).setAction(ACTION_START)
        }

        fun disconnectAndStopIntent(context: Context): Intent {
            return Intent(context, DisplayService::class.java).setAction(ACTION_DISCONNECT_AND_STOP)
        }
    }

    private val decoder = H264Decoder()
    private val frameBufferPool = FrameBufferPool()

    private val decoderInitLock = Any()

    @Volatile
    private var decoderInitialized = false

    @Volatile
    private var outputSurface: Surface? = null

    private var processingJob: Job? = null
    private var connectionJob: Job? = null
    private var notificationJob: Job? = null
    private var metricsJob: Job? = null

    private var wakeLock: PowerManager.WakeLock? = null

    private val _state = MutableStateFlow(DisplayServiceState())
    val state: StateFlow<DisplayServiceState> = _state.asStateFlow()

    inner class DisplayServiceBinder : Binder() {
        fun getService(): DisplayService = this@DisplayService

        fun disconnect() {
            this@DisplayService.disconnect()
        }

        fun setPerformanceMode(mode: PerformanceMode) {
            this@DisplayService.setPerformanceMode(mode)
        }

        fun setDecoderOutputSurface(surface: Surface?) {
            this@DisplayService.setDecoderOutputSurface(surface)
        }

        fun stopAndCleanup() {
            this@DisplayService.stopAndCleanup(reason = "binder-stop")
        }
    }

    override fun onCreate() {
        super.onCreate()
        Timber.d("DisplayService created")
        createNotificationChannel()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        val action = intent?.action
        Timber.d("DisplayService onStartCommand action=$action")

        when (action) {
            ACTION_STOP -> {
                stopAndCleanup(reason = "intent-stop")
                return START_NOT_STICKY
            }

            ACTION_DISCONNECT_AND_STOP -> {
                disconnect()
                stopAndCleanup(reason = "intent-disconnect-and-stop")
                return START_NOT_STICKY
            }

            ACTION_START, null -> {
                // Continue below.
            }

            else -> Timber.w("Unknown action=$action")
        }

        // Start foreground service with notification
        startForeground(NOTIFICATION_ID, buildNotification(_state.value))
        startProcessingIfNeeded()

        return START_STICKY
    }

    override fun onBind(intent: Intent?): IBinder {
        return binder
    }

    override fun onDestroy() {
        super.onDestroy()
        Timber.d("DisplayService destroyed")
        stopAndCleanup(reason = "onDestroy")
    }

    private fun createNotificationChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val channel = NotificationChannel(
                NOTIFICATION_CHANNEL_ID,
                "ExpandScreen Display",
                NotificationManager.IMPORTANCE_LOW
            ).apply {
                description = "Displays connection status and performance metrics"
            }

            val notificationManager = getSystemService(NotificationManager::class.java)
            notificationManager.createNotificationChannel(channel)
        }
    }

    private fun buildNotification(state: DisplayServiceState): Notification {
        val statusText =
            when (state.connectionState) {
                ConnectionState.Connecting -> "Connecting…"
                is ConnectionState.Connected -> {
                    val error = state.lastError?.let { " • ERR" } ?: ""
                    "Streaming • ${state.fps} FPS • ${state.latencyMs}ms$error"
                }
                ConnectionState.Disconnected -> "Disconnected"
            }

        return NotificationCompat.Builder(this, NOTIFICATION_CHANNEL_ID)
            .setContentTitle("ExpandScreen")
            .setContentText(statusText)
            .setSmallIcon(com.expandscreen.R.drawable.ic_stat_expandscreen)
            .setPriority(NotificationCompat.PRIORITY_LOW)
            .setOngoing(true)
            .build()
    }

    fun setDecoderOutputSurface(surface: Surface?) {
        outputSurface = surface
        synchronized(decoderInitLock) {
            decoderInitialized = false
        }
    }

    fun disconnect() {
        networkManager.disconnect()
    }

    fun setPerformanceMode(mode: PerformanceMode) {
        if (_state.value.performanceMode == mode) return
        Timber.i("DisplayService performance mode -> ${mode.label}")
        _state.value = _state.value.copy(performanceMode = mode)

        synchronized(decoderInitLock) {
            decoderInitialized = false
        }
        runCatching { decoder.flush() }
            .onFailure { Timber.w(it, "Decoder flush failed after performance mode change") }
    }

    private fun startProcessingIfNeeded() {
        serviceScope.launch {
            processingMutex.withLock {
                if (processingJob?.isActive == true) return@withLock
                acquireWakeLock()
                startNotificationUpdates()
                startMetricsUpdates()
                startConnectionCollector()
                startVideoCollector()
            }
        }
    }

    private fun startConnectionCollector() {
        connectionJob?.cancel()
        connectionJob =
            serviceScope.launch(Dispatchers.Default) {
                var everConnected = false
                networkManager.connectionState.collect { connectionState ->
                    _state.value =
                        _state.value.copy(
                            connectionState = connectionState,
                            lastError = if (connectionState is ConnectionState.Connected) null else _state.value.lastError,
                        )
                    if (connectionState is ConnectionState.Connected) {
                        everConnected = true
                    }
                    if (connectionState == ConnectionState.Disconnected && everConnected) {
                        stopAndCleanup(reason = "disconnected")
                    }
                }
            }
    }

    private fun startVideoCollector() {
        processingJob?.cancel()
        processingJob =
            serviceScope.launch(Dispatchers.Default) {
                var windowStartMs = System.currentTimeMillis()
                var framesInWindow = 0
                var lastAcceptedFrameAtMs = 0L

                networkManager.incomingMessages.collect { incoming ->
                    if (incoming !is IncomingMessage.VideoFrame) return@collect

                    runCatching {
                        val nowMs = System.currentTimeMillis()
                        val mode = _state.value.performanceMode

                        if (mode == PerformanceMode.PowerSave) {
                            val minIntervalMs = (1_000L / mode.targetRenderFps.coerceAtLeast(1)).coerceAtLeast(1L)
                            val elapsedSinceLast = nowMs - lastAcceptedFrameAtMs
                            if (elapsedSinceLast < minIntervalMs && !incoming.message.isKeyFrame) {
                                return@collect
                            }
                        }

                        val latencyMs = (nowMs - incoming.timestampMs).coerceAtLeast(0).toInt()

                        _state.value =
                            _state.value.copy(
                                latencyMs = latencyMs,
                                videoWidth = incoming.message.width,
                                videoHeight = incoming.message.height,
                            )

                        val surface = outputSurface
                        if (surface != null && ensureDecoderInitialized(incoming.message.width, incoming.message.height, surface, mode)) {
                            val encodedFrame =
                                EncodedFrame.fromMessage(
                                    message = incoming.message,
                                    timestampMs = incoming.timestampMs,
                                    pool = frameBufferPool,
                                )
                            decoder.enqueueFrame(encodedFrame)
                        }

                        lastAcceptedFrameAtMs = nowMs
                        framesInWindow += 1
                        val elapsedMs = nowMs - windowStartMs
                        if (elapsedMs >= 1_000L) {
                            val fps = (framesInWindow * 1000f / elapsedMs.toFloat()).roundToInt()
                            _state.value = _state.value.copy(fps = fps)
                            framesInWindow = 0
                            windowStartMs = nowMs
                        }
                    }.onFailure { err ->
                        Timber.e(err, "Video processing failed")
                        _state.value = _state.value.copy(lastError = err.message ?: err.javaClass.simpleName)
                        runCatching { decoder.flush() }
                    }
                }
            }
    }

    private fun startNotificationUpdates() {
        notificationJob?.cancel()
        notificationJob =
            serviceScope.launch(Dispatchers.Default) {
                val manager = getSystemService(NotificationManager::class.java)
                while (isActive) {
                    manager.notify(NOTIFICATION_ID, buildNotification(state.value))
                    delay(1_000)
                }
            }
    }

    private fun startMetricsUpdates() {
        metricsJob?.cancel()
        metricsJob =
            serviceScope.launch(Dispatchers.Default) {
                var lastWallMs = System.currentTimeMillis()
                var lastCpuMs = Process.getElapsedCpuTime()
                val cores = Runtime.getRuntime().availableProcessors().coerceAtLeast(1)

                while (isActive) {
                    delay(1_000)
                    val nowWallMs = System.currentTimeMillis()
                    val nowCpuMs = Process.getElapsedCpuTime()

                    val wallDeltaMs = (nowWallMs - lastWallMs).coerceAtLeast(1)
                    val cpuDeltaMs = (nowCpuMs - lastCpuMs).coerceAtLeast(0)
                    lastWallMs = nowWallMs
                    lastCpuMs = nowCpuMs

                    val cpuPct =
                        ((cpuDeltaMs.toDouble() / (wallDeltaMs.toDouble() * cores.toDouble())) * 100.0)
                            .roundToInt()
                            .coerceIn(0, 999)
                    val pssMb = (Debug.getPss() / 1024L).coerceAtLeast(0L).toInt()
                    val stats = decoder.snapshotStats()

                    _state.value =
                        _state.value.copy(
                            memoryPssMb = pssMb,
                            cpuUsagePercent = cpuPct,
                            decoderQueuedFrames = stats.queuedFrames,
                            decoderDroppedFrames = stats.droppedFrames,
                            decoderSkippedNonKeyFrames = stats.skippedNonKeyFrames,
                            decoderCodecResets = stats.codecResets,
                            decoderReconfigures = stats.reconfigures,
                            lastError = _state.value.lastError ?: stats.lastError,
                        )
                }
            }
    }

    private fun ensureDecoderInitialized(width: Int, height: Int, surface: Surface, mode: PerformanceMode): Boolean {
        synchronized(decoderInitLock) {
            if (decoderInitialized) return true
            decoder.initialize(
                VideoDecoderConfig(
                    width = width.coerceAtLeast(1),
                    height = height.coerceAtLeast(1),
                    outputSurface = surface,
                    lowLatency = mode.lowLatency,
                    operatingRate = mode.decoderOperatingRateFps,
                ),
            )
            decoderInitialized = true
            return true
        }
    }

    private fun acquireWakeLock() {
        if (wakeLock?.isHeld == true) return
        val power = getSystemService(PowerManager::class.java)
        wakeLock =
            power.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "ExpandScreen:DisplayService").apply {
                setReferenceCounted(false)
                acquire()
            }
    }

    private fun releaseWakeLock() {
        val lock = wakeLock ?: return
        wakeLock = null
        runCatching {
            if (lock.isHeld) lock.release()
        }.onFailure { Timber.w(it, "WakeLock release failed") }
    }

    private fun stopAndCleanup(reason: String) {
        Timber.i("DisplayService stopAndCleanup reason=$reason")
        processingJob?.cancel()
        processingJob = null
        connectionJob?.cancel()
        connectionJob = null
        notificationJob?.cancel()
        notificationJob = null
        metricsJob?.cancel()
        metricsJob = null

        synchronized(decoderInitLock) {
            decoderInitialized = false
        }
        decoder.release()
        releaseWakeLock()

        runCatching { stopForeground(STOP_FOREGROUND_REMOVE) }
        stopSelf()
    }
}
