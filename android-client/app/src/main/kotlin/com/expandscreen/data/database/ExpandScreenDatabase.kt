package com.expandscreen.data.database

import androidx.room.Database
import androidx.room.RoomDatabase
import com.expandscreen.data.model.ConnectionLogEntity
import com.expandscreen.data.model.WindowsDeviceEntity

/**
 * ExpandScreen Database
 *
 * Room database for storing:
 * - Windows device history
 * - Connection logs
 */
@Database(
    entities = [
        WindowsDeviceEntity::class,
        ConnectionLogEntity::class
    ],
    version = 1,
    exportSchema = true
)
abstract class ExpandScreenDatabase : RoomDatabase() {
    abstract fun deviceDao(): DeviceDao
    abstract fun connectionLogDao(): ConnectionLogDao
}
