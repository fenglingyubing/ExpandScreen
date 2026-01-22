package com.expandscreen.core.network

import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableSharedFlow
import timber.log.Timber
import javax.inject.Inject
import javax.inject.Singleton

/**
 * Network Manager
 *
 * Manages TCP/USB connections with Windows PC and handles
 * message receiving, parsing, and distribution.
 */
@Singleton
class NetworkManager @Inject constructor() {

    private val _messageFlow = MutableSharedFlow<NetworkMessage>()
    val messageFlow: Flow<NetworkMessage> = _messageFlow

    private var isConnected = false

    /**
     * Connect to Windows PC via USB
     */
    suspend fun connectViaUSB(): Result<Unit> {
        Timber.d("Connecting via USB...")
        // TODO: Implement USB connection
        // - Detect USB accessory mode
        // - Establish TCP connection
        // - Perform handshake
        isConnected = true
        return Result.success(Unit)
    }

    /**
     * Connect to Windows PC via WiFi
     */
    suspend fun connectViaWiFi(host: String, port: Int): Result<Unit> {
        Timber.d("Connecting via WiFi to $host:$port...")
        // TODO: Implement WiFi connection
        // - Create TCP socket
        // - Connect to server
        // - Perform handshake
        isConnected = true
        return Result.success(Unit)
    }

    /**
     * Disconnect from Windows PC
     */
    fun disconnect() {
        Timber.d("Disconnecting...")
        isConnected = false
        // TODO: Close connection gracefully
    }

    /**
     * Send message to Windows PC
     */
    suspend fun sendMessage(message: NetworkMessage): Result<Unit> {
        if (!isConnected) {
            return Result.failure(Exception("Not connected"))
        }
        // TODO: Serialize and send message
        return Result.success(Unit)
    }

    fun isConnected(): Boolean = isConnected
}

/**
 * Network Message Types
 */
sealed class NetworkMessage {
    data class VideoFrame(val data: ByteArray, val timestamp: Long) : NetworkMessage()
    data class AudioFrame(val data: ByteArray, val timestamp: Long) : NetworkMessage()
    data class TouchEvent(val x: Float, val y: Float, val action: Int) : NetworkMessage()
    data class Handshake(val deviceInfo: String) : NetworkMessage()
    data object Heartbeat : NetworkMessage()
}
