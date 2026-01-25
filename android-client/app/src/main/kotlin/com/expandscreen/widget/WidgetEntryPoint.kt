package com.expandscreen.widget

import com.expandscreen.data.repository.DeviceRepository
import dagger.hilt.EntryPoint
import dagger.hilt.InstallIn
import dagger.hilt.components.SingletonComponent

@EntryPoint
@InstallIn(SingletonComponent::class)
interface WidgetEntryPoint {
    fun deviceRepository(): DeviceRepository
}

