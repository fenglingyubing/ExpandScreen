package com.expandscreen.ui

sealed interface MainUiEvent {
    data object NavigateToDisplay : MainUiEvent
    data class ShowSnackbar(val message: String) : MainUiEvent
}

