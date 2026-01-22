package com.expandscreen.protocol

import java.io.EOFException
import java.io.InputStream
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.util.Base64
import kotlinx.serialization.KSerializer
import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable
import kotlinx.serialization.descriptors.PrimitiveKind
import kotlinx.serialization.descriptors.PrimitiveSerialDescriptor
import kotlinx.serialization.descriptors.SerialDescriptor
import kotlinx.serialization.encoding.Decoder
import kotlinx.serialization.encoding.Encoder
import kotlinx.serialization.json.Json

/**
 * Windows <-> Android protocol shared by ExpandScreen.
 *
 * Matches the C# protocol in `src/ExpandScreen.Protocol/Messages/MessageSerializer.cs`:
 * Magic(4) + Type(1) + Version(1) + Reserved(2) + Timestamp(8) + PayloadLength(4) + SequenceNumber(4) = 24 bytes.
 */

enum class MessageType(val value: Byte) {
    Handshake(0x01),
    HandshakeAck(0x02),
    VideoFrame(0x03),
    TouchEvent(0x04),
    Heartbeat(0x05),
    HeartbeatAck(0x06),
    ;

    companion object {
        fun fromByte(value: Byte): MessageType? = entries.firstOrNull { it.value == value }
    }
}

data class MessageHeader(
    val type: MessageType,
    val version: Byte = PROTOCOL_VERSION,
    val reserved: Short = 0,
    val timestampMs: Long,
    val payloadLength: Int,
    val sequenceNumber: Int,
    val magic: Int = MAGIC_NUMBER,
) {
    companion object {
        const val HEADER_SIZE_BYTES = 24
        const val MAGIC_NUMBER = 0x45585053 // "EXPS"
        const val PROTOCOL_VERSION: Byte = 0x01
    }
}

@Serializable
data class HandshakeMessage(
    @SerialName("DeviceId")
    val deviceId: String,
    @SerialName("DeviceName")
    val deviceName: String,
    @SerialName("ClientVersion")
    val clientVersion: String = "1.0.0",
    @SerialName("ScreenWidth")
    val screenWidth: Int,
    @SerialName("ScreenHeight")
    val screenHeight: Int,
)

@Serializable
data class HandshakeAckMessage(
    @SerialName("SessionId")
    val sessionId: String,
    @SerialName("ServerVersion")
    val serverVersion: String = "1.0.0",
    @SerialName("Accepted")
    val accepted: Boolean,
    @SerialName("ErrorMessage")
    val errorMessage: String? = null,
)

@Serializable
data class HeartbeatMessage(
    @SerialName("Timestamp")
    val timestamp: Long,
)

@Serializable
data class HeartbeatAckMessage(
    @SerialName("OriginalTimestamp")
    val originalTimestamp: Long,
    @SerialName("ResponseTimestamp")
    val responseTimestamp: Long,
)

object Base64ByteArraySerializer : KSerializer<ByteArray> {
    override val descriptor: SerialDescriptor =
        PrimitiveSerialDescriptor("Base64ByteArray", PrimitiveKind.STRING)

    override fun serialize(encoder: Encoder, value: ByteArray) {
        encoder.encodeString(Base64.getEncoder().encodeToString(value))
    }

    override fun deserialize(decoder: Decoder): ByteArray {
        return Base64.getDecoder().decode(decoder.decodeString())
    }
}

@Serializable
data class VideoFrameMessage(
    @SerialName("FrameNumber")
    val frameNumber: Int,
    @SerialName("Width")
    val width: Int,
    @SerialName("Height")
    val height: Int,
    @SerialName("IsKeyFrame")
    val isKeyFrame: Boolean,
    @SerialName("Data")
    @Serializable(with = Base64ByteArraySerializer::class)
    val data: ByteArray,
)

@Serializable
data class TouchEventMessage(
    @SerialName("Action")
    val action: Int,
    @SerialName("PointerId")
    val pointerId: Int,
    @SerialName("X")
    val x: Float,
    @SerialName("Y")
    val y: Float,
    @SerialName("Pressure")
    val pressure: Float,
)

object MessageCodec {
    private val json =
        Json {
            ignoreUnknownKeys = true
            encodeDefaults = true
        }

    fun encodeHeader(header: MessageHeader): ByteArray {
        val buffer =
            ByteBuffer.allocate(MessageHeader.HEADER_SIZE_BYTES).order(ByteOrder.BIG_ENDIAN)

        buffer.putInt(header.magic)
        buffer.put(header.type.value)
        buffer.put(header.version)
        buffer.putShort(header.reserved)
        buffer.putLong(header.timestampMs)
        buffer.putInt(header.payloadLength)
        buffer.putInt(header.sequenceNumber)

        return buffer.array()
    }

    fun decodeHeader(headerBytes: ByteArray): MessageHeader {
        if (headerBytes.size < MessageHeader.HEADER_SIZE_BYTES) {
            throw IllegalArgumentException(
                "Header too small: expected ${MessageHeader.HEADER_SIZE_BYTES}, got ${headerBytes.size}"
            )
        }

        val buffer = ByteBuffer.wrap(headerBytes).order(ByteOrder.BIG_ENDIAN)
        val magic = buffer.int
        if (magic != MessageHeader.MAGIC_NUMBER) {
            throw IllegalArgumentException(
                "Invalid magic: 0x${magic.toUInt().toString(16)}, expected 0x${MessageHeader.MAGIC_NUMBER.toUInt().toString(16)}"
            )
        }

        val typeByte = buffer.get()
        val type =
            MessageType.fromByte(typeByte)
                ?: throw IllegalArgumentException("Unknown message type: 0x${typeByte.toUByte().toString(16)}")

        val version = buffer.get()
        val reserved = buffer.short
        val timestampMs = buffer.long
        val payloadLength = buffer.int
        val sequenceNumber = buffer.int

        return MessageHeader(
            magic = magic,
            type = type,
            version = version,
            reserved = reserved,
            timestampMs = timestampMs,
            payloadLength = payloadLength,
            sequenceNumber = sequenceNumber,
        )
    }

    fun readHeader(input: InputStream): MessageHeader {
        val headerBytes = readFully(input, MessageHeader.HEADER_SIZE_BYTES)
        return decodeHeader(headerBytes)
    }

    fun readPayload(input: InputStream, payloadLength: Int): ByteArray {
        if (payloadLength <= 0) return ByteArray(0)
        return readFully(input, payloadLength)
    }

    fun encodeMessage(header: MessageHeader, payload: ByteArray): ByteArray {
        val headerBytes = encodeHeader(header)
        return ByteArray(headerBytes.size + payload.size).also { out ->
            System.arraycopy(headerBytes, 0, out, 0, headerBytes.size)
            System.arraycopy(payload, 0, out, headerBytes.size, payload.size)
        }
    }

    fun <T> encodeJsonPayload(payload: T, serializer: KSerializer<T>): ByteArray {
        return json.encodeToString(serializer, payload).encodeToByteArray()
    }

    fun <T> decodeJsonPayload(payload: ByteArray, serializer: KSerializer<T>): T {
        return json.decodeFromString(serializer, payload.decodeToString())
    }

    fun nowTimestampMs(): Long = System.currentTimeMillis()

    fun readFully(input: InputStream, size: Int): ByteArray {
        val buffer = ByteArray(size)
        var offset = 0
        while (offset < size) {
            val read = input.read(buffer, offset, size - offset)
            if (read == -1) throw EOFException("Unexpected EOF while reading $size bytes")
            offset += read
        }
        return buffer
    }
}
