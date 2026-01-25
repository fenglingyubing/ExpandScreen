package com.expandscreen.core.network

import com.expandscreen.protocol.HandshakeAckMessage
import com.expandscreen.protocol.HandshakeMessage
import com.expandscreen.protocol.HeartbeatAckMessage
import com.expandscreen.protocol.HeartbeatMessage
import com.expandscreen.protocol.AudioFrameMessage
import com.expandscreen.protocol.MessageCodec
import com.expandscreen.protocol.MessageHeader
import com.expandscreen.protocol.MessageType
import com.expandscreen.protocol.TouchEventMessage
import com.expandscreen.protocol.VideoFrameMessage
import java.io.Closeable
import java.io.IOException
import java.io.InputStream
import java.io.OutputStream
import java.net.InetSocketAddress
import java.net.Socket
import java.util.concurrent.atomic.AtomicInteger
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeout
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.serialization.SerializationException
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
class NetworkManager @Inject constructor(
    private val usbConnection: UsbConnection,
    private val config: NetworkManagerConfig = NetworkManagerConfig(),
) : Closeable {

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val connectionMutex = Mutex()
    private val sendMutex = Mutex()

    private val _incomingMessages =
        MutableSharedFlow<IncomingMessage>(extraBufferCapacity = 64)
    val incomingMessages: Flow<IncomingMessage> = _incomingMessages.asSharedFlow()

    private val _connectionState = MutableStateFlow<ConnectionState>(ConnectionState.Disconnected)
    val connectionState: StateFlow<ConnectionState> = _connectionState.asStateFlow()

    private var socket: Socket? = null
    private var input: InputStream? = null
    private var output: OutputStream? = null
    private var receiveJob: Job? = null
    private var heartbeatJob: Job? = null

    private val sequenceNumber = AtomicInteger(0)
    private var sessionId: String? = null
    private var pendingHandshakeAck: CompletableDeferred<HandshakeAckMessage>? = null

    private var lastWifiHost: String? = null
    private var lastWifiPort: Int? = null
    private var lastHandshake: HandshakeMessage? = null
    private var autoReconnect: Boolean = false
    private var reconnectJob: Job? = null

    private var lastHeartbeatReceivedAtMs: Long = 0

    private val maxPayloadBytes = config.maxPayloadBytes
    private val connectTimeoutMs = config.connectTimeoutMs
    private val handshakeTimeoutMs = config.handshakeTimeoutMs
    private val heartbeatIntervalMs = config.heartbeatIntervalMs
    private val heartbeatTimeoutMs = config.heartbeatTimeoutMs

    /**
     * Connect to Windows PC via USB
     */
    suspend fun connectViaUSB(handshake: HandshakeMessage): Result<Unit> {
        Timber.d("Connecting via USB...")
        return connectionMutex.withLock {
            disconnectInternal(reason = "reconnect-via-usb")
            autoReconnect = false
            reconnectJob?.cancel()
            lastHandshake = handshake

            val usbSocket =
                runCatching { usbConnection.openSocket() }.getOrElse { return@withLock Result.failure(it) }
            connectWithSocket(usbSocket, handshake)
        }
    }

    /**
     * Connect to Windows PC via WiFi
     */
    suspend fun connectViaWiFi(
        host: String,
        port: Int,
        handshake: HandshakeMessage,
        autoReconnect: Boolean = true,
    ): Result<Unit> {
        Timber.d("Connecting via WiFi to $host:$port...")
        return connectionMutex.withLock {
            disconnectInternal(reason = "reconnect-via-wifi")
            this.autoReconnect = autoReconnect
            this.lastWifiHost = host
            this.lastWifiPort = port
            this.lastHandshake = handshake
            reconnectJob?.cancel()

            val newSocket = Socket()
            return@withLock runCatching {
                withContext(Dispatchers.IO) {
                    newSocket.tcpNoDelay = true
                    newSocket.keepAlive = true
                    newSocket.connect(InetSocketAddress(host, port), connectTimeoutMs)
                }
                connectWithSocket(newSocket, handshake)
            }.getOrElse { err ->
                safeClose(newSocket)
                _connectionState.value = ConnectionState.Disconnected
                Result.failure(err)
            }
        }
    }

    /**
     * Disconnect from Windows PC
     */
    fun disconnect() {
        Timber.d("Disconnecting...")
        scope.launch {
            connectionMutex.withLock {
                autoReconnect = false
                reconnectJob?.cancel()
                disconnectInternal(reason = "user")
            }
        }
    }

    /**
     * Send TouchEvent to Windows PC
     */
    suspend fun sendTouchEvent(message: TouchEventMessage): Result<Unit> {
        return sendJsonMessage(MessageType.TouchEvent, TouchEventMessage.serializer(), message)
    }

    /**
     * Send multiple TouchEvent messages in one flush (batch).
     *
     * This reduces syscall/flush overhead when MotionEvent contains multiple pointers.
     */
    suspend fun sendTouchEvents(messages: List<TouchEventMessage>): Result<Unit> {
        if (messages.isEmpty()) return Result.success(Unit)
        val output = this.output ?: return Result.failure(IllegalStateException("Not connected"))
        return sendMutex.withLock {
            runCatching {
                val headerScratch = ByteArray(MessageHeader.HEADER_SIZE_BYTES)
                withContext(Dispatchers.IO) {
                    for (message in messages) {
                        val payloadBytes =
                            MessageCodec.encodeJsonPayload(message, TouchEventMessage.serializer())
                        val header =
                            MessageHeader(
                                type = MessageType.TouchEvent,
                                timestampMs = MessageCodec.nowTimestampMs(),
                                payloadLength = payloadBytes.size,
                                sequenceNumber = sequenceNumber.incrementAndGet(),
                            )
                        MessageCodec.writeMessage(output, header, payloadBytes, headerScratch)
                    }
                    output.flush()
                }
            }
        }
    }

    fun isConnected(): Boolean = _connectionState.value is ConnectionState.Connected

    fun getSessionId(): String? = sessionId

    override fun close() {
        scope.cancel()
    }

    private suspend fun connectWithSocket(socket: Socket, handshake: HandshakeMessage): Result<Unit> {
        _connectionState.value = ConnectionState.Connecting

        val input = socket.getInputStream()
        val output = socket.getOutputStream()

        this.socket = socket
        this.input = input
        this.output = output
        this.sessionId = null
        this.lastHeartbeatReceivedAtMs = MessageCodec.nowTimestampMs()

        val handshakeAckDeferred = CompletableDeferred<HandshakeAckMessage>()
        pendingHandshakeAck = handshakeAckDeferred
        receiveJob = scope.launch { receiveLoop(input, output) }

        val ack =
            try {
                sendJsonMessage(MessageType.Handshake, HandshakeMessage.serializer(), handshake).getOrThrow()
                withTimeout(handshakeTimeoutMs.toLong()) { handshakeAckDeferred.await() }
            } catch (e: Exception) {
                disconnectInternal(reason = "handshake-failed")
                return Result.failure(e)
            }

        if (!ack.accepted) {
            disconnectInternal(reason = "handshake-rejected")
            return Result.failure(IllegalStateException("Handshake rejected: ${ack.errorMessage ?: "unknown"}"))
        }

        this.sessionId = ack.sessionId
        _connectionState.value = ConnectionState.Connected(ack.sessionId)
        _incomingMessages.emit(IncomingMessage.HandshakeAck(ack))

        startHeartbeatLoop()
        return Result.success(Unit)
    }

    private suspend fun receiveLoop(input: InputStream, output: OutputStream) {
        try {
            val headerScratch = ByteArray(MessageHeader.HEADER_SIZE_BYTES)
            while (scope.isActive) {
                val header = MessageCodec.readHeader(input, headerScratch)
                if (header.payloadLength < 0 || header.payloadLength > maxPayloadBytes) {
                    throw IOException("Invalid payload length: ${header.payloadLength}")
                }

                val payload = MessageCodec.readPayload(input, header.payloadLength)
                handleIncoming(header, payload, output)
            }
        } catch (e: Exception) {
            if (e is IOException) {
                Timber.w(e, "Receive loop stopped (IO)")
            } else {
                Timber.e(e, "Receive loop crashed")
            }

            connectionMutex.withLock {
                val shouldReconnect = autoReconnect && lastWifiHost != null && lastWifiPort != null && lastHandshake != null
                disconnectInternal(reason = "receive-loop-error")
                if (shouldReconnect) scheduleReconnect()
            }
        }
    }

    private suspend fun handleIncoming(header: MessageHeader, payload: ByteArray, output: OutputStream) {
        when (header.type) {
            MessageType.HandshakeAck -> {
                try {
                    val ack =
                        MessageCodec.decodeJsonPayload(
                            payload,
                            HandshakeAckMessage.serializer(),
                        )
                    pendingHandshakeAck?.complete(ack)
                } catch (e: SerializationException) {
                    throw IOException("Failed to decode HandshakeAck", e)
                }
            }

            MessageType.Heartbeat -> {
                lastHeartbeatReceivedAtMs = MessageCodec.nowTimestampMs()
                val heartbeat =
                    runCatching {
                        MessageCodec.decodeJsonPayload(payload, HeartbeatMessage.serializer())
                    }.getOrNull()
                        ?: return

                _incomingMessages.emit(IncomingMessage.Heartbeat(heartbeat))
                val ack =
                    HeartbeatAckMessage(
                        originalTimestamp = heartbeat.timestamp,
                        responseTimestamp = MessageCodec.nowTimestampMs(),
                    )
                sendJsonMessageToOutput(output, MessageType.HeartbeatAck, HeartbeatAckMessage.serializer(), ack)
            }

            MessageType.HeartbeatAck -> {
                lastHeartbeatReceivedAtMs = MessageCodec.nowTimestampMs()
                val ack =
                    runCatching {
                        MessageCodec.decodeJsonPayload(payload, HeartbeatAckMessage.serializer())
                    }.getOrNull()
                        ?: return
                _incomingMessages.emit(IncomingMessage.HeartbeatAck(ack))
            }

            MessageType.VideoFrame -> {
                val frame =
                    runCatching {
                        MessageCodec.decodeJsonPayload(payload, VideoFrameMessage.serializer())
                    }.getOrElse { e ->
                        throw IOException("Failed to decode VideoFrame", e)
                    }
                _incomingMessages.emit(IncomingMessage.VideoFrame(frame, header.timestampMs))
            }

            MessageType.AudioFrame -> {
                val frame =
                    runCatching {
                        MessageCodec.decodeJsonPayload(payload, AudioFrameMessage.serializer())
                    }.getOrElse { e ->
                        throw IOException("Failed to decode AudioFrame", e)
                    }
                _incomingMessages.emit(IncomingMessage.AudioFrame(frame, header.timestampMs))
            }

            MessageType.TouchEvent -> {
                val touch =
                    runCatching {
                        MessageCodec.decodeJsonPayload(payload, TouchEventMessage.serializer())
                    }.getOrNull()
                        ?: return
                _incomingMessages.emit(IncomingMessage.TouchEvent(touch))
            }

            MessageType.Handshake -> {
                Timber.w("Unexpected Handshake received on client side; ignoring")
            }
        }
    }

    private fun startHeartbeatLoop() {
        heartbeatJob?.cancel()
        heartbeatJob =
            scope.launch {
                while (isActive) {
                    delay(heartbeatIntervalMs)
                    val timeSinceLastHeartbeat = MessageCodec.nowTimestampMs() - lastHeartbeatReceivedAtMs
                    if (_connectionState.value is ConnectionState.Connected && timeSinceLastHeartbeat > heartbeatTimeoutMs) {
                        Timber.w("Heartbeat timeout (${timeSinceLastHeartbeat}ms), disconnecting")
                        connectionMutex.withLock {
                            val shouldReconnect =
                                autoReconnect &&
                                    lastWifiHost != null &&
                                    lastWifiPort != null &&
                                    lastHandshake != null
                            disconnectInternal(reason = "heartbeat-timeout")
                            if (shouldReconnect) scheduleReconnect()
                        }
                        return@launch
                    }

                    if (_connectionState.value is ConnectionState.Connected) {
                        val heartbeat = HeartbeatMessage(timestamp = MessageCodec.nowTimestampMs())
                        sendJsonMessage(MessageType.Heartbeat, HeartbeatMessage.serializer(), heartbeat)
                    }
                }
            }
    }

    private suspend fun <T> sendJsonMessage(
        type: MessageType,
        serializer: kotlinx.serialization.KSerializer<T>,
        payload: T,
    ): Result<Unit> {
        val output = this.output ?: return Result.failure(IllegalStateException("Not connected"))
        return sendJsonMessageToOutput(output, type, serializer, payload)
    }

    private suspend fun <T> sendJsonMessageToOutput(
        output: OutputStream,
        type: MessageType,
        serializer: kotlinx.serialization.KSerializer<T>,
        payload: T,
    ): Result<Unit> {
        return sendMutex.withLock {
            runCatching {
                val headerScratch = ByteArray(MessageHeader.HEADER_SIZE_BYTES)
                val payloadBytes = MessageCodec.encodeJsonPayload(payload, serializer)
                val header =
                    MessageHeader(
                        type = type,
                        timestampMs = MessageCodec.nowTimestampMs(),
                        payloadLength = payloadBytes.size,
                        sequenceNumber = sequenceNumber.incrementAndGet(),
                    )
                withContext(Dispatchers.IO) {
                    MessageCodec.writeMessage(output, header, payloadBytes, headerScratch)
                    output.flush()
                }
            }
        }
    }

    private fun scheduleReconnect() {
        if (reconnectJob?.isActive == true) return
        reconnectJob =
            scope.launch {
                val host = lastWifiHost ?: return@launch
                val port = lastWifiPort ?: return@launch
                val handshake = lastHandshake ?: return@launch

                var delayMs = 500L
                while (isActive && autoReconnect) {
                    Timber.i("Reconnect attempt in ${delayMs}ms...")
                    delay(delayMs)
                    val result = connectViaWiFi(host, port, handshake, autoReconnect = true)
                    if (result.isSuccess) {
                        Timber.i("Reconnected")
                        return@launch
                    }

                    delayMs = (delayMs * 2).coerceAtMost(10_000L)
                }
            }
    }

    private fun disconnectInternal(reason: String) {
        Timber.i("Disconnecting (reason=$reason)")
        pendingHandshakeAck?.cancel()
        pendingHandshakeAck = null

        heartbeatJob?.cancel()
        receiveJob?.cancel()

        safeClose(input)
        safeClose(output)
        safeClose(socket)
        input = null
        output = null
        socket = null
        sessionId = null
        _connectionState.value = ConnectionState.Disconnected
    }

    private fun safeClose(closeable: Any?) {
        when (closeable) {
            is InputStream -> runCatching { closeable.close() }
            is OutputStream -> runCatching { closeable.close() }
            is Socket -> runCatching { closeable.close() }
        }
    }
}

data class NetworkManagerConfig(
    val maxPayloadBytes: Int = 10 * 1024 * 1024,
    val connectTimeoutMs: Int = 5_000,
    val handshakeTimeoutMs: Int = 5_000,
    val heartbeatIntervalMs: Long = 5_000L,
    val heartbeatTimeoutMs: Long = 15_000L,
)

sealed interface ConnectionState {
    data object Disconnected : ConnectionState
    data object Connecting : ConnectionState
    data class Connected(val sessionId: String) : ConnectionState
}

sealed interface IncomingMessage {
    data class HandshakeAck(val ack: HandshakeAckMessage) : IncomingMessage
    data class Heartbeat(val message: HeartbeatMessage) : IncomingMessage
    data class HeartbeatAck(val message: HeartbeatAckMessage) : IncomingMessage
    data class VideoFrame(val message: VideoFrameMessage, val timestampMs: Long) : IncomingMessage
    data class AudioFrame(val message: AudioFrameMessage, val timestampMs: Long) : IncomingMessage
    data class TouchEvent(val message: TouchEventMessage) : IncomingMessage
}
