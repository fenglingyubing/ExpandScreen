package com.expandscreen.ui

import android.os.Bundle
import android.view.WindowManager
import androidx.activity.ComponentActivity
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import dagger.hilt.android.AndroidEntryPoint
import timber.log.Timber

/**
 * Display Activity - Full screen video display
 *
 * This activity displays the decoded video stream from the Windows PC
 * in full-screen mode using OpenGL ES rendering.
 */
@AndroidEntryPoint
class DisplayActivity : ComponentActivity() {

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        Timber.d("DisplayActivity created")

        // Keep screen on during video display
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)

        // Enable immersive full-screen mode
        setupFullScreen()

        // TODO: Setup GLSurfaceView with renderer
        // setContentView(...)
    }

    private fun setupFullScreen() {
        WindowCompat.setDecorFitsSystemWindows(window, false)
        WindowInsetsControllerCompat(window, window.decorView).let { controller ->
            controller.hide(WindowInsetsCompat.Type.systemBars())
            controller.systemBarsBehavior =
                WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
        }
    }

    override fun onResume() {
        super.onResume()
        Timber.d("DisplayActivity resumed - starting video playback")
        // TODO: Resume video decoding and rendering
    }

    override fun onPause() {
        super.onPause()
        Timber.d("DisplayActivity paused")
        // TODO: Optionally pause rendering to save battery
    }

    override fun onDestroy() {
        super.onDestroy()
        Timber.d("DisplayActivity destroyed - releasing resources")
        // TODO: Release decoder, renderer, and network resources
    }
}
