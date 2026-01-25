package com.expandscreen.ui.qr

import android.Manifest
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import android.os.VibrationEffect
import android.os.Vibrator
import android.provider.Settings
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.camera.core.CameraSelector
import androidx.camera.core.ImageAnalysis
import androidx.camera.core.Preview
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.view.PreviewView
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.WarningAmber
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalLifecycleOwner
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.content.ContextCompat
import com.google.mlkit.vision.barcode.BarcodeScannerOptions
import com.google.mlkit.vision.barcode.BarcodeScanning
import com.google.mlkit.vision.barcode.common.Barcode
import com.google.mlkit.vision.common.InputImage
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean

@Composable
fun QrScanRoute(
    onClose: () -> Unit,
    onQrScanned: (String) -> Unit,
) {
    val context = LocalContext.current
    var hasCameraPermission by remember { mutableStateOf(context.hasCameraPermission()) }
    var showPermissionHelp by remember { mutableStateOf(false) }

    val permissionLauncher =
        rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
            hasCameraPermission = granted
            showPermissionHelp = !granted
        }

    LaunchedEffect(Unit) {
        if (!hasCameraPermission) {
            permissionLauncher.launch(Manifest.permission.CAMERA)
        }
    }

    Box(modifier = Modifier.fillMaxSize().background(Color.Black)) {
        AnimatedVisibility(visible = hasCameraPermission) {
            QrCameraPreview(
                onQrScanned = { raw ->
                    context.vibrateOnScan()
                    onQrScanned(raw)
                },
            )
        }

        ScanChrome(
            onClose = onClose,
            showPermissionHelp = showPermissionHelp && !hasCameraPermission,
            onRequestPermission = { permissionLauncher.launch(Manifest.permission.CAMERA) },
            onOpenSettings = { context.openAppSettings() },
        )
    }
}

@Composable
private fun ScanChrome(
    onClose: () -> Unit,
    showPermissionHelp: Boolean,
    onRequestPermission: () -> Unit,
    onOpenSettings: () -> Unit,
) {
    val shimmer by
        rememberInfiniteTransition(label = "qr-shimmer")
            .animateFloat(
                initialValue = 0.35f,
                targetValue = 1f,
                animationSpec = infiniteRepeatable(tween(900), repeatMode = RepeatMode.Reverse),
                label = "qr-shimmer-alpha",
            )

    Box(modifier = Modifier.fillMaxSize()) {
        IconButton(
            onClick = onClose,
            modifier =
                Modifier
                    .align(Alignment.TopStart)
                    .padding(12.dp)
                    .size(44.dp)
                    .clip(RoundedCornerShape(14.dp))
                    .background(Color(0xFF0B1118).copy(alpha = 0.72f)),
        ) {
            Icon(
                imageVector = Icons.Filled.Close,
                contentDescription = "Close",
                tint = Color(0xFFE9F2FF),
            )
        }

        Column(
            modifier =
                Modifier
                    .align(Alignment.TopCenter)
                    .padding(top = 18.dp)
                    .clip(RoundedCornerShape(18.dp))
                    .background(Color(0xFF0B1118).copy(alpha = 0.62f))
                    .border(
                        1.dp,
                        Brush.linearGradient(
                            listOf(Color(0xFF7CFAC6).copy(alpha = 0.85f), Color(0xFF6BB5FF).copy(alpha = 0.6f)),
                        ),
                        RoundedCornerShape(18.dp),
                    )
                    .padding(horizontal = 14.dp, vertical = 10.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(2.dp),
        ) {
            Text(
                text = "Scan to connect",
                style = MaterialTheme.typography.titleMedium,
                color = Color(0xFFE9F2FF),
            )
            Text(
                text = "Align the code inside the frame",
                style = MaterialTheme.typography.bodyMedium,
                color = Color(0xFFB8C7D9).copy(alpha = shimmer),
            )
        }

        BoxWithConstraints(
            modifier = Modifier.fillMaxSize(),
            contentAlignment = Alignment.Center,
        ) {
            val size = (maxWidth.coerceAtMost(maxHeight) * 0.68f).coerceAtMost(320.dp)
            ScanFrame(modifier = Modifier.size(size))
        }

        Column(
            modifier =
                Modifier
                    .align(Alignment.BottomCenter)
                    .padding(18.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            AnimatedVisibility(visible = showPermissionHelp) {
                Card(
                    colors =
                        CardDefaults.cardColors(
                            containerColor = Color(0xFF2A1B1F),
                            contentColor = Color(0xFFFFE8EF),
                        ),
                    shape = RoundedCornerShape(18.dp),
                    elevation = CardDefaults.cardElevation(defaultElevation = 0.dp),
                ) {
                    Column(modifier = Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                            Icon(
                                imageVector = Icons.Filled.WarningAmber,
                                contentDescription = null,
                                tint = Color(0xFFFFD36B),
                            )
                            Text(
                                text = "Camera permission required",
                                style = MaterialTheme.typography.titleSmall,
                            )
                        }
                        Text(
                            text = "Enable camera to scan the QR code and auto-connect.",
                            style = MaterialTheme.typography.bodyMedium,
                            color = Color(0xFFFFE8EF).copy(alpha = 0.86f),
                            textAlign = TextAlign.Start,
                        )

                        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                            Button(
                                onClick = onRequestPermission,
                                colors =
                                    ButtonDefaults.buttonColors(
                                        containerColor = Color(0xFF1B2A24),
                                        contentColor = Color(0xFFE9FFF6),
                                    ),
                                modifier = Modifier.weight(1f),
                            ) {
                                Text("Grant")
                            }
                            Spacer(modifier = Modifier.width(6.dp))
                            Button(
                                onClick = onOpenSettings,
                                colors =
                                    ButtonDefaults.buttonColors(
                                        containerColor = Color(0xFF131A24),
                                        contentColor = Color(0xFFE9F2FF),
                                    ),
                                modifier = Modifier.weight(1f),
                            ) {
                                Text("Settings")
                            }
                        }
                    }
                }
            }

            Text(
                text = "Tip: The Windows app can show a connection QR code with IP + port.",
                style = MaterialTheme.typography.bodySmall,
                color = Color(0xFFB8C7D9).copy(alpha = 0.78f),
                textAlign = TextAlign.Center,
                modifier = Modifier.alpha(0.95f),
            )
        }
    }
}

@Composable
private fun ScanFrame(modifier: Modifier = Modifier) {
    val line by
        rememberInfiniteTransition(label = "scanline")
            .animateFloat(
                initialValue = 0f,
                targetValue = 1f,
                animationSpec = infiniteRepeatable(tween(1400), repeatMode = RepeatMode.Reverse),
                label = "scanline-progress",
            )

    Box(
        modifier =
            modifier
                .clip(RoundedCornerShape(26.dp))
                .border(
                    width = 1.dp,
                    brush =
                        Brush.linearGradient(
                            listOf(
                                Color(0xFF6BB5FF).copy(alpha = 0.65f),
                                Color(0xFF7CFAC6).copy(alpha = 0.8f),
                            ),
                        ),
                    shape = RoundedCornerShape(26.dp),
                )
                .background(Color(0xFF000000).copy(alpha = 0.12f)),
    ) {
        Box(
            modifier =
                Modifier
                    .fillMaxSize()
                    .padding(horizontal = 12.dp)
                    .height(2.dp)
                    .align(Alignment.TopCenter)
                    .padding(top = (12.dp + (line * 220f).dp)),
        ) {
            Box(
                modifier =
                    Modifier
                        .fillMaxSize()
                        .background(
                            Brush.horizontalGradient(
                                listOf(
                                    Color.Transparent,
                                    Color(0xFF7CFAC6).copy(alpha = 0.85f),
                                    Color.Transparent,
                                ),
                            ),
                        ),
            )
        }
    }
}

@Composable
private fun QrCameraPreview(onQrScanned: (String) -> Unit) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current
    val previewView = remember { PreviewView(context) }

    DisposableEffect(Unit) {
        val handled = AtomicBoolean(false)
        val cameraExecutor = Executors.newSingleThreadExecutor()
        val options = BarcodeScannerOptions.Builder().setBarcodeFormats(Barcode.FORMAT_QR_CODE).build()
        val scanner = BarcodeScanning.getClient(options)

        val analysis =
            ImageAnalysis.Builder()
                .setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST)
                .build()
                .also { imageAnalysis ->
                    imageAnalysis.setAnalyzer(cameraExecutor) { imageProxy ->
                        val mediaImage = imageProxy.image
                        if (mediaImage == null || handled.get()) {
                            imageProxy.close()
                            return@setAnalyzer
                        }

                        val input =
                            InputImage.fromMediaImage(
                                mediaImage,
                                imageProxy.imageInfo.rotationDegrees,
                            )

                        scanner
                            .process(input)
                            .addOnSuccessListener { barcodes ->
                                if (handled.get()) return@addOnSuccessListener
                                val raw = barcodes.firstOrNull()?.rawValue?.trim()
                                if (!raw.isNullOrBlank() && handled.compareAndSet(false, true)) {
                                    onQrScanned(raw)
                                }
                            }
                            .addOnCompleteListener {
                                imageProxy.close()
                            }
                    }
                }

        val providerFuture = ProcessCameraProvider.getInstance(context)
        val executor = ContextCompat.getMainExecutor(context)
        providerFuture.addListener(
            {
                val provider = providerFuture.get()
                val preview =
                    Preview.Builder().build().also {
                        it.setSurfaceProvider(previewView.surfaceProvider)
                    }
                runCatching {
                    provider.unbindAll()
                    provider.bindToLifecycle(
                        lifecycleOwner,
                        CameraSelector.DEFAULT_BACK_CAMERA,
                        preview,
                        analysis,
                    )
                }
            },
            executor,
        )

        onDispose {
            runCatching { providerFuture.get().unbindAll() }
            runCatching { scanner.close() }
            cameraExecutor.shutdown()
        }
    }

    AndroidView(
        factory = { previewView },
        modifier = Modifier.fillMaxSize(),
    )
}

private fun Context.hasCameraPermission(): Boolean {
    return ContextCompat.checkSelfPermission(this, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED
}

private fun Context.openAppSettings() {
    val intent =
        Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS).apply {
            data = android.net.Uri.fromParts("package", packageName, null)
            addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        }
    startActivity(intent)
}

private fun Context.vibrateOnScan() {
    val vibrator = getSystemService(Vibrator::class.java) ?: return
    if (!vibrator.hasVibrator()) return

    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
        vibrator.vibrate(VibrationEffect.createOneShot(26, VibrationEffect.DEFAULT_AMPLITUDE))
    } else {
        @Suppress("DEPRECATION")
        vibrator.vibrate(26)
    }
}
