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
    val tlsPairing: TlsPairingState? = null,
    val lastError: String? = null,
)

data class TlsPairingState(
    val host: String,
    val port: Int,
    val expectedCode6: String,
    val fingerprintSha256Hex: String,
    val reason: String,
    val deviceNameForHistory: String,
    val inputCode: String = "",
    val error: String? = null,
)
