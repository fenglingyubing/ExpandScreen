package com.expandscreen.widget

import android.content.Context
import com.expandscreen.core.network.ConnectionState

data class QuickConnectWidgetConnectionSnapshot(
    val state: QuickConnectWidgetConnectionState,
    val sessionId: String? = null,
    val updatedAtMs: Long = System.currentTimeMillis(),
)

enum class QuickConnectWidgetConnectionState {
    Disconnected,
    Connecting,
    Connected,
}

object QuickConnectWidgetStatusStore {
    private const val PREFS_NAME = "quick_connect_widget_status"
    private const val KEY_STATE = "state"
    private const val KEY_SESSION_ID = "session_id"
    private const val KEY_UPDATED_AT = "updated_at"

    fun write(context: Context, connectionState: ConnectionState) {
        val (state, sessionId) =
            when (connectionState) {
                ConnectionState.Disconnected -> QuickConnectWidgetConnectionState.Disconnected to null
                ConnectionState.Connecting -> QuickConnectWidgetConnectionState.Connecting to null
                is ConnectionState.Connected -> QuickConnectWidgetConnectionState.Connected to connectionState.sessionId
            }

        val prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
        prefs.edit()
            .putString(KEY_STATE, state.name)
            .putString(KEY_SESSION_ID, sessionId.orEmpty())
            .putLong(KEY_UPDATED_AT, System.currentTimeMillis())
            .apply()
    }

    fun read(context: Context): QuickConnectWidgetConnectionSnapshot {
        val prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
        val state =
            runCatching { QuickConnectWidgetConnectionState.valueOf(prefs.getString(KEY_STATE, null) ?: "") }
                .getOrNull()
                ?: QuickConnectWidgetConnectionState.Disconnected

        val sessionId = prefs.getString(KEY_SESSION_ID, null)?.takeIf { it.isNotBlank() }
        val updatedAtMs = prefs.getLong(KEY_UPDATED_AT, 0L).takeIf { it > 0 } ?: System.currentTimeMillis()
        return QuickConnectWidgetConnectionSnapshot(state = state, sessionId = sessionId, updatedAtMs = updatedAtMs)
    }
}

