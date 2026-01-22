package com.expandscreen.data.database

import androidx.room.*
import com.expandscreen.data.model.ConnectionLogEntity
import com.expandscreen.data.model.WindowsDeviceEntity
import kotlinx.coroutines.flow.Flow

/**
 * Device DAO
 *
 * Data Access Object for Windows devices.
 */
@Dao
interface DeviceDao {

    @Query("SELECT * FROM windows_devices ORDER BY lastConnected DESC")
    fun getAllDevices(): Flow<List<WindowsDeviceEntity>>

    @Query("SELECT * FROM windows_devices WHERE isFavorite = 1 ORDER BY lastConnected DESC")
    fun getFavoriteDevices(): Flow<List<WindowsDeviceEntity>>

    @Query("SELECT * FROM windows_devices WHERE id = :deviceId")
    suspend fun getDeviceById(deviceId: Long): WindowsDeviceEntity?

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertDevice(device: WindowsDeviceEntity): Long

    @Update
    suspend fun updateDevice(device: WindowsDeviceEntity)

    @Delete
    suspend fun deleteDevice(device: WindowsDeviceEntity)

    @Query("UPDATE windows_devices SET lastConnected = :timestamp WHERE id = :deviceId")
    suspend fun updateLastConnected(deviceId: Long, timestamp: Long)
}

/**
 * Connection Log DAO
 *
 * Data Access Object for connection logs.
 */
@Dao
interface ConnectionLogDao {

    @Query("SELECT * FROM connection_logs ORDER BY startTime DESC LIMIT :limit")
    fun getRecentLogs(limit: Int = 50): Flow<List<ConnectionLogEntity>>

    @Query("SELECT * FROM connection_logs WHERE deviceId = :deviceId ORDER BY startTime DESC")
    fun getLogsByDevice(deviceId: Long): Flow<List<ConnectionLogEntity>>

    @Insert
    suspend fun insertLog(log: ConnectionLogEntity): Long

    @Update
    suspend fun updateLog(log: ConnectionLogEntity)

    @Query("SELECT AVG(avgFps) FROM connection_logs WHERE deviceId = :deviceId AND avgFps IS NOT NULL")
    suspend fun getAverageFps(deviceId: Long): Float?

    @Query("SELECT AVG(avgLatency) FROM connection_logs WHERE deviceId = :deviceId AND avgLatency IS NOT NULL")
    suspend fun getAverageLatency(deviceId: Long): Float?
}
