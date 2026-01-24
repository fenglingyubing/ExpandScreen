package com.expandscreen.core.performance

enum class PerformanceMode(
    val label: String,
    val targetRenderFps: Int,
    val decoderOperatingRateFps: Float?,
    val lowLatency: Boolean,
    val touchMinMoveIntervalMs: Long,
) {
    Performance(
        label = "Performance",
        targetRenderFps = 120,
        decoderOperatingRateFps = 120f,
        lowLatency = true,
        touchMinMoveIntervalMs = 4L,
    ),
    Balanced(
        label = "Balanced",
        targetRenderFps = 60,
        decoderOperatingRateFps = 60f,
        lowLatency = true,
        touchMinMoveIntervalMs = 8L,
    ),
    PowerSave(
        label = "Power Save",
        targetRenderFps = 30,
        decoderOperatingRateFps = 30f,
        lowLatency = false,
        touchMinMoveIntervalMs = 16L,
    ),
}

