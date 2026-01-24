package com.expandscreen.protocol

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable

@Serializable
data class DiscoveryRequestMessage(
    @SerialName("MessageType")
    val messageType: String = "DiscoveryRequest",
    @SerialName("RequestId")
    val requestId: String,
    @SerialName("DiscoveryProtocolVersion")
    val discoveryProtocolVersion: Int = 1,
    @SerialName("ClientDeviceId")
    val clientDeviceId: String? = null,
    @SerialName("ClientDeviceName")
    val clientDeviceName: String? = null,
)

@Serializable
data class DiscoveryResponseMessage(
    @SerialName("MessageType")
    val messageType: String = "DiscoveryResponse",
    @SerialName("RequestId")
    val requestId: String,
    @SerialName("DiscoveryProtocolVersion")
    val discoveryProtocolVersion: Int = 1,
    @SerialName("ServerId")
    val serverId: String,
    @SerialName("ServerName")
    val serverName: String,
    @SerialName("TcpPort")
    val tcpPort: Int,
    @SerialName("WebSocketSupported")
    val webSocketSupported: Boolean = false,
    @SerialName("ServerVersion")
    val serverVersion: String = "1.0.0",
)

