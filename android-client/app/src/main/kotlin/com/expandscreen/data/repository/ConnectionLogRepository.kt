package com.expandscreen.data.repository

import com.expandscreen.data.database.ConnectionLogDao
import com.expandscreen.data.model.ConnectionLogEntity
import javax.inject.Inject
import javax.inject.Singleton
import kotlinx.coroutines.flow.Flow

/**
 * Connection Log Repository
 *
 * Provides a clean API for connection log access and statistics queries.
 */
@Singleton
class ConnectionLogRepository @Inject constructor(
    private val connectionLogDao: ConnectionLogDao,
) {

    fun getRecentLogs(limit: Int = 50): Flow<List<ConnectionLogEntity>> {
        return connectionLogDao.getRecentLogs(limit = limit)
    }

    fun getLogsByDevice(deviceId: Long): Flow<List<ConnectionLogEntity>> {
        return connectionLogDao.getLogsByDevice(deviceId = deviceId)
    }

    suspend fun startLog(
        deviceId: Long,
        connectionType: String,
        startTimeMs: Long = System.currentTimeMillis(),
    ): Long {
        return connectionLogDao.insertLog(
            ConnectionLogEntity(
                deviceId = deviceId,
                startTime = startTimeMs,
                endTime = null,
                duration = null,
                avgFps = null,
                avgLatency = null,
                connectionType = connectionType,
            ),
        )
    }

    suspend fun endLog(
        logId: Long,
        deviceId: Long,
        connectionType: String,
        startTimeMs: Long,
        endTimeMs: Long = System.currentTimeMillis(),
        avgFps: Float? = null,
        avgLatencyMs: Int? = null,
    ) {
        val durationSeconds =
            ((endTimeMs - startTimeMs).coerceAtLeast(0L) / 1000L)
                .coerceAtLeast(0L)

        connectionLogDao.updateLog(
            ConnectionLogEntity(
                id = logId,
                deviceId = deviceId,
                startTime = startTimeMs,
                endTime = endTimeMs,
                duration = durationSeconds,
                avgFps = avgFps,
                avgLatency = avgLatencyMs,
                connectionType = connectionType,
            ),
        )
    }

    suspend fun getAverageFps(deviceId: Long): Float? {
        return connectionLogDao.getAverageFps(deviceId = deviceId)
    }

    suspend fun getAverageLatencyMs(deviceId: Long): Float? {
        return connectionLogDao.getAverageLatency(deviceId = deviceId)
    }
}

