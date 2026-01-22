package com.expandscreen.protocol

/**
 * Protocol Message Definitions
 *
 * Defines the communication protocol between Windows PC and Android device.
 * Messages are sent over TCP (USB or WiFi).
 */

/**
 * Message Header (24 bytes)
 */
data class MessageHeader(
    val magic: Int = MAGIC_NUMBER, // 4 bytes: 0x45585053 ("EXPS")
    val version: Byte = PROTOCOL_VERSION, // 1 byte
    val messageType: MessageType, // 1 byte
    val flags: Short = 0, // 2 bytes
    val sequenceNumber: Int, // 4 bytes
    val timestamp: Long, // 8 bytes
    val payloadLength: Int // 4 bytes
) {
    companion object {
        const val SIZE = 24
        const val MAGIC_NUMBER = 0x45585053 // "EXPS"
        const val PROTOCOL_VERSION: Byte = 1
    }
}

/**
 * Message Types
 */
enum class MessageType(val value: Byte) {
    HANDSHAKE(0x01),
    HANDSHAKE_ACK(0x02),
    HEARTBEAT(0x03),
    VIDEO_FRAME(0x10),
    AUDIO_FRAME(0x11),
    TOUCH_EVENT(0x20),
    KEYBOARD_EVENT(0x21),
    CONFIGURATION(0x30),
    ERROR(0xFF);

    companion object {
        fun fromByte(value: Byte): MessageType? {
            return entries.find { it.value == value }
        }
    }
}

/**
 * Handshake Message Payload
 */
data class HandshakePayload(
    val deviceName: String,
    val osVersion: String,
    val screenWidth: Int,
    val screenHeight: Int,
    val supportedCodecs: List<String>
)

/**
 * Video Frame Payload
 */
data class VideoFramePayload(
    val frameData: ByteArray,
    val isKeyFrame: Boolean,
    val width: Int,
    val height: Int
)

/**
 * Touch Event Payload
 */
data class TouchEventPayload(
    val action: TouchAction,
    val pointerId: Int,
    val x: Float,
    val y: Float,
    val pressure: Float,
    val size: Float
)

enum class TouchAction(val value: Byte) {
    DOWN(0x00),
    MOVE(0x01),
    UP(0x02),
    CANCEL(0x03)
}

/**
 * Message Serializer/Deserializer
 */
object MessageCodec {

    fun encodeHeader(header: MessageHeader): ByteArray {
        // TODO: Implement binary serialization
        return ByteArray(MessageHeader.SIZE)
    }

    fun decodeHeader(data: ByteArray): MessageHeader? {
        // TODO: Implement binary deserialization
        return null
    }

    fun encodeHandshake(payload: HandshakePayload): ByteArray {
        // TODO: Implement JSON or Protocol Buffers serialization
        return ByteArray(0)
    }

    fun decodeHandshake(data: ByteArray): HandshakePayload? {
        // TODO: Implement deserialization
        return null
    }
}
