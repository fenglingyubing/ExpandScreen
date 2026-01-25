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
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowBack
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.expandscreen.core.security.TrustedHostStore
import com.expandscreen.ui.theme.ExpandScreenTheme
import dagger.hilt.android.AndroidEntryPoint
import javax.inject.Inject

@AndroidEntryPoint
class TrustedHostsActivity : ComponentActivity() {
    @Inject lateinit var store: TrustedHostStore

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            ExpandScreenTheme {
                TrustedHostsScreen(
                    store = store,
                    onBack = { finish() },
                )
            }
        }
    }
}

@Composable
private fun TrustedHostsScreen(
    store: TrustedHostStore,
    onBack: () -> Unit,
) {
    val bg =
        Brush.verticalGradient(
            0f to Color(0xFF06090E),
            0.45f to Color(0xFF07121C),
            1f to Color(0xFF030507),
        )

    var hosts by remember { mutableStateOf(emptyList<TrustedHostStore.TrustedHost>()) }
    var confirmClearAll by remember { mutableStateOf(false) }

    LaunchedEffect(Unit) {
        hosts = store.listTrustedHosts()
    }

    Box(
        modifier =
            Modifier
                .fillMaxSize()
                .background(bg)
                .padding(18.dp),
    ) {
        Column(verticalArrangement = Arrangement.spacedBy(14.dp)) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(10.dp),
            ) {
                IconButton(onClick = onBack) {
                    Icon(Icons.Filled.ArrowBack, contentDescription = "Back", tint = Color(0xFFE9F2FF))
                }

                Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                    Text(
                        text = "Trusted devices",
                        style = MaterialTheme.typography.titleLarge,
                        color = Color(0xFFE9F2FF),
                    )
                    Text(
                        text = "Pinned Windows certificates (TLS)",
                        style = MaterialTheme.typography.bodyMedium,
                        color = Color(0xFFB8C7D9),
                    )
                }

                if (hosts.isNotEmpty()) {
                    OutlinedButton(onClick = { confirmClearAll = true }) {
                        Text("Clear all")
                    }
                }
            }

            if (hosts.isEmpty()) {
                Card(
                    colors =
                        CardDefaults.cardColors(
                            containerColor = Color(0xFF0B1118),
                            contentColor = Color(0xFFE9F2FF),
                        ),
                ) {
                    Column(modifier = Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                        Text("No trusted devices yet", style = MaterialTheme.typography.titleMedium)
                        Text(
                            "Enable TLS and pair once on the main screen.",
                            style = MaterialTheme.typography.bodyMedium,
                            color = Color(0xFFB8C7D9),
                        )
                    }
                }
            } else {
                LazyColumn(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                    items(hosts, key = { it.host }) { item ->
                        TrustedHostRow(
                            host = item.host,
                            fingerprintHex = item.fingerprintSha256Hex,
                            onRevoke = {
                                store.revokeHost(item.host)
                                hosts = store.listTrustedHosts()
                            },
                        )
                    }
                }
            }
        }

        if (confirmClearAll) {
            AlertDialog(
                onDismissRequest = { confirmClearAll = false },
                title = { Text("Clear all trusted devices?") },
                text = { Text("This will require pairing again on next TLS connection.") },
                confirmButton = {
                    Button(
                        onClick = {
                            store.clearAll()
                            hosts = emptyList()
                            confirmClearAll = false
                        },
                    ) {
                        Text("Clear")
                    }
                },
                dismissButton = {
                    TextButton(onClick = { confirmClearAll = false }) {
                        Text("Cancel")
                    }
                },
            )
        }
    }
}

@Composable
private fun TrustedHostRow(
    host: String,
    fingerprintHex: String,
    onRevoke: () -> Unit,
) {
    Card(
        modifier = Modifier.fillMaxWidth(),
        colors =
            CardDefaults.cardColors(
                containerColor = Color(0xFF0B1118),
                contentColor = Color(0xFFE9F2FF),
            ),
    ) {
        Row(
            modifier = Modifier.padding(14.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                Text(
                    text = host,
                    style = MaterialTheme.typography.titleMedium,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Text(
                    text = fingerprintHex.chunked(2).chunked(16).joinToString("\n") { line -> line.joinToString(":") },
                    style = MaterialTheme.typography.bodySmall,
                    fontFamily = FontFamily.Monospace,
                    color = Color(0xFFB8C7D9),
                )
            }

            Spacer(modifier = Modifier.width(10.dp))

            IconButton(onClick = onRevoke, modifier = Modifier.size(40.dp)) {
                Icon(Icons.Filled.Delete, contentDescription = "Revoke", tint = Color(0xFFFF8DA1))
            }
        }
    }
}

