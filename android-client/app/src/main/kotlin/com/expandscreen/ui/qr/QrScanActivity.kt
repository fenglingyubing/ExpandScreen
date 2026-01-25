package com.expandscreen.ui.qr

import android.app.Activity
import android.content.Intent
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.expandscreen.data.repository.SettingsRepository
import com.expandscreen.ui.theme.ExpandScreenTheme
import dagger.hilt.android.AndroidEntryPoint
import javax.inject.Inject

@AndroidEntryPoint
class QrScanActivity : ComponentActivity() {
    @Inject lateinit var settingsRepository: SettingsRepository

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            val settings by settingsRepository.settings.collectAsStateWithLifecycle()
            ExpandScreenTheme(
                themeMode = settings.display.themeMode,
                dynamicColor = settings.display.dynamicColor,
            ) {
                Surface(
                    modifier = Modifier.fillMaxSize(),
                    color = MaterialTheme.colorScheme.background,
                ) {
                    QrScanRoute(
                        onClose = {
                            setResult(Activity.RESULT_CANCELED)
                            finish()
                        },
                        onQrScanned = { raw ->
                            setResult(
                                Activity.RESULT_OK,
                                Intent().putExtra(EXTRA_QR_RAW, raw),
                            )
                            finish()
                        },
                    )
                }
            }
        }
    }

    companion object {
        const val EXTRA_QR_RAW = "com.expandscreen.extra.QR_RAW"
    }
}

