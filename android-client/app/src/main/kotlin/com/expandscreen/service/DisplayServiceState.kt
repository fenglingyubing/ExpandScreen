package com.expandscreen.service

import com.expandscreen.core.network.ConnectionState

data class DisplayServiceState(
    val connectionState: ConnectionState = ConnectionState.Disconnected,
    val fps: Int = 0,
    val latencyMs: Int = 0,
    val videoWidth: Int = 0,
    val videoHeight: Int = 0,
    val lastError: String? = null,
)
