package com.expandscreen.ui

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.expandscreen.core.network.ConnectionState
import com.expandscreen.core.network.NetworkManager
import com.expandscreen.core.network.UsbConnection
import com.expandscreen.protocol.HandshakeMessage
import com.expandscreen.ui.theme.ExpandScreenTheme
import dagger.hilt.android.AndroidEntryPoint
import java.net.Socket
import javax.inject.Inject
import kotlinx.coroutines.launch

/**
 * Main Activity - Entry point for the application
 *
 * Displays device list, connection options, and settings access
 */
@AndroidEntryPoint
class MainActivity : ComponentActivity() {
    @Inject lateinit var networkManager: NetworkManager

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            ExpandScreenTheme {
                Surface(
                    modifier = Modifier.fillMaxSize(),
                    color = MaterialTheme.colorScheme.background
                ) {
                    MainScreen(networkManager = networkManager)
                }
            }
        }
    }
}

@Composable
fun MainScreen(networkManager: NetworkManager) {
    val connectionState by networkManager.connectionState.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()

    var host by remember { mutableStateOf("192.168.1.100") }
    var port by remember { mutableStateOf("15555") }
    var deviceId by remember { mutableStateOf("android-001") }
    var deviceName by remember { mutableStateOf("Android Device") }
    var lastError by remember { mutableStateOf<String?>(null) }

    val panelBrush =
        Brush.linearGradient(
            0.0f to Color(0xFF0B0F14),
            0.6f to Color(0xFF0B0F14),
            1.0f to Color(0xFF0E1A16),
        )

    Box(
        modifier =
            Modifier
                .fillMaxSize()
                .background(panelBrush)
                .padding(20.dp),
    ) {
        Column(
            modifier =
                Modifier
                    .clip(MaterialTheme.shapes.extraLarge)
                    .background(Color(0xFF111A22))
                    .padding(18.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Text(
                text = "ExpandScreen",
                style = MaterialTheme.typography.headlineMedium,
                color = Color(0xFFE9F2FF),
            )
            Text(
                text = when (connectionState) {
                    ConnectionState.Connecting -> "Connecting…"
                    is ConnectionState.Connected -> "Connected • session ${(connectionState as ConnectionState.Connected).sessionId}"
                    ConnectionState.Disconnected -> "Disconnected"
                },
                style = MaterialTheme.typography.labelLarge,
                fontFamily = FontFamily.Monospace,
                color = Color(0xFF7CFAC6),
            )

            Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                OutlinedTextField(
                    value = host,
                    onValueChange = { host = it },
                    label = { Text("Host") },
                    modifier = Modifier.weight(1f),
                    singleLine = true,
                )
                OutlinedTextField(
                    value = port,
                    onValueChange = { port = it.filter { ch -> ch.isDigit() }.take(5) },
                    label = { Text("Port") },
                    modifier = Modifier.width(120.dp),
                    singleLine = true,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                )
            }

            Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                OutlinedTextField(
                    value = deviceId,
                    onValueChange = { deviceId = it },
                    label = { Text("DeviceId") },
                    modifier = Modifier.weight(1f),
                    singleLine = true,
                )
                OutlinedTextField(
                    value = deviceName,
                    onValueChange = { deviceName = it },
                    label = { Text("DeviceName") },
                    modifier = Modifier.weight(1f),
                    singleLine = true,
                )
            }

            Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                Button(
                    onClick = {
                        lastError = null
                        scope.launch {
                            val p = port.toIntOrNull() ?: return@launch
                            val result =
                                networkManager.connectViaWiFi(
                                    host = host,
                                    port = p,
                                    handshake =
                                        HandshakeMessage(
                                            deviceId = deviceId,
                                            deviceName = deviceName,
                                            screenWidth = 0,
                                            screenHeight = 0,
                                        ),
                                    autoReconnect = true,
                                )
                            lastError = result.exceptionOrNull()?.message
                        }
                    },
                    colors =
                        ButtonDefaults.buttonColors(
                            containerColor = Color(0xFF1C3A2F),
                            contentColor = Color(0xFFDCFFF1),
                        ),
                    enabled = connectionState !is ConnectionState.Connected,
                ) {
                    Text("Connect WiFi")
                }

                Button(
                    onClick = {
                        lastError = null
                        scope.launch {
                            val result =
                                networkManager.connectViaUSB(
                                    handshake =
                                        HandshakeMessage(
                                            deviceId = deviceId,
                                            deviceName = deviceName,
                                            screenWidth = 0,
                                            screenHeight = 0,
                                        ),
                                )
                            lastError = result.exceptionOrNull()?.message
                        }
                    },
                    enabled = connectionState !is ConnectionState.Connected,
                ) {
                    Text("Wait USB")
                }

                Button(
                    onClick = { networkManager.disconnect() },
                    enabled = connectionState is ConnectionState.Connected,
                    colors =
                        ButtonDefaults.buttonColors(
                            containerColor = Color(0xFF2A1B1F),
                            contentColor = Color(0xFFFFE8EF),
                        ),
                ) {
                    Text("Disconnect")
                }
            }

            if (lastError != null) {
                Spacer(modifier = Modifier.height(4.dp))
                Text(
                    text = "Error: $lastError",
                    color = Color(0xFFFFA3B5),
                    style = MaterialTheme.typography.bodyMedium,
                    fontFamily = FontFamily.Monospace,
                )
            }
        }
    }
}

@Preview(showBackground = true)
@Composable
fun MainScreenPreview() {
    ExpandScreenTheme {
        MainScreen(networkManager = NetworkManager(usbConnection = object : UsbConnection {
            override fun connectedAccessories() = emptyList<android.hardware.usb.UsbAccessory>()

            override suspend fun openSocket(listenPort: Int, acceptTimeoutMs: Int): Socket {
                throw UnsupportedOperationException("preview")
            }
        }))
    }
}
