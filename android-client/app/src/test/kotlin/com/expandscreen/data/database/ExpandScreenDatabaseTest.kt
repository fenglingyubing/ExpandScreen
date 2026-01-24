package com.expandscreen.data.database

import androidx.room.Room
import androidx.test.core.app.ApplicationProvider
import com.expandscreen.data.repository.ConnectionLogRepository
import com.expandscreen.data.repository.DeviceRepository
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.test.runTest
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [34])
class ExpandScreenDatabaseTest {

    private lateinit var database: ExpandScreenDatabase
    private lateinit var deviceRepository: DeviceRepository
    private lateinit var connectionLogRepository: ConnectionLogRepository

    @Before
    fun setUp() {
        val context = ApplicationProvider.getApplicationContext<android.content.Context>()
        database =
            Room.inMemoryDatabaseBuilder(context, ExpandScreenDatabase::class.java)
                .allowMainThreadQueries()
                .build()
        deviceRepository = DeviceRepository(database.deviceDao())
        connectionLogRepository = ConnectionLogRepository(database.connectionLogDao())
    }

    @After
    fun tearDown() {
        database.close()
    }

    @Test
    fun deviceRepository_upsertConnectedDevice_updatesExisting() = runTest {
        val firstId =
            deviceRepository.upsertConnectedDevice(
                deviceName = "Windows @ 192.168.1.2",
                ipAddress = "192.168.1.2",
                connectionType = "WiFi",
            )
        val secondId =
            deviceRepository.upsertConnectedDevice(
                deviceName = "Office-PC",
                ipAddress = "192.168.1.2",
                connectionType = "WiFi",
            )

        val devices = deviceRepository.getAllDevices().first()
        assertEquals(1, devices.size)
        assertEquals(firstId, secondId)
        assertEquals("Office-PC", devices.single().deviceName)
    }

    @Test
    fun connectionLogRepository_startAndEndLog_persistsStats() = runTest {
        val deviceId =
            deviceRepository.upsertConnectedDevice(
                deviceName = "Windows @ 10.0.2.2",
                ipAddress = "10.0.2.2",
                connectionType = "WiFi",
            )

        val startMs = 1_000L
        val endMs = 6_000L

        val logId =
            connectionLogRepository.startLog(
                deviceId = deviceId,
                connectionType = "WiFi",
                startTimeMs = startMs,
            )

        val started = connectionLogRepository.getRecentLogs(limit = 10).first().single()
        assertEquals(logId, started.id)
        assertEquals(deviceId, started.deviceId)
        assertEquals(startMs, started.startTime)
        assertNull(started.endTime)

        connectionLogRepository.endLog(
            logId = logId,
            deviceId = deviceId,
            connectionType = "WiFi",
            startTimeMs = startMs,
            endTimeMs = endMs,
            avgFps = 48.5f,
            avgLatencyMs = 37,
        )

        val ended = connectionLogRepository.getRecentLogs(limit = 10).first().single()
        assertNotNull(ended.endTime)
        assertEquals(5L, ended.duration)
        assertEquals(48.5f, ended.avgFps)
        assertEquals(37, ended.avgLatency)

        assertEquals(48.5f, connectionLogRepository.getAverageFps(deviceId))
        assertEquals(37f, connectionLogRepository.getAverageLatencyMs(deviceId))
    }
}

