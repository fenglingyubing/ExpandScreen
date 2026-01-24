package com.expandscreen.ui.settings

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.expandscreen.R
import com.expandscreen.data.repository.SettingsRepository
import com.expandscreen.ui.theme.ExpandScreenTheme
import dagger.hilt.android.AndroidEntryPoint
import javax.inject.Inject

@AndroidEntryPoint
class OpenSourceLicensesActivity : ComponentActivity() {
    @Inject lateinit var settingsRepository: SettingsRepository

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            val settings by settingsRepository.settings.collectAsStateWithLifecycle()
            ExpandScreenTheme(
                themeMode = settings.display.themeMode,
                dynamicColor = settings.display.dynamicColor,
            ) {
                Surface(modifier = Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
                    LicensesScreen(onClose = { finish() })
                }
            }
        }
    }
}

@Composable
private fun LicensesScreen(onClose: () -> Unit) {
    val context = androidx.compose.ui.platform.LocalContext.current
    val apacheText =
        remember {
            runCatching {
                context.resources.openRawResource(R.raw.apache_2_0).bufferedReader().use { it.readText() }
            }.getOrElse { "Failed to load license text: ${it.message ?: it.javaClass.simpleName}" }
        }

    val background =
        Brush.linearGradient(
            0.0f to Color(0xFF070A0F),
            0.7f to Color(0xFF070A0F),
            1.0f to Color(0xFF092018),
        )

    Box(
        modifier =
            Modifier
                .fillMaxSize()
                .background(background)
                .padding(horizontal = 18.dp, vertical = 16.dp),
    ) {
        Column(verticalArrangement = Arrangement.spacedBy(14.dp)) {
            Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                    Text(
                        text = "Open source licenses",
                        style = MaterialTheme.typography.titleLarge,
                        color = Color(0xFFE9F2FF),
                    )
                    Text(
                        text = "Most dependencies are Apache-2.0 licensed.",
                        style = MaterialTheme.typography.bodyMedium,
                        color = Color(0xFFB8C7D9),
                    )
                }
                Button(
                    onClick = onClose,
                    colors =
                        ButtonDefaults.buttonColors(
                            containerColor = Color(0xFF1C2B3A),
                            contentColor = Color(0xFFE9F2FF),
                        ),
                ) {
                    Text("Close", fontFamily = FontFamily.Monospace)
                }
            }

            Column(
                modifier =
                    Modifier
                        .fillMaxWidth()
                        .weight(1f)
                        .background(Color(0xFF0B1118).copy(alpha = 0.92f), shape = MaterialTheme.shapes.large)
                        .padding(14.dp)
                        .verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(10.dp),
            ) {
                Text(
                    text =
                        "Notable libraries:\n" +
                            "• OkHttp (Square)\n" +
                            "• Kotlinx Serialization / Coroutines\n" +
                            "• Room\n" +
                            "• Dagger/Hilt\n" +
                            "• Timber\n\n" +
                            "License text:",
                    style = MaterialTheme.typography.bodyMedium,
                    color = Color(0xFFE9F2FF),
                )
                Spacer(modifier = Modifier.height(6.dp))
                Text(
                    text = apacheText,
                    style = MaterialTheme.typography.bodySmall,
                    fontFamily = FontFamily.Monospace,
                    color = Color(0xFFB8C7D9),
                )
            }
        }
    }
}
