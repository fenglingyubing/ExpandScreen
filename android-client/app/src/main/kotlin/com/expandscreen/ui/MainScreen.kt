package com.expandscreen.ui

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.asPaddingValues
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.ime
import androidx.compose.foundation.layout.navigationBars
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBars
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Devices
import androidx.compose.material.icons.filled.QrCodeScanner
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material.icons.filled.Star
import androidx.compose.material.icons.filled.StarBorder
import androidx.compose.material.icons.filled.Usb
import androidx.compose.material.icons.filled.Wifi
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.LocalContentColor
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.PathEffect
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import androidx.compose.foundation.text.KeyboardOptions
import com.expandscreen.core.network.ConnectionState
import com.expandscreen.core.network.DiscoveredWindowsServer
import com.expandscreen.data.model.WindowsDeviceEntity
import com.expandscreen.data.repository.PreferredConnection
import com.expandscreen.ui.theme.ExpandScreenTheme

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun MainScreen(
    state: MainUiState,
    snackbarHost: @Composable () -> Unit,
    onHostChange: (String) -> Unit,
    onPortChange: (String) -> Unit,
    onDeviceIdChange: (String) -> Unit,
    onDeviceNameChange: (String) -> Unit,
    onConnectWifi: () -> Unit,
    onDiscoverWifi: () -> Unit,
    onConnectDiscovered: (DiscoveredWindowsServer) -> Unit,
    onConnectUsb: () -> Unit,
    onDisconnect: () -> Unit,
    onConnectHistory: (WindowsDeviceEntity) -> Unit,
    onToggleFavorite: (WindowsDeviceEntity) -> Unit,
    onDeleteDevice: (WindowsDeviceEntity) -> Unit,
    onRequestQrScan: () -> Unit,
    onOpenSettings: () -> Unit,
    onCancelWaiting: () -> Unit,
) {
    val backgroundBrush =
        Brush.linearGradient(
            0.0f to Color(0xFF070A0F),
            0.65f to Color(0xFF070A0F),
            1.0f to Color(0xFF092018),
        )

    val baseInsets =
        PaddingValues(
            start = 18.dp,
            end = 18.dp,
            top = WindowInsets.statusBars.asPaddingValues().calculateTopPadding() + 14.dp,
            bottom = WindowInsets.navigationBars.asPaddingValues().calculateBottomPadding() + 16.dp,
        )

    Box(
        modifier =
            Modifier
                .fillMaxSize()
                .background(backgroundBrush)
                .drawBehind {
                    val stroke = 1.dp.toPx()
                    val dash = floatArrayOf(6.dp.toPx(), 10.dp.toPx())
                    val effect = PathEffect.dashPathEffect(dash, 0f)
                    val lineColor = Color(0xFF14323D).copy(alpha = 0.22f)
                    val gridStep = 44.dp.toPx()

                    var x = 0f
                    while (x <= size.width) {
                        drawLine(
                            color = lineColor,
                            start = androidx.compose.ui.geometry.Offset(x, 0f),
                            end = androidx.compose.ui.geometry.Offset(x, size.height),
                            strokeWidth = stroke,
                            pathEffect = effect,
                            cap = StrokeCap.Round,
                        )
                        x += gridStep
                    }
                    var y = 0f
                    while (y <= size.height) {
                        drawLine(
                            color = lineColor,
                            start = androidx.compose.ui.geometry.Offset(0f, y),
                            end = androidx.compose.ui.geometry.Offset(size.width, y),
                            strokeWidth = stroke,
                            pathEffect = effect,
                            cap = StrokeCap.Round,
                        )
                        y += gridStep
                    }
                },
    ) {
        LazyColumn(
            modifier = Modifier.fillMaxSize(),
            contentPadding =
                PaddingValues(
                    start = baseInsets.calculateLeftPadding(androidx.compose.ui.unit.LayoutDirection.Ltr),
                    end = baseInsets.calculateRightPadding(androidx.compose.ui.unit.LayoutDirection.Ltr),
                    top = baseInsets.calculateTopPadding(),
                    bottom =
                        baseInsets.calculateBottomPadding() +
                            WindowInsets.ime.asPaddingValues().calculateBottomPadding(),
                ),
            verticalArrangement = Arrangement.spacedBy(14.dp),
        ) {
            item {
                Header(
                    connectionState = state.connectionState,
                    onOpenSettings = onOpenSettings,
                )
            }

            item {
                QuickConnectCard(
                    host = state.host,
                    port = state.port,
                    androidDeviceId = state.androidDeviceId,
                    androidDeviceName = state.androidDeviceName,
                    preferredConnection = state.preferredConnection,
                    connectionState = state.connectionState,
                    onHostChange = onHostChange,
                    onPortChange = onPortChange,
                    onDeviceIdChange = onDeviceIdChange,
                    onDeviceNameChange = onDeviceNameChange,
                    onConnectWifi = onConnectWifi,
                    onConnectUsb = onConnectUsb,
                    onDisconnect = onDisconnect,
                    onRequestQrScan = onRequestQrScan,
                )
            }

            item {
                WifiDiscoveryCard(
                    discovered = state.discoveredWifiServers,
                    isDiscovering = state.isWifiDiscovering,
                    connectionState = state.connectionState,
                    onDiscover = onDiscoverWifi,
                    onConnect = onConnectDiscovered,
                )
            }

            item {
                DeviceHistoryCard(
                    devices = state.devices,
                    onConnect = onConnectHistory,
                    onToggleFavorite = onToggleFavorite,
                    onDelete = onDeleteDevice,
                )
            }

            item {
                ErrorCard(lastError = state.lastError)
            }
        }

        Box(
            modifier =
                Modifier
                    .align(Alignment.BottomCenter)
                    .fillMaxWidth()
                    .padding(horizontal = 18.dp, vertical = 14.dp),
        ) {
            snackbarHost()
        }

        AnimatedVisibility(visible = state.connectionState == ConnectionState.Connecting) {
            ConnectionWaitingOverlay(onCancel = onCancelWaiting)
        }

    }
}

@Composable
private fun Header(
    connectionState: ConnectionState,
    onOpenSettings: () -> Unit,
) {
    val status =
        when (connectionState) {
            ConnectionState.Connecting -> "CONNECTING"
            is ConnectionState.Connected -> "CONNECTED • ${connectionState.sessionId}"
            ConnectionState.Disconnected -> "DISCONNECTED"
        }

    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.Top,
    ) {
        Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
            Text(
                text = "ExpandScreen",
                style = MaterialTheme.typography.headlineLarge,
                color = Color(0xFFE9F2FF),
            )
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                Icon(
                    imageVector = Icons.Filled.Devices,
                    contentDescription = null,
                    tint = Color(0xFF7CFAC6),
                    modifier = Modifier.size(18.dp),
                )
                Text(
                    text = "LINK $status",
                    style = MaterialTheme.typography.labelLarge,
                    fontFamily = FontFamily.Monospace,
                    color = Color(0xFF7CFAC6),
                )
            }
        }

        IconButton(onClick = onOpenSettings) {
            Icon(
                imageVector = Icons.Filled.Settings,
                contentDescription = "Settings",
                tint = Color(0xFFE9F2FF),
            )
        }
    }
}

@Composable
private fun QuickConnectCard(
    host: String,
    port: String,
    androidDeviceId: String,
    androidDeviceName: String,
    preferredConnection: PreferredConnection,
    connectionState: ConnectionState,
    onHostChange: (String) -> Unit,
    onPortChange: (String) -> Unit,
    onDeviceIdChange: (String) -> Unit,
    onDeviceNameChange: (String) -> Unit,
    onConnectWifi: () -> Unit,
    onConnectUsb: () -> Unit,
    onDisconnect: () -> Unit,
    onRequestQrScan: () -> Unit,
) {
    val isBusy = connectionState == ConnectionState.Connecting
    val isConnected = connectionState is ConnectionState.Connected
    val preferUsb = preferredConnection == PreferredConnection.Usb

    val wifiColors =
        if (!preferUsb) {
            ButtonDefaults.buttonColors(
                containerColor = Color(0xFF133B3A),
                contentColor = Color(0xFFDCFFF1),
            )
        } else {
            ButtonDefaults.buttonColors(
                containerColor = Color(0xFF1C2B3A),
                contentColor = Color(0xFFE9F2FF),
            )
        }

    val usbColors =
        if (preferUsb) {
            ButtonDefaults.buttonColors(
                containerColor = Color(0xFF133B3A),
                contentColor = Color(0xFFDCFFF1),
            )
        } else {
            ButtonDefaults.buttonColors()
        }

    Card(
        colors =
            CardDefaults.cardColors(
                containerColor = Color(0xFF0D141B).copy(alpha = 0.96f),
                contentColor = Color(0xFFE9F2FF),
            ),
        shape = MaterialTheme.shapes.extraLarge,
        elevation = CardDefaults.cardElevation(defaultElevation = 0.dp),
    ) {
        Column(
            modifier = Modifier.padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Text(
                text = "Quick Connect",
                style = MaterialTheme.typography.titleMedium,
                color = Color(0xFFE9F2FF),
            )

            Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                OutlinedTextField(
                    value = host,
                    onValueChange = onHostChange,
                    label = { Text("Host") },
                    modifier = Modifier.weight(1f),
                    singleLine = true,
                    enabled = !isBusy && !isConnected,
                )
                OutlinedTextField(
                    value = port,
                    onValueChange = { onPortChange(it.filter(Char::isDigit).take(5)) },
                    label = { Text("Port") },
                    modifier = Modifier.width(120.dp),
                    singleLine = true,
                    enabled = !isBusy && !isConnected,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                )
            }

            Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                OutlinedTextField(
                    value = androidDeviceId,
                    onValueChange = onDeviceIdChange,
                    label = { Text("Device ID") },
                    modifier = Modifier.weight(1f),
                    singleLine = true,
                    enabled = !isBusy && !isConnected,
                )
                OutlinedTextField(
                    value = androidDeviceName,
                    onValueChange = onDeviceNameChange,
                    label = { Text("Device Name") },
                    modifier = Modifier.weight(1f),
                    singleLine = true,
                    enabled = !isBusy && !isConnected,
                )
            }

            Row(horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.CenterVertically) {
                Button(
                    onClick = onConnectWifi,
                    enabled = !isBusy && !isConnected,
                    colors = wifiColors,
                ) {
                    Icon(
                        imageVector = Icons.Filled.Wifi,
                        contentDescription = null,
                        modifier = Modifier.size(18.dp),
                    )
                    Spacer(modifier = Modifier.width(8.dp))
                    Text("WiFi")
                }

                Button(
                    onClick = onConnectUsb,
                    enabled = !isBusy && !isConnected,
                    colors = usbColors,
                ) {
                    Icon(
                        imageVector = Icons.Filled.Usb,
                        contentDescription = null,
                        modifier = Modifier.size(18.dp),
                    )
                    Spacer(modifier = Modifier.width(8.dp))
                    Text("USB")
                }

                TextButton(onClick = onRequestQrScan, enabled = !isBusy && !isConnected) {
                    Icon(
                        imageVector = Icons.Filled.QrCodeScanner,
                        contentDescription = null,
                        modifier = Modifier.size(18.dp),
                    )
                    Spacer(modifier = Modifier.width(8.dp))
                    Text("Scan (soon)")
                }

                Spacer(modifier = Modifier.weight(1f))

                Button(
                    onClick = onDisconnect,
                    enabled = !isBusy && isConnected,
                    colors =
                        ButtonDefaults.buttonColors(
                            containerColor = Color(0xFF2A1B1F),
                            contentColor = Color(0xFFFFE8EF),
                        ),
                ) {
                    Text("Disconnect")
                }
            }
        }
    }
}

@Composable
private fun WifiDiscoveryCard(
    discovered: List<DiscoveredWindowsServer>,
    isDiscovering: Boolean,
    connectionState: ConnectionState,
    onDiscover: () -> Unit,
    onConnect: (DiscoveredWindowsServer) -> Unit,
) {
    val isBusy = connectionState != ConnectionState.Disconnected
    val shimmer by
        rememberInfiniteTransition(label = "discovery-shimmer")
            .animateFloat(
                initialValue = 0f,
                targetValue = 1f,
                animationSpec = infiniteRepeatable(animation = tween(1_200), repeatMode = RepeatMode.Restart),
                label = "discovery-shimmer-f",
            )

    Card(
        colors =
            CardDefaults.cardColors(
                containerColor = Color(0xFF0B1118).copy(alpha = 0.92f),
                contentColor = Color(0xFFE9F2FF),
            ),
        shape = MaterialTheme.shapes.extraLarge,
        elevation = CardDefaults.cardElevation(defaultElevation = 0.dp),
    ) {
        Column(
            modifier = Modifier.padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    Text(
                        text = "WiFi Discovery",
                        style = MaterialTheme.typography.titleMedium,
                    )
                    Text(
                        text = "Broadcast + reply • UDP",
                        style = MaterialTheme.typography.labelMedium,
                        color = Color(0xFFB8C7D9),
                    )
                }

                TextButton(onClick = onDiscover, enabled = !isBusy && !isDiscovering) {
                    if (isDiscovering) {
                        CircularProgressIndicator(
                            modifier = Modifier.size(16.dp),
                            strokeWidth = 2.dp,
                            color = Color(0xFF7CFAC6),
                        )
                        Spacer(modifier = Modifier.width(10.dp))
                        Text("Scanning…")
                    } else {
                        Icon(
                            imageVector = Icons.Filled.Wifi,
                            contentDescription = null,
                            modifier = Modifier.size(18.dp),
                        )
                        Spacer(modifier = Modifier.width(8.dp))
                        Text("Scan LAN")
                    }
                }
            }

            HorizontalDivider(color = Color(0xFF14323D).copy(alpha = 0.4f))

            val hintColor =
                if (isDiscovering) {
                    Color(0xFF7CFAC6).copy(alpha = 0.65f + shimmer * 0.25f)
                } else {
                    Color(0xFFB8C7D9)
                }

            if (discovered.isEmpty()) {
                Text(
                    text =
                        if (isDiscovering) {
                            "Listening for Windows discovery responses…"
                        } else {
                            "Tap “Scan LAN” to find Windows hosts on the same WiFi."
                        },
                    style = MaterialTheme.typography.bodyMedium,
                    color = hintColor,
                )
                return@Column
            }

            Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                discovered.take(8).forEach { server ->
                    DiscoveredServerRow(
                        server = server,
                        enabled = !isBusy,
                        onConnect = { onConnect(server) },
                    )
                }
            }
        }
    }
}

@Composable
private fun DiscoveredServerRow(
    server: DiscoveredWindowsServer,
    enabled: Boolean,
    onConnect: () -> Unit,
) {
    val borderBrush =
        Brush.linearGradient(
            0.0f to Color(0xFF1D3E3E).copy(alpha = 0.55f),
            1.0f to Color(0xFF12252D).copy(alpha = 0.55f),
        )

    Row(
        modifier =
            Modifier
                .fillMaxWidth()
                .clip(MaterialTheme.shapes.large)
                .background(Color(0xFF0D141B).copy(alpha = 0.96f))
                .drawBehind {
                    val stroke = 1.dp.toPx()
                    val dash = floatArrayOf(6.dp.toPx(), 8.dp.toPx())
                    val effect = PathEffect.dashPathEffect(dash, 0f)
                    drawRect(
                        brush = borderBrush,
                        style = androidx.compose.ui.graphics.drawscope.Stroke(width = stroke, pathEffect = effect),
                    )
                }
                .clickable(
                    enabled = enabled,
                    interactionSource = remember { MutableInteractionSource() },
                    indication = null,
                    onClick = onConnect,
                )
                .padding(horizontal = 14.dp, vertical = 12.dp),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Text(
                text = server.serverName.ifBlank { "Windows Host" },
                style = MaterialTheme.typography.titleSmall,
                color = Color(0xFFE9F2FF),
            )
            Text(
                text = "${server.host}:${server.tcpPort} • v${server.serverVersion}",
                style = MaterialTheme.typography.labelMedium,
                fontFamily = FontFamily.Monospace,
                color = Color(0xFFB8C7D9),
            )
        }

        Button(
            onClick = onConnect,
            enabled = enabled,
            colors =
                ButtonDefaults.buttonColors(
                    containerColor = Color(0xFF133B3A),
                    contentColor = Color(0xFFDCFFF1),
                ),
            contentPadding = PaddingValues(horizontal = 14.dp, vertical = 10.dp),
        ) {
            Text("Connect")
        }
    }
}

@Composable
private fun DeviceHistoryCard(
    devices: List<WindowsDeviceEntity>,
    onConnect: (WindowsDeviceEntity) -> Unit,
    onToggleFavorite: (WindowsDeviceEntity) -> Unit,
    onDelete: (WindowsDeviceEntity) -> Unit,
) {
    Card(
        colors =
            CardDefaults.cardColors(
                containerColor = Color(0xFF0B1118).copy(alpha = 0.92f),
                contentColor = Color(0xFFE9F2FF),
            ),
        shape = MaterialTheme.shapes.extraLarge,
        elevation = CardDefaults.cardElevation(defaultElevation = 0.dp),
    ) {
        Column(
            modifier = Modifier.padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            Text(
                text = "History",
                style = MaterialTheme.typography.titleMedium,
            )
            HorizontalDivider(color = Color(0xFF14323D).copy(alpha = 0.4f))

            if (devices.isEmpty()) {
                Text(
                    text = "No devices yet. Connect once and it’ll show up here.",
                    style = MaterialTheme.typography.bodyMedium,
                    color = Color(0xFFB8C7D9),
                )
                return@Column
            }

            val ordered =
                devices.sortedWith(
                    compareByDescending<WindowsDeviceEntity> { it.isFavorite }.thenByDescending { it.lastConnected },
                )

            Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                ordered.take(6).forEach { device ->
                    DeviceRow(
                        device = device,
                        onClick = { onConnect(device) },
                        onToggleFavorite = { onToggleFavorite(device) },
                        onDelete = { onDelete(device) },
                    )
                }
            }

            if (devices.size > 6) {
                Text(
                    text = "Showing 6 of ${devices.size}",
                    style = MaterialTheme.typography.labelSmall,
                    fontFamily = FontFamily.Monospace,
                    color = Color(0xFF7CFAC6),
                )
            }
        }
    }
}

@Composable
private fun DeviceRow(
    device: WindowsDeviceEntity,
    onClick: () -> Unit,
    onToggleFavorite: () -> Unit,
    onDelete: () -> Unit,
) {
    val pillColor =
        when (device.connectionType) {
            "USB" -> Color(0xFF223044)
            else -> Color(0xFF1A3A2F)
        }

    val meta =
        if (device.connectionType == "WiFi") {
            device.ipAddress ?: "WiFi"
        } else {
            "USB"
        }

    Row(
        modifier =
            Modifier
                .clip(MaterialTheme.shapes.large)
                .background(Color(0xFF0E1822))
                .clickable(
                    interactionSource = remember { MutableInteractionSource() },
                    indication = null,
                    onClick = onClick,
                )
                .padding(horizontal = 12.dp, vertical = 10.dp),
        horizontalArrangement = Arrangement.spacedBy(12.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            Text(
                text = device.deviceName,
                style = MaterialTheme.typography.titleSmall,
                color = Color(0xFFE9F2FF),
            )
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                Text(
                    text = meta,
                    style = MaterialTheme.typography.labelSmall,
                    fontFamily = FontFamily.Monospace,
                    color = Color(0xFFB8C7D9),
                )
                Text(
                    text = formatEpochMs(device.lastConnected),
                    style = MaterialTheme.typography.labelSmall,
                    fontFamily = FontFamily.Monospace,
                    color = Color(0xFFB8C7D9),
                )
            }
        }

        Box(
            modifier =
                Modifier
                    .clip(MaterialTheme.shapes.small)
                    .background(pillColor)
                    .padding(horizontal = 10.dp, vertical = 6.dp),
        ) {
            Text(
                text = device.connectionType.uppercase(),
                style = MaterialTheme.typography.labelSmall,
                fontFamily = FontFamily.Monospace,
                color = Color(0xFFDCFFF1),
            )
        }

        IconButton(onClick = onToggleFavorite) {
            Icon(
                imageVector = if (device.isFavorite) Icons.Filled.Star else Icons.Filled.StarBorder,
                contentDescription = "Favorite",
                tint = if (device.isFavorite) Color(0xFFFFD36B) else LocalContentColor.current.copy(alpha = 0.9f),
            )
        }

        TextButton(onClick = onDelete) {
            Text(
                text = "Remove",
                fontFamily = FontFamily.Monospace,
                color = Color(0xFFFFA3B5),
            )
        }
    }
}

@Composable
private fun ErrorCard(lastError: String?) {
    val error = lastError ?: return
    Card(
        colors =
            CardDefaults.cardColors(
                containerColor = Color(0xFF221116).copy(alpha = 0.94f),
                contentColor = Color(0xFFFFE8EF),
            ),
        shape = MaterialTheme.shapes.extraLarge,
        elevation = CardDefaults.cardElevation(defaultElevation = 0.dp),
    ) {
        Column(
            modifier = Modifier.padding(14.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            Text(
                text = "Last error",
                style = MaterialTheme.typography.labelLarge,
                fontFamily = FontFamily.Monospace,
                color = Color(0xFFFFA3B5),
            )
            Text(
                text = error,
                style = MaterialTheme.typography.bodyMedium,
                fontFamily = FontFamily.Monospace,
                color = Color(0xFFFFE8EF),
            )
        }
    }
}

@Composable
private fun ConnectionWaitingOverlay(onCancel: () -> Unit) {
    val shimmer by
        rememberInfiniteTransition(label = "waiting-shimmer")
            .animateFloat(
                initialValue = 0.15f,
                targetValue = 0.65f,
                animationSpec =
                    infiniteRepeatable(
                        animation = tween(950),
                        repeatMode = RepeatMode.Reverse,
                    ),
                label = "waiting-alpha",
            )

    Box(
        modifier =
            Modifier
                .fillMaxSize()
                .background(Color(0xFF000000).copy(alpha = 0.62f))
                .padding(24.dp),
    ) {
        Card(
            modifier = Modifier.align(Alignment.Center),
            colors =
                CardDefaults.cardColors(
                    containerColor = Color(0xFF0B1118),
                    contentColor = Color(0xFFE9F2FF),
                ),
            shape = MaterialTheme.shapes.extraLarge,
            elevation = CardDefaults.cardElevation(defaultElevation = 0.dp),
        ) {
            Column(
                modifier = Modifier.padding(18.dp),
                verticalArrangement = Arrangement.spacedBy(10.dp),
            ) {
                Row(horizontalArrangement = Arrangement.spacedBy(12.dp), verticalAlignment = Alignment.CenterVertically) {
                    CircularProgressIndicator(
                        modifier = Modifier.size(22.dp),
                        strokeWidth = 2.5.dp,
                        color = Color(0xFF7CFAC6),
                    )
                    Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                        Text(
                            text = "Connecting…",
                            style = MaterialTheme.typography.titleMedium,
                            color = Color(0xFFE9F2FF),
                        )
                        Text(
                            text = "Waiting for handshake • keep the Windows side ready",
                            style = MaterialTheme.typography.bodyMedium,
                            color = Color(0xFFB8C7D9).copy(alpha = shimmer),
                        )
                    }
                }

                Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                    Spacer(modifier = Modifier.weight(1f))
                    Button(
                        onClick = onCancel,
                        colors =
                            ButtonDefaults.buttonColors(
                                containerColor = Color(0xFF2A1B1F),
                                contentColor = Color(0xFFFFE8EF),
                            ),
                    ) {
                        Text("Cancel")
                    }
                }
            }
        }
    }
}

private fun formatEpochMs(epochMs: Long): String {
    return runCatching {
        val dt =
            java.time.Instant
                .ofEpochMilli(epochMs)
                .atZone(java.time.ZoneId.systemDefault())
        java.time.format.DateTimeFormatter.ofPattern("MM-dd HH:mm").format(dt)
    }.getOrElse { epochMs.toString() }
}

@Preview(showBackground = true)
@Composable
private fun MainScreenPreview() {
    ExpandScreenTheme {
            MainScreen(
                state =
                    MainUiState(
                        connectionState = ConnectionState.Disconnected,
                    devices =
                        listOf(
                            WindowsDeviceEntity(
                                id = 1,
                                deviceName = "DESKTOP-NEON",
                                ipAddress = "192.168.1.23",
                                lastConnected = System.currentTimeMillis(),
                                isFavorite = true,
                                connectionType = "WiFi",
                            ),
                            WindowsDeviceEntity(
                                id = 2,
                                deviceName = "WORKSTATION",
                                ipAddress = null,
                                lastConnected = System.currentTimeMillis() - 86_400_000,
                                isFavorite = false,
                                connectionType = "USB",
                            ),
                        ),
                    host = "192.168.1.100",
                    port = "15555",
                    androidDeviceId = "android-preview",
                    androidDeviceName = "Android Preview",
                    lastError = "Example error message",
                ),
                snackbarHost = {},
                onHostChange = {},
                onPortChange = {},
                onDeviceIdChange = {},
                onDeviceNameChange = {},
                onConnectWifi = {},
                onDiscoverWifi = {},
                onConnectDiscovered = {},
                onConnectUsb = {},
                onDisconnect = {},
                onConnectHistory = {},
                onToggleFavorite = {},
                onDeleteDevice = {},
            onRequestQrScan = {},
            onOpenSettings = {},
            onCancelWaiting = {},
        )
    }
}
