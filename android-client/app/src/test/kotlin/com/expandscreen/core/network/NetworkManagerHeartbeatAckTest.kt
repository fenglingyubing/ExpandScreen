package com.expandscreen.core.network

import com.expandscreen.protocol.HandshakeAckMessage
import com.expandscreen.protocol.HandshakeMessage
import com.expandscreen.protocol.HeartbeatAckMessage
import com.expandscreen.protocol.HeartbeatMessage
import com.expandscreen.protocol.MessageCodec
import com.expandscreen.protocol.MessageHeader
import com.expandscreen.protocol.MessageType
import java.net.ServerSocket
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class NetworkManagerHeartbeatAckTest {
    private class FakeUsbConnection : UsbConnection {
        override fun connectedAccessories() = emptyList<android.hardware.usb.UsbAccessory>()

        override suspend fun openSocket(listenPort: Int, acceptTimeoutMs: Int) =
            throw UnsupportedOperationException("not used in this test")
    }

    @Test
    fun serverHeartbeat_triggersClientHeartbeatAck() = runBlocking {
        ServerSocket(0).use { server ->
            val allowHeartbeat = CompletableDeferred<Unit>()
            val stop = CompletableDeferred<Unit>()

            val serverJob: Job =
                launch(Dispatchers.IO) {
                    val socket = server.accept()
                    socket.soTimeout = 2_000
                    socket.use {
                        val input = it.getInputStream()
                        val output = it.getOutputStream()

                        val header = MessageCodec.readHeader(input)
                        assertEquals(MessageType.Handshake, header.type)
                        val payload = MessageCodec.readPayload(input, header.payloadLength)
                        MessageCodec.decodeJsonPayload(payload, HandshakeMessage.serializer())

                        val ack = HandshakeAckMessage(sessionId = "session-123", accepted = true)
                        val ackBytes = MessageCodec.encodeJsonPayload(ack, HandshakeAckMessage.serializer())
                        val ackHeader =
                            MessageHeader(
                                type = MessageType.HandshakeAck,
                                timestampMs = MessageCodec.nowTimestampMs(),
                                payloadLength = ackBytes.size,
                                sequenceNumber = 1,
                            )
                        output.write(MessageCodec.encodeMessage(ackHeader, ackBytes))
                        output.flush()

                        allowHeartbeat.await()

                        val heartbeat = HeartbeatMessage(timestamp = 777L)
                        val heartbeatBytes = MessageCodec.encodeJsonPayload(heartbeat, HeartbeatMessage.serializer())
                        val heartbeatHeader =
                            MessageHeader(
                                type = MessageType.Heartbeat,
                                timestampMs = 1000L,
                                payloadLength = heartbeatBytes.size,
                                sequenceNumber = 2,
                            )
                        output.write(MessageCodec.encodeMessage(heartbeatHeader, heartbeatBytes))
                        output.flush()

                        val ackFromClientHeader = MessageCodec.readHeader(input)
                        assertEquals(MessageType.HeartbeatAck, ackFromClientHeader.type)
                        val ackFromClientPayload = MessageCodec.readPayload(input, ackFromClientHeader.payloadLength)
                        val decoded =
                            MessageCodec.decodeJsonPayload(
                                ackFromClientPayload,
                                HeartbeatAckMessage.serializer(),
                            )
                        assertEquals(777L, decoded.originalTimestamp)

                        stop.await()
                    }
                }

            val manager =
                NetworkManager(
                    usbConnection = FakeUsbConnection(),
                    config = NetworkManagerConfig(heartbeatIntervalMs = 60_000L, heartbeatTimeoutMs = 120_000L),
                )
            val result =
                manager.connectViaWiFi(
                    host = "127.0.0.1",
                    port = server.localPort,
                    handshake =
                        HandshakeMessage(
                            deviceId = "android-1",
                            deviceName = "Pixel",
                            screenWidth = 1920,
                            screenHeight = 1080,
                        ),
                    autoReconnect = false,
                )

            assertTrue(result.isSuccess)
            allowHeartbeat.complete(Unit)

            delay(50)
            stop.complete(Unit)
            serverJob.join()

            manager.disconnect()
            manager.close()
        }
    }
}

