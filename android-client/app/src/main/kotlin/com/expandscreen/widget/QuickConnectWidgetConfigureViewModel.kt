package com.expandscreen.widget

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.expandscreen.data.model.WindowsDeviceEntity
import com.expandscreen.data.repository.DeviceRepository
import dagger.hilt.android.lifecycle.HiltViewModel
import javax.inject.Inject
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.stateIn

@HiltViewModel
class QuickConnectWidgetConfigureViewModel @Inject constructor(
    deviceRepository: DeviceRepository
) : ViewModel() {
    val favorites: StateFlow<List<WindowsDeviceEntity>> =
        deviceRepository.getFavoriteDevices().stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList())
}

