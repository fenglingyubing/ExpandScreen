package com.expandscreen.widget

import android.content.Context

object QuickConnectWidgetPrefs {
    private const val PREFS_NAME = "quick_connect_widget_prefs"
    private const val KEY_SELECTED_DEVICE_ID_PREFIX = "selected_device_id_"

    fun setSelectedDeviceId(context: Context, appWidgetId: Int, deviceId: Long?) {
        val prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
        prefs.edit().apply {
            val key = "$KEY_SELECTED_DEVICE_ID_PREFIX$appWidgetId"
            if (deviceId == null) remove(key) else putLong(key, deviceId)
        }.apply()
    }

    fun getSelectedDeviceId(context: Context, appWidgetId: Int): Long? {
        val prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
        val key = "$KEY_SELECTED_DEVICE_ID_PREFIX$appWidgetId"
        return if (!prefs.contains(key)) null else prefs.getLong(key, -1L).takeIf { it > 0 }
    }

    fun clear(context: Context, appWidgetId: Int) {
        val prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
        prefs.edit().remove("$KEY_SELECTED_DEVICE_ID_PREFIX$appWidgetId").apply()
    }
}

