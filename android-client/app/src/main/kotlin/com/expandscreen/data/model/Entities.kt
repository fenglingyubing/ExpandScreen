package com.expandscreen.data.model

import androidx.room.Entity
import androidx.room.PrimaryKey

/**
 * Windows Device Entity
 *
 * Stores information about Windows PCs that have been connected.
 */
@Entity(tableName = "windows_devices")
data class WindowsDeviceEntity(
    @PrimaryKey(autoGenerate = true)
    val id: Long = 0,
    val deviceName: String,
    val ipAddress: String?,
    val lastConnected: Long,
    val isFavorite: Boolean = false,
    val connectionType: String // "USB" or "WiFi"
)

/**
 * Connection Log Entity
 *
 * Stores connection history and statistics.
 */
@Entity(tableName = "connection_logs")
data class ConnectionLogEntity(
    @PrimaryKey(autoGenerate = true)
    val id: Long = 0,
    val deviceId: Long,
    val startTime: Long,
    val endTime: Long?,
    val duration: Long?, // in seconds
    val avgFps: Float?,
    val avgLatency: Int?, // in milliseconds
    val connectionType: String
)
