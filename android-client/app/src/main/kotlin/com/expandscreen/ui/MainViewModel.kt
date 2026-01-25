package com.expandscreen.ui

import android.os.Build
import android.provider.Settings
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.expandscreen.core.security.TlsPinning
import com.expandscreen.core.security.TrustedHostStore
import com.expandscreen.core.network.NetworkManager
import com.expandscreen.core.network.WifiDiscoveryClient
import com.expandscreen.core.network.DiscoveredWindowsServer
import com.expandscreen.data.model.WindowsDeviceEntity
import com.expandscreen.data.repository.DeviceRepository
import com.expandscreen.data.repository.PreferredConnection
import com.expandscreen.data.repository.SettingsRepository
import com.expandscreen.protocol.HandshakeMessage
import com.expandscreen.ui.qr.QrCodeParser
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
    private val trustedHostStore: TrustedHostStore,
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

    fun onQrScanResult(raw: String) {
        val isBusy = _uiState.value.connectionState != com.expandscreen.core.network.ConnectionState.Disconnected
        if (isBusy) {
            _events.tryEmit(MainUiEvent.ShowSnackbar("当前正在连接中，请先断开再扫码"))
            return
        }

        val parsed = QrCodeParser.parse(raw)
        parsed.onFailure { err ->
            _events.tryEmit(MainUiEvent.ShowSnackbar("二维码解析失败：${err.message ?: err.javaClass.simpleName}"))
            return
        }

        val info = parsed.getOrThrow()
        _uiState.update { it.copy(host = info.host, port = info.port.toString(), lastError = null) }
        if (!info.token.isNullOrBlank()) {
            _events.tryEmit(MainUiEvent.ShowSnackbar("已读取认证信息（当前版本暂不使用）"))
        }

        connectWifiInternal(
            host = info.host,
            port = info.port,
            deviceNameForHistory = info.deviceName ?: "Windows @ ${info.host}",
        )
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
            val tlsEnabled = settingsRepository.settings.value.network.tlsEnabled
            val pinnedFingerprint = if (tlsEnabled) trustedHostStore.getPinnedFingerprint(host) else null
            val result =
                networkManager.connectViaWiFi(
                    host = host,
                    port = port,
                    handshake = handshake,
                    autoReconnect = autoReconnect,
                    useTls = tlsEnabled,
                    pinnedFingerprintSha256Hex = pinnedFingerprint,
                )
            result.onSuccess {
                val deviceId =
                    deviceRepository.upsertConnectedDevice(
                    deviceName = deviceNameForHistory,
                    ipAddress = host,
                    connectionType = "WiFi",
                )
                _events.emit(MainUiEvent.NavigateToDisplay(deviceId = deviceId, connectionType = "WiFi"))
            }.onFailure { err ->
                if (tlsEnabled && err is TlsPinning.PairingRequiredException) {
                    val reason =
                        when (err.reason) {
                            TlsPinning.PairingRequiredException.Reason.NotTrusted -> "首次连接需要配对：输入 Windows 端显示的 6 位配对码"
                            TlsPinning.PairingRequiredException.Reason.FingerprintMismatch ->
                                "证书已变更或可能存在中间人攻击：请核对配对码后重新配对"
                        }

                    _uiState.update {
                        it.copy(
                            tlsPairing =
                                TlsPairingState(
                                    host = host,
                                    port = port,
                                    expectedCode6 = err.peer.pairingCode6,
                                    fingerprintSha256Hex = err.peer.fingerprintSha256Hex,
                                    reason = reason,
                                    deviceNameForHistory = deviceNameForHistory,
                                ),
                            lastError = null,
                        )
                    }
                    return@onFailure
                }

                _uiState.update { it.copy(lastError = err.message ?: err.toString(), tlsPairing = null) }
                _events.emit(MainUiEvent.ShowSnackbar("连接失败：${err.message ?: err.javaClass.simpleName}"))
            }
        }
    }

    fun setTlsPairingCodeInput(raw: String) {
        val digits = raw.filter { it.isDigit() }.take(6)
        _uiState.update { state ->
            val pairing = state.tlsPairing ?: return@update state
            state.copy(tlsPairing = pairing.copy(inputCode = digits, error = null))
        }
    }

    fun cancelTlsPairing() {
        _uiState.update { it.copy(tlsPairing = null) }
    }

    fun confirmTlsPairing() {
        val pairing = _uiState.value.tlsPairing ?: return
        val input = pairing.inputCode.trim()
        if (input.length != 6) {
            _uiState.update { it.copy(tlsPairing = pairing.copy(error = "请输入 6 位配对码")) }
            return
        }
        if (input != pairing.expectedCode6) {
            _uiState.update { it.copy(tlsPairing = pairing.copy(error = "配对码不匹配，请重新核对")) }
            return
        }

        trustedHostStore.trustHost(pairing.host, pairing.fingerprintSha256Hex)
        _uiState.update { it.copy(tlsPairing = null) }
        _events.tryEmit(MainUiEvent.ShowSnackbar("已完成配对（TLS 信任已保存）"))

        connectWifiInternal(
            host = pairing.host,
            port = pairing.port,
            deviceNameForHistory = pairing.deviceNameForHistory,
        )
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
                val deviceId =
                    deviceRepository.upsertConnectedDevice(
                    deviceName = "USB",
                    ipAddress = null,
                    connectionType = "USB",
                )
                _events.emit(MainUiEvent.NavigateToDisplay(deviceId = deviceId, connectionType = "USB"))
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

    fun quickConnectDeviceId(deviceId: Long) {
        val isBusy = _uiState.value.connectionState != com.expandscreen.core.network.ConnectionState.Disconnected
        if (isBusy) {
            _events.tryEmit(MainUiEvent.ShowSnackbar("当前正在连接中，请先断开再发起快速连接"))
            return
        }

        viewModelScope.launch {
            val device = deviceRepository.getDeviceById(deviceId)
            if (device == null) {
                _events.emit(MainUiEvent.ShowSnackbar("未找到该设备（可能已删除）"))
                return@launch
            }
            connectHistory(device)
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
