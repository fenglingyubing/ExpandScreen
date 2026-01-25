package com.expandscreen.widget

import android.content.Context
import com.expandscreen.core.network.NetworkManager
import com.expandscreen.data.repository.DeviceRepository
import dagger.hilt.android.qualifiers.ApplicationContext
import java.util.concurrent.atomic.AtomicBoolean
import javax.inject.Inject
import javax.inject.Singleton
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.collect
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.launch

@Singleton
class WidgetSyncObserver @Inject constructor(
    @ApplicationContext private val context: Context,
    private val networkManager: NetworkManager,
    private val deviceRepository: DeviceRepository,
) {
    private val started = AtomicBoolean(false)
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)

    fun start() {
        if (!started.compareAndSet(false, true)) return

        scope.launch(Dispatchers.IO) {
            networkManager.connectionState.collect { state ->
                QuickConnectWidgetStatusStore.write(context, state)
                QuickConnectWidgetUpdater.updateAll(context)
            }
        }

        scope.launch(Dispatchers.IO) {
            deviceRepository
                .getFavoriteDevices()
                .map { devices -> devices.map { it.id } }
                .distinctUntilChanged()
                .collect {
                    QuickConnectWidgetUpdater.updateAll(context)
                }
        }
    }
}
