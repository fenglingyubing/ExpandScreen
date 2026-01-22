package com.expandscreen.service

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.Intent
import android.os.Binder
import android.os.Build
import android.os.IBinder
import androidx.core.app.NotificationCompat
import dagger.hilt.android.AndroidEntryPoint
import timber.log.Timber

/**
 * Display Service
 *
 * Foreground service that handles:
 * - Network data reception
 * - Video decoding
 * - Frame rendering coordination
 * - WakeLock management
 */
@AndroidEntryPoint
class DisplayService : Service() {

    private val binder = DisplayServiceBinder()

    companion object {
        private const val NOTIFICATION_CHANNEL_ID = "expandscreen_display"
        private const val NOTIFICATION_ID = 1001
    }

    inner class DisplayServiceBinder : Binder() {
        fun getService(): DisplayService = this@DisplayService
    }

    override fun onCreate() {
        super.onCreate()
        Timber.d("DisplayService created")
        createNotificationChannel()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        Timber.d("DisplayService started")

        // Start foreground service with notification
        val notification = createNotification()
        startForeground(NOTIFICATION_ID, notification)

        // TODO: Start data processing loop
        // - Network receive → Decode → Render

        return START_STICKY
    }

    override fun onBind(intent: Intent?): IBinder {
        return binder
    }

    override fun onDestroy() {
        super.onDestroy()
        Timber.d("DisplayService destroyed")
        // TODO: Release all resources
    }

    private fun createNotificationChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val channel = NotificationChannel(
                NOTIFICATION_CHANNEL_ID,
                "ExpandScreen Display",
                NotificationManager.IMPORTANCE_LOW
            ).apply {
                description = "Displays connection status and performance metrics"
            }

            val notificationManager = getSystemService(NotificationManager::class.java)
            notificationManager.createNotificationChannel(channel)
        }
    }

    private fun createNotification(): Notification {
        return NotificationCompat.Builder(this, NOTIFICATION_CHANNEL_ID)
            .setContentTitle("ExpandScreen")
            .setContentText("Connected and streaming")
            .setSmallIcon(android.R.drawable.ic_dialog_info) // TODO: Use custom icon
            .setPriority(NotificationCompat.PRIORITY_LOW)
            .setOngoing(true)
            .build()
    }

    /**
     * Update notification with real-time info
     */
    fun updateNotification(fps: Int, latency: Int) {
        val notification = NotificationCompat.Builder(this, NOTIFICATION_CHANNEL_ID)
            .setContentTitle("ExpandScreen - Active")
            .setContentText("FPS: $fps | Latency: ${latency}ms")
            .setSmallIcon(android.R.drawable.ic_dialog_info)
            .setPriority(NotificationCompat.PRIORITY_LOW)
            .setOngoing(true)
            .build()

        val notificationManager = getSystemService(NotificationManager::class.java)
        notificationManager.notify(NOTIFICATION_ID, notification)
    }
}
