package com.expandscreen.data.repository

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable

@Serializable
data class AppSettings(
    val video: VideoSettings = VideoSettings(),
    val performance: PerformanceSettings = PerformanceSettings(),
    val display: DisplaySettings = DisplaySettings(),
    val network: NetworkSettings = NetworkSettings(),
)

@Serializable
data class VideoSettings(
    val resolution: VideoResolution = VideoResolution.Auto,
    val frameRate: VideoFrameRate = VideoFrameRate.Auto,
    val quality: VideoQuality = VideoQuality.Balanced,
)

@Serializable
enum class VideoResolution {
    @SerialName("auto")
    Auto,

    @SerialName("1280x720")
    R1280x720,

    @SerialName("1920x1080")
    R1920x1080,

    @SerialName("2560x1600")
    R2560x1600,
}

@Serializable
enum class VideoFrameRate {
    @SerialName("auto")
    Auto,

    @SerialName("30")
    Fps30,

    @SerialName("60")
    Fps60,

    @SerialName("120")
    Fps120,
}

@Serializable
enum class VideoQuality {
    @SerialName("low")
    Low,

    @SerialName("balanced")
    Balanced,

    @SerialName("high")
    High,
}

@Serializable
data class PerformanceSettings(
    val preset: PerformancePreset = PerformancePreset.Balanced,
    val lowLatencyMode: Boolean = true,
)

@Serializable
enum class PerformancePreset {
    @SerialName("power_save")
    PowerSave,

    @SerialName("balanced")
    Balanced,

    @SerialName("performance")
    Performance,
}

@Serializable
data class DisplaySettings(
    val keepScreenOn: Boolean = true,
    val allowRotation: Boolean = false,
    val fullScreen: Boolean = true,
)

@Serializable
data class NetworkSettings(
    val preferredConnection: PreferredConnection = PreferredConnection.Wifi,
    val autoReconnect: Boolean = true,
)

@Serializable
enum class PreferredConnection {
    @SerialName("wifi")
    Wifi,

    @SerialName("usb")
    Usb,
}

