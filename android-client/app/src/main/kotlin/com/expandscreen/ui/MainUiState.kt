package com.expandscreen.ui

import com.expandscreen.core.network.ConnectionState
import com.expandscreen.core.network.DiscoveredWindowsServer
import com.expandscreen.data.model.WindowsDeviceEntity
import com.expandscreen.data.repository.PreferredConnection

data class MainUiState(
    val connectionState: ConnectionState = ConnectionState.Disconnected,
    val devices: List<WindowsDeviceEntity> = emptyList(),
    val host: String = "192.168.1.100",
    val port: String = "15555",
    val androidDeviceId: String = "",
    val androidDeviceName: String = "",
    val isWifiDiscovering: Boolean = false,
    val discoveredWifiServers: List<DiscoveredWindowsServer> = emptyList(),
    val preferredConnection: PreferredConnection = PreferredConnection.Wifi,
    val lastError: String? = null,
)
