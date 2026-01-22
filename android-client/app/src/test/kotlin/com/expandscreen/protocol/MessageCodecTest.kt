package com.expandscreen.protocol

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Test

class MessageCodecTest {
    @Test
    fun encodeHeader_and_decodeHeader_roundTrip() {
        val header =
            MessageHeader(
                type = MessageType.VideoFrame,
                version = 0x01,
                reserved = 0,
                timestampMs = 1_700_000_000_000,
                payloadLength = 1234,
                sequenceNumber = 42,
            )

        val bytes = MessageCodec.encodeHeader(header)
        assertEquals(MessageHeader.HEADER_SIZE_BYTES, bytes.size)
        assertArrayEquals(byteArrayOf('E'.code.toByte(), 'X'.code.toByte(), 'P'.code.toByte(), 'S'.code.toByte()), bytes.copyOfRange(0, 4))

        val decoded = MessageCodec.decodeHeader(bytes)
        assertEquals(header.magic, decoded.magic)
        assertEquals(header.type, decoded.type)
        assertEquals(header.version, decoded.version)
        assertEquals(header.reserved, decoded.reserved)
        assertEquals(header.timestampMs, decoded.timestampMs)
        assertEquals(header.payloadLength, decoded.payloadLength)
        assertEquals(header.sequenceNumber, decoded.sequenceNumber)
    }
}

