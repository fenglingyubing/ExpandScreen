package com.expandscreen.ui

import android.app.Activity
import android.app.ActivityOptions
import android.content.Intent
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.remember
import androidx.compose.ui.platform.LocalContext
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.expandscreen.ui.qr.QrScanActivity
import com.expandscreen.ui.settings.SettingsActivity
import com.expandscreen.widget.QuickConnectWidgetIntents
import kotlinx.coroutines.launch

@Composable
fun MainRoute(
    launchIntent: Intent? = null,
    onIntentConsumed: () -> Unit = {},
    viewModel: MainViewModel = hiltViewModel(),
) {
    val context = LocalContext.current
    val state by viewModel.uiState.collectAsStateWithLifecycle()
    val snackbars = remember { SnackbarHostState() }
    val scope = rememberCoroutineScope()

    LaunchedEffect(launchIntent) {
        val intent = launchIntent ?: return@LaunchedEffect
        val deviceId = intent.getLongExtra(QuickConnectWidgetIntents.EXTRA_DEVICE_ID, -1L)

        if (intent.action == QuickConnectWidgetIntents.ACTION_QUICK_CONNECT && deviceId > 0) {
            viewModel.quickConnectDeviceId(deviceId)
            onIntentConsumed()
        }
    }

    val qrLauncher =
        rememberLauncherForActivityResult(ActivityResultContracts.StartActivityForResult()) { result ->
            if (result.resultCode != Activity.RESULT_OK) return@rememberLauncherForActivityResult
            val raw = result.data?.getStringExtra(QrScanActivity.EXTRA_QR_RAW)?.trim().orEmpty()
            if (raw.isBlank()) {
                scope.launch { snackbars.showSnackbar("未获取到二维码内容") }
                return@rememberLauncherForActivityResult
            }
            viewModel.onQrScanResult(raw)
        }

    LaunchedEffect(Unit) {
        viewModel.events.collect { event ->
            when (event) {
                is MainUiEvent.NavigateToDisplay -> {
                    val intent =
                        Intent(context, DisplayActivity::class.java).apply {
                            putExtra(DisplayActivity.EXTRA_DEVICE_ID, event.deviceId)
                            putExtra(DisplayActivity.EXTRA_CONNECTION_TYPE, event.connectionType)
                        }

                    val activity = context as? Activity
                    if (activity != null) {
                        activity.startActivity(
                            intent,
                            ActivityOptions.makeCustomAnimation(
                                activity,
                                android.R.anim.fade_in,
                                android.R.anim.fade_out,
                            ).toBundle(),
                        )
                    } else {
                        context.startActivity(intent)
                    }
                }
                MainUiEvent.NavigateToSettings -> {
                    context.startActivity(Intent(context, SettingsActivity::class.java))
                }
                is MainUiEvent.ShowSnackbar -> {
                    snackbars.showSnackbar(event.message)
                }
            }
        }
    }

    MainScreen(
        state = state,
        snackbarHost = { SnackbarHost(hostState = snackbars) },
        onHostChange = viewModel::setHost,
        onPortChange = viewModel::setPort,
        onDeviceIdChange = viewModel::setAndroidDeviceId,
        onDeviceNameChange = viewModel::setAndroidDeviceName,
        onConnectWifi = viewModel::connectWifi,
        onDiscoverWifi = viewModel::discoverWifiServers,
        onConnectDiscovered = viewModel::connectDiscovered,
        onConnectUsb = viewModel::waitUsb,
        onDisconnect = viewModel::disconnect,
        onConnectHistory = viewModel::connectHistory,
        onToggleFavorite = viewModel::toggleFavorite,
        onDeleteDevice = viewModel::deleteDevice,
        onRequestQrScan = { qrLauncher.launch(Intent(context, QrScanActivity::class.java)) },
        onOpenSettings = viewModel::openSettings,
        onCancelWaiting = viewModel::disconnect,
    )
}
