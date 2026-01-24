package com.expandscreen.ui

import android.os.Build
import android.provider.Settings
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.expandscreen.core.network.NetworkManager
import com.expandscreen.core.network.WifiDiscoveryClient
import com.expandscreen.core.network.DiscoveredWindowsServer
import com.expandscreen.data.model.WindowsDeviceEntity
import com.expandscreen.data.repository.DeviceRepository
import com.expandscreen.data.repository.PreferredConnection
import com.expandscreen.data.repository.SettingsRepository
import com.expandscreen.protocol.HandshakeMessage
import dagger.hilt.android.lifecycle.HiltViewModel
import dagger.hilt.android.qualifiers.ApplicationContext
import javax.inject.Inject
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

@HiltViewModel
class MainViewModel @Inject constructor(
    @ApplicationContext private val appContext: android.content.Context,
    private val networkManager: NetworkManager,
    private val wifiDiscoveryClient: WifiDiscoveryClient,
    private val deviceRepository: DeviceRepository,
    private val settingsRepository: SettingsRepository,
) : ViewModel() {
    private val _uiState = MutableStateFlow(MainUiState())
    val uiState = _uiState.asStateFlow()

    private val _events = MutableSharedFlow<MainUiEvent>(extraBufferCapacity = 16)
    val events = _events.asSharedFlow()

    init {
        val androidId =
            runCatching {
                Settings.Secure.getString(appContext.contentResolver, Settings.Secure.ANDROID_ID)
            }.getOrNull() ?: "android"
        val androidName = "${Build.MANUFACTURER} ${Build.MODEL}".trim()

        _uiState.update { it.copy(androidDeviceId = androidId, androidDeviceName = androidName) }

        viewModelScope.launch {
            networkManager.connectionState.collect { state ->
                _uiState.update { it.copy(connectionState = state) }
            }
        }
        viewModelScope.launch {
            deviceRepository.getAllDevices().collect { devices ->
                _uiState.update { it.copy(devices = devices) }
            }
        }
        viewModelScope.launch {
            settingsRepository.settings.collect { settings ->
                val preferred: PreferredConnection = settings.network.preferredConnection
                _uiState.update { it.copy(preferredConnection = preferred) }
            }
        }
    }

    fun setHost(host: String) {
        _uiState.update { it.copy(host = host) }
    }

    fun setPort(port: String) {
        _uiState.update { it.copy(port = port) }
    }

    fun setAndroidDeviceId(id: String) {
        _uiState.update { it.copy(androidDeviceId = id) }
    }

    fun setAndroidDeviceName(name: String) {
        _uiState.update { it.copy(androidDeviceName = name) }
    }

    fun openSettings() {
        _events.tryEmit(MainUiEvent.NavigateToSettings)
    }

    fun requestQrScan() {
        _events.tryEmit(MainUiEvent.ShowSnackbar("扫码连接暂未实现（预留）"))
    }

    fun disconnect() {
        networkManager.disconnect()
    }

    fun discoverWifiServers() {
        val isBusy = _uiState.value.connectionState != com.expandscreen.core.network.ConnectionState.Disconnected
        if (isBusy) return

        viewModelScope.launch {
            _uiState.update { it.copy(isWifiDiscovering = true, discoveredWifiServers = emptyList(), lastError = null) }

            val result =
                wifiDiscoveryClient.discoverServers(
                    clientDeviceId = _uiState.value.androidDeviceId.takeIf { it.isNotBlank() },
                    clientDeviceName = _uiState.value.androidDeviceName.takeIf { it.isNotBlank() },
                )

            result.onSuccess { servers ->
                _uiState.update { current ->
                    val nextHost =
                        if (current.host == "192.168.1.100" && servers.isNotEmpty()) servers.first().host else current.host
                    val nextPort =
                        if (current.port == "15555" && servers.isNotEmpty()) servers.first().tcpPort.toString() else current.port

                    current.copy(
                        isWifiDiscovering = false,
                        discoveredWifiServers = servers,
                        host = nextHost,
                        port = nextPort,
                    )
                }

                if (servers.isEmpty()) {
                    _events.emit(MainUiEvent.ShowSnackbar("未发现可用的 Windows 端（确认同一 WiFi 且 Windows 已开启 WiFi 服务）"))
                }
            }.onFailure { err ->
                _uiState.update { it.copy(isWifiDiscovering = false, lastError = err.message ?: err.toString()) }
                _events.emit(MainUiEvent.ShowSnackbar("设备发现失败：${err.message ?: err.javaClass.simpleName}"))
            }
        }
    }

    fun connectWifi() {
        val host = _uiState.value.host.trim()
        val port = _uiState.value.port.toIntOrNull()
        if (host.isEmpty()) {
            _events.tryEmit(MainUiEvent.ShowSnackbar("Host 不能为空"))
            return
        }
        if (port == null) {
            _events.tryEmit(MainUiEvent.ShowSnackbar("Port 无效"))
            return
        }

        connectWifiInternal(host = host, port = port, deviceNameForHistory = "Windows @ $host")
    }

    fun connectDiscovered(server: DiscoveredWindowsServer) {
        _uiState.update { it.copy(host = server.host, port = server.tcpPort.toString()) }
        connectWifiInternal(
            host = server.host,
            port = server.tcpPort,
            deviceNameForHistory = server.serverName.ifBlank { "Windows @ ${server.host}" },
        )
    }

    private fun connectWifiInternal(host: String, port: Int, deviceNameForHistory: String) {
        val (screenWidth, screenHeight) = getLandscapeScreenSizePx()
        val handshake =
            HandshakeMessage(
                deviceId = _uiState.value.androidDeviceId,
                deviceName = _uiState.value.androidDeviceName,
                screenWidth = screenWidth,
                screenHeight = screenHeight,
            )

        viewModelScope.launch {
            _uiState.update { it.copy(lastError = null) }
            val autoReconnect = settingsRepository.settings.value.network.autoReconnect
            val result =
                networkManager.connectViaWiFi(
                    host = host,
                    port = port,
                    handshake = handshake,
                    autoReconnect = autoReconnect,
                )
            result.onSuccess {
                deviceRepository.upsertConnectedDevice(
                    deviceName = deviceNameForHistory,
                    ipAddress = host,
                    connectionType = "WiFi",
                )
                _events.emit(MainUiEvent.NavigateToDisplay)
            }.onFailure { err ->
                _uiState.update { it.copy(lastError = err.message ?: err.toString()) }
                _events.emit(MainUiEvent.ShowSnackbar("连接失败：${err.message ?: err.javaClass.simpleName}"))
            }
        }
    }

    fun waitUsb() {
        val (screenWidth, screenHeight) = getLandscapeScreenSizePx()
        val handshake =
            HandshakeMessage(
                deviceId = _uiState.value.androidDeviceId,
                deviceName = _uiState.value.androidDeviceName,
                screenWidth = screenWidth,
                screenHeight = screenHeight,
            )

        viewModelScope.launch {
            _uiState.update { it.copy(lastError = null) }
            val result = networkManager.connectViaUSB(handshake)
            result.onSuccess {
                deviceRepository.upsertConnectedDevice(
                    deviceName = "USB",
                    ipAddress = null,
                    connectionType = "USB",
                )
                _events.emit(MainUiEvent.NavigateToDisplay)
            }.onFailure { err ->
                _uiState.update { it.copy(lastError = err.message ?: err.toString()) }
                _events.emit(MainUiEvent.ShowSnackbar("USB连接失败：${err.message ?: err.javaClass.simpleName}"))
            }
        }
    }

    fun connectHistory(device: WindowsDeviceEntity) {
        when (device.connectionType) {
            "USB" -> waitUsb()
            else -> {
                val ip = device.ipAddress
                if (ip.isNullOrBlank()) {
                    _events.tryEmit(MainUiEvent.ShowSnackbar("该历史设备缺少 IP"))
                    return
                }
                setHost(ip)
                connectWifi()
            }
        }
    }

    fun toggleFavorite(device: WindowsDeviceEntity) {
        viewModelScope.launch { deviceRepository.toggleFavorite(device) }
    }

    fun deleteDevice(device: WindowsDeviceEntity) {
        viewModelScope.launch { deviceRepository.deleteDevice(device) }
    }

    private fun getLandscapeScreenSizePx(): Pair<Int, Int> {
        val metrics = appContext.resources.displayMetrics
        val w = metrics.widthPixels.coerceAtLeast(1)
        val h = metrics.heightPixels.coerceAtLeast(1)
        return if (w >= h) {
            w to h
        } else {
            h to w
        }
    }
}
