package com.expandscreen.ui.settings

import android.os.Bundle
import android.widget.ImageButton
import androidx.fragment.app.FragmentActivity
import com.expandscreen.R
import dagger.hilt.android.AndroidEntryPoint

@AndroidEntryPoint
class SettingsActivity : FragmentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_settings)

        findViewById<ImageButton>(R.id.settings_back)?.setOnClickListener {
            finish()
        }
    }
}

