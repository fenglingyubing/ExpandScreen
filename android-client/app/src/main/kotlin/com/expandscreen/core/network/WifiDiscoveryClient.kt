package com.expandscreen.core.network

import android.content.Context
import android.net.wifi.WifiManager
import com.expandscreen.protocol.DiscoveryCodec
import com.expandscreen.protocol.DiscoveryRequestMessage
import com.expandscreen.protocol.DiscoveryResponseMessage
import dagger.hilt.android.qualifiers.ApplicationContext
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.SocketTimeoutException
import java.util.UUID
import javax.inject.Inject
import javax.inject.Singleton
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import timber.log.Timber

data class DiscoveredWindowsServer(
    val serverId: String,
    val serverName: String,
    val host: String,
    val tcpPort: Int,
    val serverVersion: String,
    val webSocketSupported: Boolean,
)

@Singleton
class WifiDiscoveryClient @Inject constructor(
    @ApplicationContext private val context: Context,
) {
    companion object {
        const val DefaultDiscoveryPort: Int = 15556
    }

    private val wifiManager: WifiManager? = context.applicationContext.getSystemService(WifiManager::class.java)

    suspend fun discoverServers(
        clientDeviceId: String?,
        clientDeviceName: String?,
        udpPort: Int = DefaultDiscoveryPort,
        timeoutMs: Long = 1_200L,
    ): Result<List<DiscoveredWindowsServer>> {
        return withContext(Dispatchers.IO) {
            runCatching {
                val requestId = UUID.randomUUID().toString().replace("-", "")
                val request =
                    DiscoveryRequestMessage(
                        requestId = requestId,
                        clientDeviceId = clientDeviceId,
                        clientDeviceName = clientDeviceName,
                    )
                val requestBytes =
                    DiscoveryCodec.encodeJsonPayload(
                        request,
                        DiscoveryRequestMessage.serializer(),
                    )

                val multicastLock =
                    wifiManager
                        ?.createMulticastLock("ExpandScreenDiscovery")
                        ?.apply {
                            setReferenceCounted(false)
                            runCatching { acquire() }
                        }

                try {
                    DatagramSocket().use { socket ->
                        socket.broadcast = true
                        socket.soTimeout = 250

                        val targets = buildBroadcastTargets(udpPort)
                        for (target in targets) {
                            socket.send(
                                DatagramPacket(
                                    requestBytes,
                                    requestBytes.size,
                                    target,
                                    udpPort,
                                ),
                            )
                        }

                        val discoveredByKey = LinkedHashMap<String, DiscoveredWindowsServer>()
                        val deadlineNs = System.nanoTime() + timeoutMs * 1_000_000L
                        val buffer = ByteArray(4096)

                        while (System.nanoTime() < deadlineNs) {
                            try {
                                val packet = DatagramPacket(buffer, buffer.size)
                                socket.receive(packet)
                                val payload = packet.data.copyOf(packet.length)
                                val response =
                                    runCatching {
                                        DiscoveryCodec.decodeJsonPayload(
                                            payload,
                                            DiscoveryResponseMessage.serializer(),
                                        )
                                    }.getOrNull()
                                        ?: continue

                                if (response.messageType != "DiscoveryResponse") continue
                                if (response.requestId != requestId) continue

                                val host = packet.address.hostAddress ?: continue
                                if (response.tcpPort <= 0) continue

                                val key = "${response.serverId}@${host}:${response.tcpPort}"
                                discoveredByKey[key] =
                                    DiscoveredWindowsServer(
                                        serverId = response.serverId,
                                        serverName = response.serverName,
                                        host = host,
                                        tcpPort = response.tcpPort,
                                        serverVersion = response.serverVersion,
                                        webSocketSupported = response.webSocketSupported,
                                    )
                            } catch (_: SocketTimeoutException) {
                                // continue waiting until deadline
                            }
                        }

                        discoveredByKey.values.sortedWith(
                            compareBy<DiscoveredWindowsServer> { it.serverName.lowercase() }
                                .thenBy { it.host }
                                .thenBy { it.tcpPort },
                        )
                    }
                } finally {
                    runCatching { multicastLock?.release() }
                }
            }.onFailure { err ->
                Timber.w(err, "WiFi discovery failed")
            }
        }
    }

    private fun buildBroadcastTargets(udpPort: Int): List<InetAddress> {
        val targets = LinkedHashSet<InetAddress>()
        runCatching { targets.add(InetAddress.getByName("255.255.255.255")) }

        val dhcp = wifiManager?.dhcpInfo
        if (dhcp != null) {
            val ip = dhcp.ipAddress
            val mask = dhcp.netmask
            if (ip != 0 && mask != 0) {
                val broadcast = (ip and mask) or mask.inv()
                val quads =
                    byteArrayOf(
                        (broadcast and 0xFF).toByte(),
                        (broadcast shr 8 and 0xFF).toByte(),
                        (broadcast shr 16 and 0xFF).toByte(),
                        (broadcast shr 24 and 0xFF).toByte(),
                    )
                runCatching { targets.add(InetAddress.getByAddress(quads)) }
            }
        }

        if (targets.isEmpty()) {
            Timber.w("No broadcast targets resolved for udpPort=$udpPort")
        }

        return targets.toList()
    }
}

