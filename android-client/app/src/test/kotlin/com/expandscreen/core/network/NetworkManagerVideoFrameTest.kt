package com.expandscreen.core.network

import com.expandscreen.protocol.HandshakeAckMessage
import com.expandscreen.protocol.HandshakeMessage
import com.expandscreen.protocol.MessageCodec
import com.expandscreen.protocol.MessageHeader
import com.expandscreen.protocol.MessageType
import com.expandscreen.protocol.VideoFrameMessage
import java.net.ServerSocket
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.flow.filterIsInstance
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeout
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class NetworkManagerVideoFrameTest {
    private class FakeUsbConnection : UsbConnection {
        override fun connectedAccessories() = emptyList<android.hardware.usb.UsbAccessory>()

        override suspend fun openSocket(listenPort: Int, acceptTimeoutMs: Int) =
            throw UnsupportedOperationException("not used in this test")
    }

    @Test
    fun connectViaWiFi_emitsVideoFrameMessages() = runBlocking {
        ServerSocket(0).use { server ->
            val sendFrame = CompletableDeferred<Unit>()

            val serverJob =
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
                        output.write(MessageCodec.encodeMessage(ackHeader, ackBytes))
                        output.flush()

                        sendFrame.await()

                        val frame =
                            VideoFrameMessage(
                                frameNumber = 1,
                                width = 1920,
                                height = 1080,
                                isKeyFrame = true,
                                data = byteArrayOf(0x01, 0x02, 0x03),
                            )
                        val frameBytes = MessageCodec.encodeJsonPayload(frame, VideoFrameMessage.serializer())
                        val frameHeader =
                            MessageHeader(
                                type = MessageType.VideoFrame,
                                timestampMs = 1234L,
                                payloadLength = frameBytes.size,
                                sequenceNumber = 2,
                            )
                        output.write(MessageCodec.encodeMessage(frameHeader, frameBytes))
                        output.flush()
                    }
                }

            val manager = NetworkManager(FakeUsbConnection())
            val frameDeferred =
                async {
                    withTimeout(2_000) {
                        manager.incomingMessages.filterIsInstance<IncomingMessage.VideoFrame>().first()
                    }
                }

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

            sendFrame.complete(Unit)

            val incoming = frameDeferred.await()
            assertEquals(1920, incoming.message.width)
            assertEquals(1080, incoming.message.height)
            assertEquals(true, incoming.message.isKeyFrame)
            assertArrayEquals(byteArrayOf(0x01, 0x02, 0x03), incoming.message.data)
            assertEquals(1234L, incoming.timestampMs)

            serverJob.join()
            manager.disconnect()
            manager.close()
        }
    }
}
