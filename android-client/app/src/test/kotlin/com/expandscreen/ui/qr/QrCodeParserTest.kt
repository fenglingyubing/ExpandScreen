package com.expandscreen.ui.qr

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class QrCodeParserTest {
    @Test
    fun parse_hostPort() {
        val result = QrCodeParser.parse("192.168.1.10:15555")
        assertTrue(result.isSuccess)
        val info = result.getOrThrow()
        assertEquals("192.168.1.10", info.host)
        assertEquals(15555, info.port)
    }

    @Test
    fun parse_uriWithQuery() {
        val result = QrCodeParser.parse("expandscreen://connect?host=10.0.0.2&port=15555&token=abc&name=DESKTOP")
        assertTrue(result.isSuccess)
        val info = result.getOrThrow()
        assertEquals("10.0.0.2", info.host)
        assertEquals(15555, info.port)
        assertEquals("abc", info.token)
        assertEquals("DESKTOP", info.deviceName)
    }

    @Test
    fun parse_json() {
        val result = QrCodeParser.parse("""{"host":"10.0.0.3","port":15555,"token":"t"}""")
        assertTrue(result.isSuccess)
        val info = result.getOrThrow()
        assertEquals("10.0.0.3", info.host)
        assertEquals(15555, info.port)
        assertEquals("t", info.token)
    }
}

