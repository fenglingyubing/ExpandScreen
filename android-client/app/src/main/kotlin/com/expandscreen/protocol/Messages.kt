package com.expandscreen.protocol

import java.io.EOFException
import java.io.InputStream
import java.io.OutputStream
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

    fun encodeHeaderInto(buffer: ByteArray, header: MessageHeader, offset: Int = 0) {
        require(buffer.size - offset >= MessageHeader.HEADER_SIZE_BYTES) {
            "buffer too small: need ${MessageHeader.HEADER_SIZE_BYTES} bytes from offset=$offset"
        }

        var i = offset
        putIntBE(buffer, i, header.magic); i += 4
        buffer[i++] = header.type.value
        buffer[i++] = header.version
        putShortBE(buffer, i, header.reserved); i += 2
        putLongBE(buffer, i, header.timestampMs); i += 8
        putIntBE(buffer, i, header.payloadLength); i += 4
        putIntBE(buffer, i, header.sequenceNumber); i += 4
    }

    fun encodeHeader(header: MessageHeader): ByteArray {
        return ByteArray(MessageHeader.HEADER_SIZE_BYTES).also { out -> encodeHeaderInto(out, header) }
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

    fun readHeader(input: InputStream, scratch: ByteArray): MessageHeader {
        require(scratch.size >= MessageHeader.HEADER_SIZE_BYTES) {
            "scratch too small: need >= ${MessageHeader.HEADER_SIZE_BYTES}"
        }
        readFullyInto(input, scratch, MessageHeader.HEADER_SIZE_BYTES)
        return decodeHeader(scratch)
    }

    fun readPayload(input: InputStream, payloadLength: Int): ByteArray {
        if (payloadLength <= 0) return ByteArray(0)
        return readFully(input, payloadLength)
    }

    fun writeMessage(output: OutputStream, header: MessageHeader, payload: ByteArray, headerScratch: ByteArray? = null) {
        val headerBytes = headerScratch ?: ByteArray(MessageHeader.HEADER_SIZE_BYTES)
        encodeHeaderInto(headerBytes, header)
        output.write(headerBytes, 0, MessageHeader.HEADER_SIZE_BYTES)
        if (payload.isNotEmpty()) {
            output.write(payload)
        }
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

    fun readFullyInto(input: InputStream, buffer: ByteArray, size: Int, offset: Int = 0) {
        require(size >= 0) { "size must be >= 0" }
        require(offset >= 0) { "offset must be >= 0" }
        require(buffer.size - offset >= size) { "buffer too small for size=$size at offset=$offset" }

        var localOffset = offset
        val end = offset + size
        while (localOffset < end) {
            val read = input.read(buffer, localOffset, end - localOffset)
            if (read == -1) throw EOFException("Unexpected EOF while reading $size bytes")
            localOffset += read
        }
    }

    private fun putShortBE(buffer: ByteArray, offset: Int, value: Short) {
        buffer[offset] = ((value.toInt() ushr 8) and 0xFF).toByte()
        buffer[offset + 1] = (value.toInt() and 0xFF).toByte()
    }

    private fun putIntBE(buffer: ByteArray, offset: Int, value: Int) {
        buffer[offset] = ((value ushr 24) and 0xFF).toByte()
        buffer[offset + 1] = ((value ushr 16) and 0xFF).toByte()
        buffer[offset + 2] = ((value ushr 8) and 0xFF).toByte()
        buffer[offset + 3] = (value and 0xFF).toByte()
    }

    private fun putLongBE(buffer: ByteArray, offset: Int, value: Long) {
        buffer[offset] = ((value ushr 56) and 0xFF).toByte()
        buffer[offset + 1] = ((value ushr 48) and 0xFF).toByte()
        buffer[offset + 2] = ((value ushr 40) and 0xFF).toByte()
        buffer[offset + 3] = ((value ushr 32) and 0xFF).toByte()
        buffer[offset + 4] = ((value ushr 24) and 0xFF).toByte()
        buffer[offset + 5] = ((value ushr 16) and 0xFF).toByte()
        buffer[offset + 6] = ((value ushr 8) and 0xFF).toByte()
        buffer[offset + 7] = (value and 0xFF).toByte()
    }
}
