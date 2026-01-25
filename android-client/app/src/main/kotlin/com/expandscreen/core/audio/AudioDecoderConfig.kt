package com.expandscreen.core.audio

import android.media.MediaFormat

class AudioDecoderConfig(
    val sampleRate: Int,
    val channelCount: Int,
    val mimeType: String,
    val codecConfig0: ByteArray = ByteArray(0),
    val codecConfig1: ByteArray = ByteArray(0),
    val lowLatency: Boolean = true,
    val maxInputSize: Int? = null,
) {
    override fun equals(other: Any?): Boolean {
        if (this === other) return true
        if (other !is AudioDecoderConfig) return false
        if (sampleRate != other.sampleRate) return false
        if (channelCount != other.channelCount) return false
        if (mimeType != other.mimeType) return false
        if (lowLatency != other.lowLatency) return false
        if (maxInputSize != other.maxInputSize) return false
        if (!codecConfig0.contentEquals(other.codecConfig0)) return false
        if (!codecConfig1.contentEquals(other.codecConfig1)) return false
        return true
    }

    override fun hashCode(): Int {
        var result = sampleRate
        result = 31 * result + channelCount
        result = 31 * result + mimeType.hashCode()
        result = 31 * result + codecConfig0.contentHashCode()
        result = 31 * result + codecConfig1.contentHashCode()
        result = 31 * result + lowLatency.hashCode()
        result = 31 * result + (maxInputSize ?: 0)
        return result
    }

    companion object {
        val DEFAULT_AAC = AudioDecoderConfig(sampleRate = 48_000, channelCount = 2, mimeType = MediaFormat.MIMETYPE_AUDIO_AAC)
        val DEFAULT_OPUS = AudioDecoderConfig(sampleRate = 48_000, channelCount = 2, mimeType = MediaFormat.MIMETYPE_AUDIO_OPUS)
    }
}
