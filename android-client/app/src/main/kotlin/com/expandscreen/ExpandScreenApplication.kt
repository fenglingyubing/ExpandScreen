package com.expandscreen

import android.app.Application
import dagger.hilt.android.HiltAndroidApp
import javax.inject.Inject
import com.expandscreen.widget.WidgetSyncObserver
import timber.log.Timber

/**
 * ExpandScreen Application Class
 *
 * Initializes Hilt dependency injection and Timber logging.
 */
@HiltAndroidApp
class ExpandScreenApplication : Application() {

    @Inject lateinit var widgetSyncObserver: WidgetSyncObserver

    override fun onCreate() {
        super.onCreate()

        // Initialize Timber for logging
        if (BuildConfig.DEBUG) {
            Timber.plant(Timber.DebugTree())
        } else {
            // In production, consider using a custom tree that reports to crash analytics
            Timber.plant(ReleaseTree())
        }

        widgetSyncObserver.start()
        Timber.i("ExpandScreen Application initialized")
    }

    /**
     * Custom Timber tree for release builds
     * Filters out verbose and debug logs, only logs warnings and errors
     */
    private class ReleaseTree : Timber.Tree() {
        override fun log(priority: Int, tag: String?, message: String, t: Throwable?) {
            if (priority == android.util.Log.ERROR || priority == android.util.Log.WARN) {
                // In production, send to crash analytics service
                // Example: Crashlytics.log(message)
            }
        }
    }
}
