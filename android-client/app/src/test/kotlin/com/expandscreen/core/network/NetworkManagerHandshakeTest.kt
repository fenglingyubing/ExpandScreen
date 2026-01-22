package com.expandscreen.core.network

import com.expandscreen.protocol.HandshakeAckMessage
import com.expandscreen.protocol.HandshakeMessage
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

class NetworkManagerHandshakeTest {
    private class FakeUsbConnection : UsbConnection {
        override fun connectedAccessories() = emptyList<android.hardware.usb.UsbAccessory>()

        override suspend fun openSocket(listenPort: Int, acceptTimeoutMs: Int) =
            throw UnsupportedOperationException("not used in this test")
    }

    @Test
    fun connectViaWiFi_sendsHandshake_and_receivesAck() = runBlocking {
        ServerSocket(0).use { server ->
            val stop = CompletableDeferred<Unit>()
            val serverJob: Job =
                launch(Dispatchers.IO) {
                    val socket = server.accept()
                    socket.use {
                        val input = it.getInputStream()
                        val output = it.getOutputStream()

                        val header = MessageCodec.readHeader(input)
                        assertEquals(MessageType.Handshake, header.type)
                        val payload = MessageCodec.readPayload(input, header.payloadLength)
                        val handshake = MessageCodec.decodeJsonPayload(payload, HandshakeMessage.serializer())
                        assertEquals("android-1", handshake.deviceId)

                        val ack = HandshakeAckMessage(sessionId = "session-123", accepted = true)
                        val ackBytes = MessageCodec.encodeJsonPayload(ack, HandshakeAckMessage.serializer())
                        val ackHeader =
                            MessageHeader(
                                type = MessageType.HandshakeAck,
                                timestampMs = MessageCodec.nowTimestampMs(),
                                payloadLength = ackBytes.size,
                                sequenceNumber = 1,
                            )
                        val messageBytes = MessageCodec.encodeMessage(ackHeader, ackBytes)
                        output.write(messageBytes)
                        output.flush()

                        stop.await()
                    }
                }

            val manager = NetworkManager(FakeUsbConnection())
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
            assertTrue(manager.isConnected())
            assertEquals("session-123", manager.getSessionId())

            stop.complete(Unit)
            serverJob.join()

            manager.disconnect()
            delay(50)
            manager.close()
        }
    }
}

