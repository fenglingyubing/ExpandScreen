package com.expandscreen.ui

sealed interface MainUiEvent {
    data object NavigateToDisplay : MainUiEvent
    data object NavigateToSettings : MainUiEvent
    data class ShowSnackbar(val message: String) : MainUiEvent
}
