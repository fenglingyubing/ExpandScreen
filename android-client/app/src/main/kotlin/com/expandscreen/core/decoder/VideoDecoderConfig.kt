package com.expandscreen.core.decoder

import android.media.MediaFormat
import android.view.Surface

data class VideoDecoderConfig(
    val width: Int,
    val height: Int,
    val outputSurface: Surface,
    val mimeType: String = MediaFormat.MIMETYPE_VIDEO_AVC,
    val lowLatency: Boolean = true,
    val maxInputSize: Int? = null,
    val operatingRate: Float? = null,
)
