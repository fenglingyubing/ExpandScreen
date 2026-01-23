package com.expandscreen.core.decoder

import com.expandscreen.protocol.VideoFrameMessage

class EncodedFrame(
    val data: ByteArray,
    val size: Int,
    val presentationTimeUs: Long,
    val isKeyFrame: Boolean,
    val width: Int,
    val height: Int,
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
            message: VideoFrameMessage,
            timestampMs: Long,
            pool: FrameBufferPool? = null,
        ): EncodedFrame {
            val payloadSize = message.data.size
            val buffer = pool?.acquire(payloadSize) ?: message.data
            if (buffer !== message.data) {
                System.arraycopy(message.data, 0, buffer, 0, payloadSize)
            }

            return EncodedFrame(
                data = buffer,
                size = payloadSize,
                presentationTimeUs = timestampMs * 1000,
                isKeyFrame = message.isKeyFrame,
                width = message.width,
                height = message.height,
                onRelease = if (pool == null || buffer === message.data) null else pool::release,
            )
        }
    }
}
