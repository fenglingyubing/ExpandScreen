package com.expandscreen.widget

import android.app.PendingIntent
import android.appwidget.AppWidgetManager
import android.content.Context
import android.content.Intent
import android.os.Bundle
import android.text.format.DateUtils
import android.widget.RemoteViews
import androidx.core.content.ContextCompat
import com.expandscreen.R
import com.expandscreen.data.model.WindowsDeviceEntity
import com.expandscreen.ui.MainActivity
import dagger.hilt.android.EntryPointAccessors
import kotlinx.coroutines.flow.first

object QuickConnectWidgetUpdater {
    suspend fun updateAll(context: Context) {
        val appWidgetManager = AppWidgetManager.getInstance(context)
        val ids = appWidgetManager.getAppWidgetIds(QuickConnectWidgetProvider.componentName(context))
        update(context, appWidgetManager, ids)
    }

    suspend fun update(context: Context, appWidgetManager: AppWidgetManager, appWidgetIds: IntArray) {
        if (appWidgetIds.isEmpty()) return

        val entryPoint = EntryPointAccessors.fromApplication(context, WidgetEntryPoint::class.java)
        val deviceRepository = entryPoint.deviceRepository()
        val snapshot = QuickConnectWidgetStatusStore.read(context)

        for (appWidgetId in appWidgetIds) {
            val options = appWidgetManager.getAppWidgetOptions(appWidgetId)
            val layoutId = pickLayout(options)

            val selectedDevice =
                QuickConnectWidgetPrefs.getSelectedDeviceId(context, appWidgetId)?.let { deviceId ->
                    deviceRepository.getDeviceById(deviceId)
                } ?: deviceRepository.getFavoriteDevices().first().firstOrNull()

            val views =
                buildRemoteViews(
                    context = context,
                    appWidgetId = appWidgetId,
                    layoutId = layoutId,
                    selectedDevice = selectedDevice,
                    snapshot = snapshot,
                )

            appWidgetManager.updateAppWidget(appWidgetId, views)
        }
    }

    private fun pickLayout(options: Bundle?): Int {
        val minWidth = options?.getInt(AppWidgetManager.OPTION_APPWIDGET_MIN_WIDTH) ?: 0
        val minHeight = options?.getInt(AppWidgetManager.OPTION_APPWIDGET_MIN_HEIGHT) ?: 0

        return when {
            minHeight < 100 && minWidth < 180 -> R.layout.widget_quick_connect_2x1
            minHeight < 100 -> R.layout.widget_quick_connect_4x1
            else -> R.layout.widget_quick_connect_4x2
        }
    }

    private fun buildRemoteViews(
        context: Context,
        appWidgetId: Int,
        layoutId: Int,
        selectedDevice: WindowsDeviceEntity?,
        snapshot: QuickConnectWidgetConnectionSnapshot,
    ): RemoteViews {
        val views = RemoteViews(context.packageName, layoutId)

        views.setTextViewText(R.id.widget_title, context.getString(R.string.widget_quick_connect_title))

        val statusText =
            when (snapshot.state) {
                QuickConnectWidgetConnectionState.Connected ->
                    context.getString(
                        R.string.widget_status_connected,
                        snapshot.sessionId?.takeLast(6)?.uppercase().orEmpty(),
                    )
                QuickConnectWidgetConnectionState.Connecting -> context.getString(R.string.widget_status_connecting)
                QuickConnectWidgetConnectionState.Disconnected -> context.getString(R.string.widget_status_disconnected)
            }
        views.setTextViewText(R.id.widget_status, statusText)

        val statusColor =
            when (snapshot.state) {
                QuickConnectWidgetConnectionState.Connected -> ContextCompat.getColor(context, R.color.widget_status_ok)
                QuickConnectWidgetConnectionState.Connecting -> ContextCompat.getColor(context, R.color.widget_status_warn)
                QuickConnectWidgetConnectionState.Disconnected -> ContextCompat.getColor(context, R.color.widget_status_idle)
            }
        views.setInt(R.id.widget_status_dot, "setColorFilter", statusColor)

        val deviceLabel = selectedDevice?.deviceName ?: context.getString(R.string.widget_no_favorite)
        views.setTextViewText(R.id.widget_device_name, deviceLabel)

        if (layoutId == R.layout.widget_quick_connect_4x2) {
            val subtitle =
                if (selectedDevice == null) {
                    context.getString(R.string.widget_subtitle_pick_favorite)
                } else {
                    val connectedAgo =
                        DateUtils.getRelativeTimeSpanString(
                            selectedDevice.lastConnected,
                            System.currentTimeMillis(),
                            DateUtils.MINUTE_IN_MILLIS,
                            DateUtils.FORMAT_ABBREV_RELATIVE,
                        )
                    val ip = selectedDevice.ipAddress?.takeIf { it.isNotBlank() } ?: "USB"
                    context.getString(R.string.widget_subtitle_device_detail, selectedDevice.connectionType, ip, connectedAgo)
                }
            views.setTextViewText(R.id.widget_subtitle, subtitle)
        }

        val openAppIntent =
            PendingIntent.getActivity(
                context,
                appWidgetId,
                Intent(context, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
            )
        views.setOnClickPendingIntent(R.id.widget_root, openAppIntent)

        val actionIntent =
            if (selectedDevice == null) {
                Intent(context, QuickConnectWidgetConfigureActivity::class.java).apply {
                    putExtra(AppWidgetManager.EXTRA_APPWIDGET_ID, appWidgetId)
                }
            } else {
                Intent(context, MainActivity::class.java).apply {
                    action = QuickConnectWidgetIntents.ACTION_QUICK_CONNECT
                    putExtra(QuickConnectWidgetIntents.EXTRA_DEVICE_ID, selectedDevice.id)
                    addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP)
                }
            }

        val actionPendingIntent =
            PendingIntent.getActivity(
                context,
                appWidgetId + 10_000,
                actionIntent,
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
            )
        views.setOnClickPendingIntent(R.id.widget_action, actionPendingIntent)

        val actionLabel =
            if (selectedDevice == null) {
                context.getString(R.string.widget_action_choose)
            } else {
                context.getString(R.string.widget_action_connect)
            }
        views.setTextViewText(R.id.widget_action, actionLabel)

        val actionHint =
            if (layoutId == R.layout.widget_quick_connect_2x1) {
                null
            } else {
                if (selectedDevice == null) {
                    context.getString(R.string.widget_hint_choose)
                } else {
                    context.getString(R.string.widget_hint_tap_to_connect)
                }
            }
        if (actionHint != null) {
            views.setTextViewText(R.id.widget_hint, actionHint)
        }

        return views
    }
}

