package com.expandscreen.ui

import android.os.Build
import android.provider.Settings
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.expandscreen.core.network.NetworkManager
import com.expandscreen.data.model.WindowsDeviceEntity
import com.expandscreen.data.repository.DeviceRepository
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
    private val deviceRepository: DeviceRepository,
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
        _uiState.update { it.copy(showSettings = true) }
    }

    fun closeSettings() {
        _uiState.update { it.copy(showSettings = false) }
    }

    fun requestQrScan() {
        _events.tryEmit(MainUiEvent.ShowSnackbar("扫码连接暂未实现（预留）"))
    }

    fun disconnect() {
        networkManager.disconnect()
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

        val handshake =
            HandshakeMessage(
                deviceId = _uiState.value.androidDeviceId,
                deviceName = _uiState.value.androidDeviceName,
                screenWidth = 0,
                screenHeight = 0,
            )

        viewModelScope.launch {
            _uiState.update { it.copy(lastError = null) }
            val result =
                networkManager.connectViaWiFi(
                    host = host,
                    port = port,
                    handshake = handshake,
                    autoReconnect = true,
                )
            result.onSuccess {
                deviceRepository.upsertConnectedDevice(
                    deviceName = "Windows @ $host",
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
        val handshake =
            HandshakeMessage(
                deviceId = _uiState.value.androidDeviceId,
                deviceName = _uiState.value.androidDeviceName,
                screenWidth = 0,
                screenHeight = 0,
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
}
