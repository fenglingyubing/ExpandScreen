package com.expandscreen.data.repository

import kotlinx.coroutines.flow.StateFlow

interface SettingsRepository {
    val settings: StateFlow<AppSettings>

    fun setVideoResolution(resolution: VideoResolution)
    fun setVideoFrameRate(frameRate: VideoFrameRate)
    fun setVideoQuality(quality: VideoQuality)

    fun setPerformancePreset(preset: PerformancePreset)
    fun setLowLatencyMode(enabled: Boolean)

    fun setKeepScreenOn(enabled: Boolean)
    fun setAllowRotation(enabled: Boolean)
    fun setFullScreen(enabled: Boolean)
    fun setThemeMode(mode: ThemeMode)
    fun setDynamicColor(enabled: Boolean)

    fun setPreferredConnection(connection: PreferredConnection)
    fun setAutoReconnect(enabled: Boolean)

    fun exportToJson(pretty: Boolean = true): String
    fun importFromJson(json: String): Result<Unit>

    companion object Keys {
        const val PREFS_NAME = "expandscreen_settings"

        const val VIDEO_RESOLUTION = "pref_video_resolution"
        const val VIDEO_FRAME_RATE = "pref_video_frame_rate"
        const val VIDEO_QUALITY = "pref_video_quality"

        const val PERF_PRESET = "pref_perf_preset"
        const val PERF_LOW_LATENCY = "pref_perf_low_latency"

        const val DISPLAY_KEEP_SCREEN_ON = "pref_display_keep_screen_on"
        const val DISPLAY_ALLOW_ROTATION = "pref_display_allow_rotation"
        const val DISPLAY_FULLSCREEN = "pref_display_fullscreen"
        const val DISPLAY_THEME_MODE = "pref_display_theme_mode"
        const val DISPLAY_DYNAMIC_COLOR = "pref_display_dynamic_color"

        const val NETWORK_PREFERRED_CONNECTION = "pref_network_preferred_connection"
        const val NETWORK_AUTO_RECONNECT = "pref_network_auto_reconnect"
    }
}
