package com.expandscreen.core.audio

import com.expandscreen.core.decoder.FrameBufferPool
import com.expandscreen.protocol.AudioFrameMessage

class EncodedAudioFrame(
    val data: ByteArray,
    val size: Int,
    val presentationTimeUs: Long,
    val frameNumber: Int,
    val decoderConfig: AudioDecoderConfig,
    private val onRelease: ((ByteArray) -> Unit)? = null,
) {
    init {
        require(size >= 0) { "size must be >= 0" }
        require(size <= data.size) { "size must be <= data.size" }
    }

    fun release() {
        onRelease?.invoke(data)
    }

    companion object {
        fun fromMessage(
            message: AudioFrameMessage,
            timestampMs: Long,
            pool: FrameBufferPool? = null,
        ): EncodedAudioFrame {
            val payloadSize = message.data.size
            val buffer = pool?.acquire(payloadSize) ?: message.data
            if (buffer !== message.data) {
                System.arraycopy(message.data, 0, buffer, 0, payloadSize)
            }

            return EncodedAudioFrame(
                data = buffer,
                size = payloadSize,
                presentationTimeUs = timestampMs * 1000,
                frameNumber = message.frameNumber,
                decoderConfig =
                    AudioDecoderConfig(
                        sampleRate = message.sampleRate,
                        channelCount = message.channelCount,
                        mimeType = message.mimeType,
                        codecConfig0 = message.codecConfig0,
                        codecConfig1 = message.codecConfig1,
                    ),
                onRelease = if (pool == null || buffer === message.data) null else pool::release,
            )
        }
    }
}

