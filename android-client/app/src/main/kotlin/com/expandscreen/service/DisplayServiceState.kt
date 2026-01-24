package com.expandscreen.service

import com.expandscreen.core.network.ConnectionState
import com.expandscreen.core.performance.PerformanceMode

data class DisplayServiceState(
    val connectionState: ConnectionState = ConnectionState.Disconnected,
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
    val lastError: String? = null,
)
