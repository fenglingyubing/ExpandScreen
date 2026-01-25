package com.expandscreen.widget

import android.appwidget.AppWidgetManager
import android.appwidget.AppWidgetProvider
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch

class QuickConnectWidgetProvider : AppWidgetProvider() {
    override fun onUpdate(context: Context, appWidgetManager: AppWidgetManager, appWidgetIds: IntArray) {
        val result = goAsync()
        CoroutineScope(SupervisorJob() + Dispatchers.IO).launch {
            QuickConnectWidgetUpdater.update(context, appWidgetManager, appWidgetIds)
            result.finish()
        }
    }

    override fun onDeleted(context: Context, appWidgetIds: IntArray) {
        for (id in appWidgetIds) {
            QuickConnectWidgetPrefs.clear(context, id)
        }
    }

    override fun onAppWidgetOptionsChanged(
        context: Context,
        appWidgetManager: AppWidgetManager,
        appWidgetId: Int,
        newOptions: android.os.Bundle,
    ) {
        val result = goAsync()
        CoroutineScope(SupervisorJob() + Dispatchers.IO).launch {
            QuickConnectWidgetUpdater.update(context, appWidgetManager, intArrayOf(appWidgetId))
            result.finish()
        }
    }

    override fun onReceive(context: Context, intent: Intent) {
        super.onReceive(context, intent)
        if (intent.action == Intent.ACTION_LOCALE_CHANGED || intent.action == Intent.ACTION_CONFIGURATION_CHANGED) {
            val result = goAsync()
            CoroutineScope(SupervisorJob() + Dispatchers.IO).launch {
                QuickConnectWidgetUpdater.updateAll(context)
                result.finish()
            }
        }
    }

    companion object {
        fun componentName(context: Context): ComponentName =
            ComponentName(context, QuickConnectWidgetProvider::class.java)
    }
}

