package com.expandscreen.ui

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.tooling.preview.Preview
import com.expandscreen.ui.theme.ExpandScreenTheme
import dagger.hilt.android.AndroidEntryPoint

/**
 * Main Activity - Entry point for the application
 *
 * Displays device list, connection options, and settings access
 */
@AndroidEntryPoint
class MainActivity : ComponentActivity() {

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            ExpandScreenTheme {
                Surface(
                    modifier = Modifier.fillMaxSize(),
                    color = MaterialTheme.colorScheme.background
                ) {
                    MainScreen()
                }
            }
        }
    }
}

@Composable
fun MainScreen() {
    Text(text = "ExpandScreen - Main Screen")
}

@Preview(showBackground = true)
@Composable
fun MainScreenPreview() {
    ExpandScreenTheme {
        MainScreen()
    }
}
