package com.expandscreen.core.decoder

import android.media.MediaCodec
import android.media.MediaFormat
import android.os.Build
import timber.log.Timber
import java.util.concurrent.ArrayBlockingQueue
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean

/**
 * Video Decoder Interface
 *
 * Defines the contract for video decoding implementations.
 */
interface VideoDecoder {
    fun initialize(config: VideoDecoderConfig)
    fun enqueueFrame(frame: EncodedFrame): Boolean
    fun flush()
    fun release()
}

/**
 * H.264 Video Decoder Implementation using MediaCodec.
 */
class H264Decoder(
    private val frameQueueCapacity: Int = 6,
    private val inputTimeoutUs: Long = 5_000,
    private val outputTimeoutUs: Long = 0,
) : VideoDecoder {

    private val frameQueue = ArrayBlockingQueue<EncodedFrame>(frameQueueCapacity)
    private val bufferInfo = MediaCodec.BufferInfo()
    private val running = AtomicBoolean(false)
    private val codecLock = Any()

    private var codec: MediaCodec? = null
    private var config: VideoDecoderConfig? = null
    private var decodeThread: Thread? = null
    private var needsKeyFrame = true

    override fun initialize(config: VideoDecoderConfig) {
        stopDecodeThread()
        synchronized(codecLock) {
            releaseCodec()
            this.config = config
            this.needsKeyFrame = true
            this.codec = createCodec(config)
        }
        startDecodeThread()
    }

    override fun enqueueFrame(frame: EncodedFrame): Boolean {
        if (!running.get()) {
            frame.release()
            return false
        }

        if (frameQueue.offer(frame)) {
            return true
        }

        val dropped = frameQueue.poll()
        dropped?.release()
        val accepted = frameQueue.offer(frame)
        if (!accepted) {
            frame.release()
        }
        return accepted
    }

    override fun flush() {
        synchronized(codecLock) {
            needsKeyFrame = true
            clearQueuedFrames()
            runCatching { codec?.flush() }
                .onFailure { Timber.w(it, "Decoder flush failed") }
        }
    }

    override fun release() {
        stopDecodeThread()
        synchronized(codecLock) {
            clearQueuedFrames()
            releaseCodec()
            config = null
        }
    }

    private fun startDecodeThread() {
        if (running.get()) return
        running.set(true)
        decodeThread =
            Thread({ decodeLoop() }, "H264DecodeThread").apply {
                isDaemon = true
                start()
            }
    }

    private fun stopDecodeThread() {
        if (!running.get()) return
        running.set(false)
        decodeThread?.interrupt()
        if (decodeThread != Thread.currentThread()) {
            runCatching { decodeThread?.join(1_000) }
                .onFailure { Timber.w(it, "Decode thread join failed") }
        }
        decodeThread = null
    }

    private fun decodeLoop() {
        while (running.get()) {
            val frame =
                try {
                    frameQueue.poll(10, TimeUnit.MILLISECONDS)
                } catch (e: InterruptedException) {
                    return
                } ?: continue
            try {
                val activeConfig = config
                if (activeConfig == null) continue

                if (frame.width != activeConfig.width || frame.height != activeConfig.height) {
                    reconfigureForFrame(frame, activeConfig)
                    continue
                }

                if (needsKeyFrame && !frame.isKeyFrame) continue

                val activeCodec = codec
                if (activeCodec == null) continue

                val inputIndex = activeCodec.dequeueInputBuffer(inputTimeoutUs)
                if (inputIndex < 0) continue

                val inputBuffer = activeCodec.getInputBuffer(inputIndex)
                if (inputBuffer == null) {
                    activeCodec.queueInputBuffer(inputIndex, 0, 0, frame.presentationTimeUs, 0)
                    continue
                }
                if (frame.size > inputBuffer.capacity()) {
                    activeCodec.queueInputBuffer(inputIndex, 0, 0, frame.presentationTimeUs, 0)
                    continue
                }

                inputBuffer.clear()
                inputBuffer.put(frame.data, 0, frame.size)
                activeCodec.queueInputBuffer(inputIndex, 0, frame.size, frame.presentationTimeUs, 0)
                if (frame.isKeyFrame) {
                    needsKeyFrame = false
                }
                drainOutput(activeCodec)
            } catch (e: IllegalStateException) {
                Timber.e(e, "Decoder error; resetting")
                resetCodec()
            } finally {
                frame.release()
            }
        }
    }

    private fun drainOutput(activeCodec: MediaCodec) {
        while (true) {
            val outputIndex = activeCodec.dequeueOutputBuffer(bufferInfo, outputTimeoutUs)
            when {
                outputIndex >= 0 -> {
                    activeCodec.releaseOutputBuffer(outputIndex, true)
                    if ((bufferInfo.flags and MediaCodec.BUFFER_FLAG_END_OF_STREAM) != 0) {
                        return
                    }
                }

                outputIndex == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED -> {
                    val format = activeCodec.outputFormat
                    Timber.i("Decoder output format changed: $format")
                }

                outputIndex == MediaCodec.INFO_OUTPUT_BUFFERS_CHANGED -> continue
                outputIndex == MediaCodec.INFO_TRY_AGAIN_LATER -> return
                else -> return
            }
        }
    }

    private fun createCodec(config: VideoDecoderConfig): MediaCodec {
        val format = MediaFormat.createVideoFormat(config.mimeType, config.width, config.height)
        config.maxInputSize?.let { format.setInteger(MediaFormat.KEY_MAX_INPUT_SIZE, it) }
        if (config.operatingRate != null && Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            format.setFloat(MediaFormat.KEY_OPERATING_RATE, config.operatingRate)
        }
        if (config.lowLatency && Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            format.setInteger(MediaFormat.KEY_LOW_LATENCY, 1)
        }

        return MediaCodec.createDecoderByType(config.mimeType).apply {
            configure(format, config.outputSurface, null, 0)
            start()
        }
    }

    private fun resetCodec() {
        synchronized(codecLock) {
            needsKeyFrame = true
            clearQueuedFrames()
            releaseCodec()
            val activeConfig = config ?: return
            codec = runCatching { createCodec(activeConfig) }
                .onFailure { Timber.e(it, "Failed to recreate decoder") }
                .getOrNull()
        }
    }

    private fun reconfigureForFrame(frame: EncodedFrame, activeConfig: VideoDecoderConfig) {
        val updatedConfig =
            activeConfig.copy(
                width = frame.width,
                height = frame.height,
            )
        Timber.i("Decoder reconfigure: ${activeConfig.width}x${activeConfig.height} -> ${frame.width}x${frame.height}")
        config = updatedConfig
        resetCodec()
    }

    private fun clearQueuedFrames() {
        var pending = frameQueue.poll()
        while (pending != null) {
            pending.release()
            pending = frameQueue.poll()
        }
    }

    private fun releaseCodec() {
        codec?.let { activeCodec ->
            runCatching { activeCodec.stop() }
                .onFailure { Timber.w(it, "Decoder stop failed") }
            runCatching { activeCodec.release() }
                .onFailure { Timber.w(it, "Decoder release failed") }
        }
        codec = null
    }
}
