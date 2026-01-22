package com.expandscreen.core.network

import android.content.Context
import android.hardware.usb.UsbAccessory
import android.hardware.usb.UsbManager
import dagger.Binds
import dagger.Module
import dagger.hilt.android.qualifiers.ApplicationContext
import dagger.hilt.InstallIn
import dagger.hilt.components.SingletonComponent
import java.net.InetSocketAddress
import java.net.ServerSocket
import java.net.Socket
import javax.inject.Inject
import javax.inject.Singleton
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

interface UsbConnection {
    fun connectedAccessories(): List<UsbAccessory>

    suspend fun openSocket(
        listenPort: Int = 15555,
        acceptTimeoutMs: Int = 10_000,
    ): Socket
}

/**
 * USB connection helper (device side).
 *
 * In the current architecture, Windows performs `adb forward tcp:<local> tcp:<remote>`,
 * and the Android device listens on `<remote>` and accepts the forwarded TCP connection.
 */
@Singleton
class AndroidUsbConnection @Inject constructor(
    @ApplicationContext private val context: Context,
) : UsbConnection {
    private val usbManager: UsbManager? = context.getSystemService(UsbManager::class.java)

    override fun connectedAccessories(): List<UsbAccessory> {
        return usbManager?.accessoryList?.toList() ?: emptyList()
    }

    override suspend fun openSocket(listenPort: Int, acceptTimeoutMs: Int): Socket {
        return withContext(Dispatchers.IO) {
            ServerSocket().use { server ->
                server.reuseAddress = true
                server.soTimeout = acceptTimeoutMs
                server.bind(InetSocketAddress("0.0.0.0", listenPort))

                val socket = server.accept()
                socket.tcpNoDelay = true
                socket.keepAlive = true
                socket
            }
        }
    }
}

@Module
@InstallIn(SingletonComponent::class)
abstract class UsbConnectionModule {
    @Binds
    @Singleton
    abstract fun bindUsbConnection(impl: AndroidUsbConnection): UsbConnection
}
