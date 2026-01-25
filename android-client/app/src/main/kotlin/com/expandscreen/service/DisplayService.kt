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
import com.expandscreen.core.audio.AudioDecoderConfig
import com.expandscreen.core.audio.AudioPlayer
import com.expandscreen.core.audio.EncodedAudioFrame
import com.expandscreen.core.audio.MediaCodecAudioDecoder
import com.expandscreen.core.decoder.EncodedFrame
import com.expandscreen.core.decoder.FrameBufferPool
import com.expandscreen.core.decoder.H264Decoder
import com.expandscreen.core.decoder.VideoDecoderConfig
import com.expandscreen.core.network.ConnectionState
import com.expandscreen.core.network.IncomingMessage
import com.expandscreen.core.network.NetworkManager
import com.expandscreen.core.performance.PerformanceMode
import com.expandscreen.protocol.AudioFrameMessage
import dagger.hilt.android.AndroidEntryPoint
import com.expandscreen.data.repository.ConnectionLogRepository
import com.expandscreen.data.repository.PerformancePreset
import com.expandscreen.data.repository.SettingsRepository
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
    @Inject lateinit var settingsRepository: SettingsRepository
    @Inject lateinit var connectionLogRepository: ConnectionLogRepository

    private val binder = DisplayServiceBinder()
    private val serviceScope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
    private val processingMutex = Mutex()
    private val cleanupMutex = Mutex()

    @Volatile
    private var cleanedUp: Boolean = false

    companion object {
        const val ACTION_START = "com.expandscreen.action.DISPLAY_START"
        const val ACTION_STOP = "com.expandscreen.action.DISPLAY_STOP"
        const val ACTION_DISCONNECT_AND_STOP = "com.expandscreen.action.DISPLAY_DISCONNECT_AND_STOP"

        private const val EXTRA_DEVICE_ID = "com.expandscreen.extra.DEVICE_ID"
        private const val EXTRA_CONNECTION_TYPE = "com.expandscreen.extra.CONNECTION_TYPE"

        private const val NOTIFICATION_CHANNEL_ID = "expandscreen_display"
        private const val NOTIFICATION_ID = 1001

        fun startIntent(context: Context): Intent {
            return Intent(context, DisplayService::class.java).setAction(ACTION_START)
        }

        fun startIntent(
            context: Context,
            deviceId: Long,
            connectionType: String,
        ): Intent {
            return Intent(context, DisplayService::class.java)
                .setAction(ACTION_START)
                .putExtra(EXTRA_DEVICE_ID, deviceId)
                .putExtra(EXTRA_CONNECTION_TYPE, connectionType)
        }

        fun disconnectAndStopIntent(context: Context): Intent {
            return Intent(context, DisplayService::class.java).setAction(ACTION_DISCONNECT_AND_STOP)
        }
    }

    private val decoder = H264Decoder()
    private val frameBufferPool = FrameBufferPool()
    private lateinit var audioPlayer: AudioPlayer
    private lateinit var audioDecoder: MediaCodecAudioDecoder

    private val decoderInitLock = Any()
    private val audioInitLock = Any()

    @Volatile
    private var decoderInitialized = false

    @Volatile
    private var audioInitialized = false

    @Volatile
    private var audioDecoderConfig: AudioDecoderConfig? = null

    @Volatile
    private var outputSurface: Surface? = null

    private var processingJob: Job? = null
    private var connectionJob: Job? = null
    private var notificationJob: Job? = null
    private var metricsJob: Job? = null
    private var settingsJob: Job? = null
    private var connectionLogJob: Job? = null

    private var wakeLock: PowerManager.WakeLock? = null

    @Volatile
    private var decoderLowLatency: Boolean = true

    private val _state = MutableStateFlow(DisplayServiceState())
    val state: StateFlow<DisplayServiceState> = _state.asStateFlow()

    @Volatile
    private var logDeviceId: Long? = null

    @Volatile
    private var logConnectionType: String? = null

    @Volatile
    private var activeLogId: Long? = null

    @Volatile
    private var activeLogStartTimeMs: Long? = null

    @Volatile
    private var samples: Long = 0

    @Volatile
    private var fpsSum: Long = 0

    @Volatile
    private var latencySum: Long = 0

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
            this@DisplayService.stopAndCleanupAsync(reason = "binder-stop")
        }
    }

    override fun onCreate() {
        super.onCreate()
        Timber.d("DisplayService created")
        createNotificationChannel()
        startSettingsCollector()
        audioPlayer = AudioPlayer(applicationContext)
        audioDecoder = MediaCodecAudioDecoder(onPcmFrame = audioPlayer::enqueue)
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        val action = intent?.action
        Timber.d("DisplayService onStartCommand action=$action")

        extractLogContext(intent)

        when (action) {
            ACTION_STOP -> {
                stopAndCleanupAsync(reason = "intent-stop")
                return START_NOT_STICKY
            }

            ACTION_DISCONNECT_AND_STOP -> {
                disconnect()
                stopAndCleanupAsync(reason = "intent-disconnect-and-stop")
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
        stopAndCleanupAsync(reason = "onDestroy")
    }

    private fun startSettingsCollector() {
        settingsJob?.cancel()
        settingsJob =
            serviceScope.launch(Dispatchers.Default) {
                settingsRepository.settings.collect { settings ->
                    val mode = settings.performance.preset.toPerformanceMode()
                    if (state.value.performanceMode != mode) {
                        setPerformanceMode(mode)
                    }

                    val nextLowLatency = settings.performance.lowLatencyMode
                    if (decoderLowLatency != nextLowLatency) {
                        decoderLowLatency = nextLowLatency
                        synchronized(decoderInitLock) { decoderInitialized = false }
                        runCatching { decoder.flush() }
                            .onFailure { Timber.w(it, "Decoder flush failed after low-latency setting change") }
                    }
                }
            }
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
                startMediaCollector()
            }
        }
    }

    private fun startConnectionCollector() {
        connectionJob?.cancel()
        connectionJob =
            serviceScope.launch(Dispatchers.Default) {
                var everConnected = false
                var previousState: ConnectionState = ConnectionState.Disconnected
                networkManager.connectionState.collect { connectionState ->
                    _state.value =
                        _state.value.copy(
                            connectionState = connectionState,
                            lastError = if (connectionState is ConnectionState.Connected) null else _state.value.lastError,
                        )
                    if (connectionState is ConnectionState.Connected) {
                        if (previousState !is ConnectionState.Connected) {
                            startActiveLogIfPossible()
                        }
                        everConnected = true
                    }
                    if (connectionState == ConnectionState.Disconnected && everConnected) {
                        finalizeActiveLogIfNeeded(endReason = "disconnected")
                        stopAndCleanupAsync(reason = "disconnected")
                    }
                    previousState = connectionState
                }
            }
    }

    private fun extractLogContext(intent: Intent?) {
        val incomingDeviceId = intent?.getLongExtra(EXTRA_DEVICE_ID, -1L) ?: -1L
        val incomingType = intent?.getStringExtra(EXTRA_CONNECTION_TYPE)

        if (incomingDeviceId > 0L) {
            logDeviceId = incomingDeviceId
        }
        if (!incomingType.isNullOrBlank()) {
            logConnectionType = incomingType
        }
    }

    private suspend fun startActiveLogIfPossible() {
        if (activeLogId != null) return
        val deviceId = logDeviceId ?: return
        val type = logConnectionType ?: return

        val startTimeMs = System.currentTimeMillis()
        val logId = connectionLogRepository.startLog(deviceId = deviceId, connectionType = type, startTimeMs = startTimeMs)
        activeLogId = logId
        activeLogStartTimeMs = startTimeMs

        samples = 0
        fpsSum = 0
        latencySum = 0

        connectionLogJob?.cancel()
        connectionLogJob =
            serviceScope.launch(Dispatchers.Default) {
                while (isActive) {
                    delay(1_000)
                    val snapshot = _state.value
                    fpsSum += snapshot.fps.toLong().coerceAtLeast(0L)
                    latencySum += snapshot.latencyMs.toLong().coerceAtLeast(0L)
                    samples += 1
                }
            }
    }

    private suspend fun finalizeActiveLogIfNeeded(endReason: String) {
        val logId = activeLogId ?: return
        val deviceId = logDeviceId ?: return
        val type = logConnectionType ?: return
        val startTimeMs = activeLogStartTimeMs ?: return

        Timber.i("Finalizing connection log id=$logId reason=$endReason")

        connectionLogJob?.cancel()
        connectionLogJob = null

        val sampleCount = samples
        val avgFps =
            if (sampleCount > 0) (fpsSum.toFloat() / sampleCount.toFloat()) else null
        val avgLatencyMs =
            if (sampleCount > 0) (latencySum / sampleCount).toInt() else null

        val endTimeMs = System.currentTimeMillis()
        connectionLogRepository.endLog(
            logId = logId,
            deviceId = deviceId,
            connectionType = type,
            startTimeMs = startTimeMs,
            endTimeMs = endTimeMs,
            avgFps = avgFps,
            avgLatencyMs = avgLatencyMs,
        )

        activeLogId = null
        activeLogStartTimeMs = null
        samples = 0
        fpsSum = 0
        latencySum = 0
    }

    private fun startMediaCollector() {
        processingJob?.cancel()
        processingJob =
            serviceScope.launch(Dispatchers.Default) {
                var windowStartMs = System.currentTimeMillis()
                var framesInWindow = 0
                var lastAcceptedFrameAtMs = 0L

                networkManager.incomingMessages.collect { incoming ->
                    runCatching {
                        val nowMs = System.currentTimeMillis()
                        val mode = _state.value.performanceMode

                        when (incoming) {
                            is IncomingMessage.VideoFrame -> {
                                if (mode == PerformanceMode.PowerSave) {
                                    val minIntervalMs =
                                        (1_000L / mode.targetRenderFps.coerceAtLeast(1))
                                            .coerceAtLeast(1L)
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
                            }

                            is IncomingMessage.AudioFrame -> {
                                if (ensureAudioDecoderInitialized(incoming.message)) {
                                    val encoded =
                                        EncodedAudioFrame.fromMessage(
                                            message = incoming.message,
                                            timestampMs = incoming.timestampMs,
                                            pool = frameBufferPool,
                                        )
                                    audioDecoder.enqueueFrame(encoded)
                                }
                            }

                            else -> return@collect
                        }
                    }.onFailure { err ->
                        Timber.e(err, "Media processing failed")
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
                    lowLatency = decoderLowLatency,
                    operatingRate = mode.decoderOperatingRateFps,
                ),
            )
            decoderInitialized = true
            return true
        }
    }

    private fun ensureAudioDecoderInitialized(message: AudioFrameMessage): Boolean {
        synchronized(audioInitLock) {
            val nextConfig =
                AudioDecoderConfig(
                    sampleRate = message.sampleRate.coerceAtLeast(1),
                    channelCount = message.channelCount.coerceAtLeast(1),
                    mimeType = message.mimeType,
                    codecConfig0 = message.codecConfig0,
                    codecConfig1 = message.codecConfig1,
                    lowLatency = true,
                )

            if (!audioInitialized) {
                audioPlayer.start()
                audioDecoder.initialize(nextConfig)
                audioDecoderConfig = nextConfig
                audioInitialized = true
                return true
            }

            if (audioDecoderConfig != nextConfig) {
                audioDecoder.initialize(nextConfig)
                audioDecoderConfig = nextConfig
            }
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
        connectionLogJob?.cancel()
        connectionLogJob = null
        settingsJob?.cancel()
        settingsJob = null

        synchronized(decoderInitLock) {
            decoderInitialized = false
        }
        synchronized(audioInitLock) {
            audioInitialized = false
            audioDecoderConfig = null
        }
        decoder.release()
        audioDecoder.release()
        audioPlayer.release()
        releaseWakeLock()

        runCatching { stopForeground(STOP_FOREGROUND_REMOVE) }
        stopSelf()
    }

    private fun stopAndCleanupAsync(reason: String) {
        serviceScope.launch(Dispatchers.Default) {
            val shouldRun =
                cleanupMutex.withLock {
                    if (cleanedUp) return@withLock false
                    cleanedUp = true
                    true
                }
            if (!shouldRun) return@launch

            runCatching { finalizeActiveLogIfNeeded(endReason = reason) }
                .onFailure { Timber.w(it, "Finalize connection log failed") }

            stopAndCleanup(reason = reason)
        }
    }

    private fun PerformancePreset.toPerformanceMode(): PerformanceMode {
        return when (this) {
            PerformancePreset.Performance -> PerformanceMode.Performance
            PerformancePreset.Balanced -> PerformanceMode.Balanced
            PerformancePreset.PowerSave -> PerformanceMode.PowerSave
        }
    }
}
