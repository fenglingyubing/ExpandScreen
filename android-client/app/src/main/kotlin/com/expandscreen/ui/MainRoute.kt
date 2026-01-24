package com.expandscreen.ui

import android.content.Intent
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.runtime.remember
import androidx.compose.ui.platform.LocalContext
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle

@Composable
fun MainRoute(viewModel: MainViewModel = hiltViewModel()) {
    val context = LocalContext.current
    val state by viewModel.uiState.collectAsStateWithLifecycle()
    val snackbars = remember { SnackbarHostState() }

    LaunchedEffect(Unit) {
        viewModel.events.collect { event ->
            when (event) {
                MainUiEvent.NavigateToDisplay -> {
                    context.startActivity(Intent(context, DisplayActivity::class.java))
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
        onRequestQrScan = viewModel::requestQrScan,
        onOpenSettings = viewModel::openSettings,
        onCloseSettings = viewModel::closeSettings,
        onCancelWaiting = viewModel::disconnect,
    )
}
