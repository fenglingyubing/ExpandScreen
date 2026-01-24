package com.expandscreen.data.repository

import android.content.Context
import android.content.SharedPreferences
import dagger.hilt.android.qualifiers.ApplicationContext
import javax.inject.Inject
import javax.inject.Singleton
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.serialization.SerializationException
import kotlinx.serialization.json.Json

@Singleton
class SharedPreferencesSettingsRepository @Inject constructor(
    @ApplicationContext private val context: Context,
) : SettingsRepository {

    private val prefs: SharedPreferences =
        context.getSharedPreferences(SettingsRepository.PREFS_NAME, Context.MODE_PRIVATE)

    private val json =
        Json {
            prettyPrint = true
            encodeDefaults = true
            ignoreUnknownKeys = true
        }

    private val compactJson =
        Json {
            prettyPrint = false
            encodeDefaults = true
            ignoreUnknownKeys = true
        }

    private val _settings = MutableStateFlow(readFromPrefs(prefs))
    override val settings: StateFlow<AppSettings> = _settings.asStateFlow()

    private val listener =
        SharedPreferences.OnSharedPreferenceChangeListener { sharedPreferences, _ ->
            _settings.value = readFromPrefs(sharedPreferences)
        }

    init {
        prefs.registerOnSharedPreferenceChangeListener(listener)
    }

    override fun setVideoResolution(resolution: VideoResolution) {
        update { it.copy(video = it.video.copy(resolution = resolution)) }
    }

    override fun setVideoFrameRate(frameRate: VideoFrameRate) {
        update { it.copy(video = it.video.copy(frameRate = frameRate)) }
    }

    override fun setVideoQuality(quality: VideoQuality) {
        update { it.copy(video = it.video.copy(quality = quality)) }
    }

    override fun setPerformancePreset(preset: PerformancePreset) {
        update { it.copy(performance = it.performance.copy(preset = preset)) }
    }

    override fun setLowLatencyMode(enabled: Boolean) {
        update { it.copy(performance = it.performance.copy(lowLatencyMode = enabled)) }
    }

    override fun setKeepScreenOn(enabled: Boolean) {
        update { it.copy(display = it.display.copy(keepScreenOn = enabled)) }
    }

    override fun setAllowRotation(enabled: Boolean) {
        update { it.copy(display = it.display.copy(allowRotation = enabled)) }
    }

    override fun setFullScreen(enabled: Boolean) {
        update { it.copy(display = it.display.copy(fullScreen = enabled)) }
    }

    override fun setPreferredConnection(connection: PreferredConnection) {
        update { it.copy(network = it.network.copy(preferredConnection = connection)) }
    }

    override fun setAutoReconnect(enabled: Boolean) {
        update { it.copy(network = it.network.copy(autoReconnect = enabled)) }
    }

    override fun exportToJson(pretty: Boolean): String {
        val settings = _settings.value
        return (if (pretty) json else compactJson).encodeToString(AppSettings.serializer(), settings)
    }

    override fun importFromJson(json: String): Result<Unit> {
        val parsed =
            try {
                this.json.decodeFromString(AppSettings.serializer(), json)
            } catch (e: SerializationException) {
                return Result.failure(e)
            }

        writeToPrefs(parsed)
        _settings.value = parsed
        return Result.success(Unit)
    }

    private fun update(block: (AppSettings) -> AppSettings) {
        val next = block(_settings.value)
        writeToPrefs(next)
        _settings.value = next
    }

    private fun readFromPrefs(prefs: SharedPreferences): AppSettings {
        val default = AppSettings()

        val video =
            default.video.copy(
                resolution = parseResolution(prefs.getString(SettingsRepository.VIDEO_RESOLUTION, null)),
                frameRate = parseFrameRate(prefs.getString(SettingsRepository.VIDEO_FRAME_RATE, null)),
                quality = parseQuality(prefs.getString(SettingsRepository.VIDEO_QUALITY, null)),
            )

        val performance =
            default.performance.copy(
                preset = parsePerformancePreset(prefs.getString(SettingsRepository.PERF_PRESET, null)),
                lowLatencyMode = prefs.getBoolean(SettingsRepository.PERF_LOW_LATENCY, default.performance.lowLatencyMode),
            )

        val display =
            default.display.copy(
                keepScreenOn = prefs.getBoolean(SettingsRepository.DISPLAY_KEEP_SCREEN_ON, default.display.keepScreenOn),
                allowRotation = prefs.getBoolean(SettingsRepository.DISPLAY_ALLOW_ROTATION, default.display.allowRotation),
                fullScreen = prefs.getBoolean(SettingsRepository.DISPLAY_FULLSCREEN, default.display.fullScreen),
            )

        val network =
            default.network.copy(
                preferredConnection =
                    parsePreferredConnection(
                        prefs.getString(SettingsRepository.NETWORK_PREFERRED_CONNECTION, null),
                    ),
                autoReconnect = prefs.getBoolean(SettingsRepository.NETWORK_AUTO_RECONNECT, default.network.autoReconnect),
            )

        return AppSettings(video = video, performance = performance, display = display, network = network)
    }

    private fun writeToPrefs(settings: AppSettings) {
        prefs
            .edit()
            .putString(SettingsRepository.VIDEO_RESOLUTION, resolutionPrefValue(settings.video.resolution))
            .putString(SettingsRepository.VIDEO_FRAME_RATE, frameRatePrefValue(settings.video.frameRate))
            .putString(SettingsRepository.VIDEO_QUALITY, qualityPrefValue(settings.video.quality))
            .putString(SettingsRepository.PERF_PRESET, performancePresetPrefValue(settings.performance.preset))
            .putBoolean(SettingsRepository.PERF_LOW_LATENCY, settings.performance.lowLatencyMode)
            .putBoolean(SettingsRepository.DISPLAY_KEEP_SCREEN_ON, settings.display.keepScreenOn)
            .putBoolean(SettingsRepository.DISPLAY_ALLOW_ROTATION, settings.display.allowRotation)
            .putBoolean(SettingsRepository.DISPLAY_FULLSCREEN, settings.display.fullScreen)
            .putString(
                SettingsRepository.NETWORK_PREFERRED_CONNECTION,
                preferredConnectionPrefValue(settings.network.preferredConnection),
            )
            .putBoolean(SettingsRepository.NETWORK_AUTO_RECONNECT, settings.network.autoReconnect)
            .apply()
    }

    private fun parseResolution(value: String?): VideoResolution {
        return when (value) {
            "1280x720" -> VideoResolution.R1280x720
            "1920x1080" -> VideoResolution.R1920x1080
            "2560x1600" -> VideoResolution.R2560x1600
            else -> VideoResolution.Auto
        }
    }

    private fun resolutionPrefValue(resolution: VideoResolution): String {
        return when (resolution) {
            VideoResolution.Auto -> "auto"
            VideoResolution.R1280x720 -> "1280x720"
            VideoResolution.R1920x1080 -> "1920x1080"
            VideoResolution.R2560x1600 -> "2560x1600"
        }
    }

    private fun parseFrameRate(value: String?): VideoFrameRate {
        return when (value) {
            "30" -> VideoFrameRate.Fps30
            "60" -> VideoFrameRate.Fps60
            "120" -> VideoFrameRate.Fps120
            else -> VideoFrameRate.Auto
        }
    }

    private fun frameRatePrefValue(frameRate: VideoFrameRate): String {
        return when (frameRate) {
            VideoFrameRate.Auto -> "auto"
            VideoFrameRate.Fps30 -> "30"
            VideoFrameRate.Fps60 -> "60"
            VideoFrameRate.Fps120 -> "120"
        }
    }

    private fun parseQuality(value: String?): VideoQuality {
        return when (value) {
            "low" -> VideoQuality.Low
            "high" -> VideoQuality.High
            else -> VideoQuality.Balanced
        }
    }

    private fun qualityPrefValue(quality: VideoQuality): String {
        return when (quality) {
            VideoQuality.Low -> "low"
            VideoQuality.Balanced -> "balanced"
            VideoQuality.High -> "high"
        }
    }

    private fun parsePerformancePreset(value: String?): PerformancePreset {
        return when (value) {
            "power_save" -> PerformancePreset.PowerSave
            "performance" -> PerformancePreset.Performance
            else -> PerformancePreset.Balanced
        }
    }

    private fun performancePresetPrefValue(preset: PerformancePreset): String {
        return when (preset) {
            PerformancePreset.PowerSave -> "power_save"
            PerformancePreset.Balanced -> "balanced"
            PerformancePreset.Performance -> "performance"
        }
    }

    private fun parsePreferredConnection(value: String?): PreferredConnection {
        return when (value) {
            "usb" -> PreferredConnection.Usb
            else -> PreferredConnection.Wifi
        }
    }

    private fun preferredConnectionPrefValue(connection: PreferredConnection): String {
        return when (connection) {
            PreferredConnection.Wifi -> "wifi"
            PreferredConnection.Usb -> "usb"
        }
    }
}
