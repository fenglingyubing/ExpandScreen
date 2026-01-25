package com.expandscreen.core.audio

data class PcmAudioFormat(
    val sampleRate: Int,
    val channelCount: Int,
    val pcmEncoding: Int,
)

class PcmAudioFrame(
    val data: ByteArray,
    val size: Int,
    val presentationTimeUs: Long,
    val format: PcmAudioFormat,
    private val onRelease: ((ByteArray) -> Unit)? = null,
) {
    init {
        require(size >= 0) { "size must be >= 0" }
        require(size <= data.size) { "size must be <= data.size" }
    }

    fun release() {
        onRelease?.invoke(data)
    }
}

