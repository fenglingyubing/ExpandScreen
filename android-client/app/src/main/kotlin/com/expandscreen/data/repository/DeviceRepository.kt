package com.expandscreen.data.repository

import com.expandscreen.data.database.DeviceDao
import com.expandscreen.data.model.WindowsDeviceEntity
import kotlinx.coroutines.flow.Flow
import javax.inject.Inject
import javax.inject.Singleton

/**
 * Device Repository
 *
 * Provides a clean API for device data access.
 */
@Singleton
class DeviceRepository @Inject constructor(
    private val deviceDao: DeviceDao
) {

    fun getAllDevices(): Flow<List<WindowsDeviceEntity>> {
        return deviceDao.getAllDevices()
    }

    fun getFavoriteDevices(): Flow<List<WindowsDeviceEntity>> {
        return deviceDao.getFavoriteDevices()
    }

    suspend fun getDeviceById(deviceId: Long): WindowsDeviceEntity? {
        return deviceDao.getDeviceById(deviceId)
    }

    suspend fun saveDevice(device: WindowsDeviceEntity): Long {
        return deviceDao.insertDevice(device)
    }

    suspend fun updateDevice(device: WindowsDeviceEntity) {
        deviceDao.updateDevice(device)
    }

    suspend fun deleteDevice(device: WindowsDeviceEntity) {
        deviceDao.deleteDevice(device)
    }

    suspend fun toggleFavorite(device: WindowsDeviceEntity) {
        val updated = device.copy(isFavorite = !device.isFavorite)
        deviceDao.updateDevice(updated)
    }

    suspend fun updateLastConnected(deviceId: Long) {
        deviceDao.updateLastConnected(deviceId, System.currentTimeMillis())
    }

    suspend fun upsertConnectedDevice(
        deviceName: String,
        ipAddress: String?,
        connectionType: String,
    ): Long {
        val now = System.currentTimeMillis()
        val existing =
            if (!ipAddress.isNullOrBlank()) {
                deviceDao.findByIpAddress(ipAddress, connectionType)
            } else {
                deviceDao.findByNameAndType(deviceName, connectionType)
            }

        val entity =
            if (existing != null) {
                existing.copy(
                    deviceName = deviceName,
                    ipAddress = ipAddress,
                    lastConnected = now,
                    connectionType = connectionType,
                )
            } else {
                WindowsDeviceEntity(
                    deviceName = deviceName,
                    ipAddress = ipAddress,
                    lastConnected = now,
                    isFavorite = false,
                    connectionType = connectionType,
                )
            }

        return deviceDao.insertDevice(entity)
    }
}
