package com.expandscreen.ui

sealed interface MainUiEvent {
    data class NavigateToDisplay(
        val deviceId: Long,
        val connectionType: String,
    ) : MainUiEvent
    data object NavigateToSettings : MainUiEvent
    data class ShowSnackbar(val message: String) : MainUiEvent
}
