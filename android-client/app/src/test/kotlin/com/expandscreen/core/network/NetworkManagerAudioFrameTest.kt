package com.expandscreen.core.network

import com.expandscreen.protocol.AudioFrameMessage
import com.expandscreen.protocol.HandshakeAckMessage
import com.expandscreen.protocol.HandshakeMessage
import com.expandscreen.protocol.MessageCodec
import com.expandscreen.protocol.MessageHeader
import com.expandscreen.protocol.MessageType
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

class NetworkManagerAudioFrameTest {
    private class FakeUsbConnection : UsbConnection {
        override fun connectedAccessories() = emptyList<android.hardware.usb.UsbAccessory>()

        override suspend fun openSocket(listenPort: Int, acceptTimeoutMs: Int) =
            throw UnsupportedOperationException("not used in this test")
    }

    @Test
    fun connectViaWiFi_emitsAudioFrameMessages() = runBlocking {
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
                            AudioFrameMessage(
                                frameNumber = 1,
                                mimeType = "audio/mp4a-latm",
                                sampleRate = 48_000,
                                channelCount = 2,
                                data = byteArrayOf(0x11, 0x22, 0x33),
                            )
                        val frameBytes = MessageCodec.encodeJsonPayload(frame, AudioFrameMessage.serializer())
                        val frameHeader =
                            MessageHeader(
                                type = MessageType.AudioFrame,
                                timestampMs = 2222L,
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
                        manager.incomingMessages.filterIsInstance<IncomingMessage.AudioFrame>().first()
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
            assertEquals(1, incoming.message.frameNumber)
            assertEquals("audio/mp4a-latm", incoming.message.mimeType)
            assertEquals(48_000, incoming.message.sampleRate)
            assertEquals(2, incoming.message.channelCount)
            assertArrayEquals(byteArrayOf(0x11, 0x22, 0x33), incoming.message.data)
            assertEquals(2222L, incoming.timestampMs)

            serverJob.join()
            manager.disconnect()
            manager.close()
        }
    }
}

