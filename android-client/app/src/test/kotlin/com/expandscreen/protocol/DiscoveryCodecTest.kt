package com.expandscreen.protocol

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class DiscoveryCodecTest {
    @Test
    fun encode_request_uses_pascal_case_field_names() {
        val request =
            DiscoveryRequestMessage(
                requestId = "abc123",
                clientDeviceId = "android-1",
                clientDeviceName = "Pixel",
            )

        val bytes = DiscoveryCodec.encodeJsonPayload(request, DiscoveryRequestMessage.serializer())
        val json = bytes.decodeToString()

        assertTrue(json.contains("\"MessageType\":\"DiscoveryRequest\""))
        assertTrue(json.contains("\"RequestId\":\"abc123\""))
        assertTrue(json.contains("\"DiscoveryProtocolVersion\":1"))
        assertTrue(json.contains("\"ClientDeviceId\":\"android-1\""))
        assertTrue(json.contains("\"ClientDeviceName\":\"Pixel\""))
    }

    @Test
    fun decode_response_from_windows() {
        val json =
            """
            {
              "MessageType": "DiscoveryResponse",
              "RequestId": "abc123",
              "DiscoveryProtocolVersion": 1,
              "ServerId": "PC-1",
              "ServerName": "My-PC",
              "TcpPort": 15555,
              "WebSocketSupported": false,
              "ServerVersion": "1.0.0"
            }
            """.trimIndent()

        val response =
            DiscoveryCodec.decodeJsonPayload(
                json.encodeToByteArray(),
                DiscoveryResponseMessage.serializer(),
            )

        assertEquals("DiscoveryResponse", response.messageType)
        assertEquals("abc123", response.requestId)
        assertEquals(1, response.discoveryProtocolVersion)
        assertEquals("PC-1", response.serverId)
        assertEquals("My-PC", response.serverName)
        assertEquals(15555, response.tcpPort)
        assertEquals(false, response.webSocketSupported)
        assertEquals("1.0.0", response.serverVersion)
    }
}

