package com.expandscreen.core.audio

import android.media.AudioFormat
import android.media.MediaCodec
import android.media.MediaFormat
import android.os.Build
import java.nio.ByteBuffer
import java.util.concurrent.ArrayBlockingQueue
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicLong
import timber.log.Timber

interface AudioDecoder {
    fun initialize(config: AudioDecoderConfig)
    fun enqueueFrame(frame: EncodedAudioFrame): Boolean
    fun flush()
    fun release()
}

class MediaCodecAudioDecoder(
    private val frameQueueCapacity: Int = 24,
    private val inputTimeoutUs: Long = 5_000,
    private val outputTimeoutUs: Long = 0,
    private val onPcmFrame: (PcmAudioFrame) -> Unit,
) : AudioDecoder {

    private val frameQueue = ArrayBlockingQueue<EncodedAudioFrame>(frameQueueCapacity)
    private val bufferInfo = MediaCodec.BufferInfo()
    private val running = AtomicBoolean(false)
    private val codecLock = Any()

    private val droppedFrames = AtomicLong(0)
    private val codecResets = AtomicLong(0)

    @Volatile
    private var lastError: String? = null

    @Volatile
    private var codec: MediaCodec? = null

    @Volatile
    private var config: AudioDecoderConfig? = null

    @Volatile
    private var currentPcmFormat: PcmAudioFormat? = null

    private var decodeThread: Thread? = null

    override fun initialize(config: AudioDecoderConfig) {
        stopDecodeThread()
        synchronized(codecLock) {
            releaseCodec()
            this.config = config
            this.lastError = null
            droppedFrames.set(0)
            codecResets.set(0)
            this.codec = createCodec(config)
        }
        startDecodeThread()
    }

    override fun enqueueFrame(frame: EncodedAudioFrame): Boolean {
        if (!running.get()) {
            droppedFrames.incrementAndGet()
            frame.release()
            return false
        }

        if (frameQueue.offer(frame)) {
            return true
        }

        val dropped = frameQueue.poll()
        dropped?.release()
        droppedFrames.incrementAndGet()
        val accepted = frameQueue.offer(frame)
        if (!accepted) {
            droppedFrames.incrementAndGet()
            frame.release()
        }
        return accepted
    }

    override fun flush() {
        synchronized(codecLock) {
            clearQueuedFrames()
            runCatching { codec?.flush() }
                .onFailure { Timber.w(it, "Audio decoder flush failed") }
        }
    }

    override fun release() {
        stopDecodeThread()
        synchronized(codecLock) {
            clearQueuedFrames()
            releaseCodec()
            config = null
            currentPcmFormat = null
        }
    }

    fun snapshotStats(): AudioDecoderStats {
        return AudioDecoderStats(
            queuedFrames = frameQueue.size,
            droppedFrames = droppedFrames.get(),
            codecResets = codecResets.get(),
            lastError = lastError,
        )
    }

    private fun startDecodeThread() {
        if (running.get()) return
        running.set(true)
        decodeThread =
            Thread({ decodeLoop() }, "AudioDecodeThread").apply {
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
                .onFailure { Timber.w(it, "Audio decode thread join failed") }
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

                if (frame.decoderConfig != activeConfig) {
                    synchronized(codecLock) {
                        if (config != frame.decoderConfig) {
                            reconfigure(frame.decoderConfig)
                        }
                    }
                }

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
                drainOutput(activeCodec)
            } catch (e: IllegalStateException) {
                Timber.e(e, "Audio decoder error; resetting")
                lastError = e.message ?: e.javaClass.simpleName
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
                    val outputBuffer = activeCodec.getOutputBuffer(outputIndex)
                    if (outputBuffer != null && bufferInfo.size > 0) {
                        val pcmFormat =
                            currentPcmFormat
                                ?: PcmAudioFormat(
                                    sampleRate = config?.sampleRate ?: 48_000,
                                    channelCount = config?.channelCount ?: 2,
                                    pcmEncoding = AudioFormat.ENCODING_PCM_16BIT,
                                )
                        val frameBytes = copyOutputBuffer(outputBuffer, bufferInfo.size)
                        onPcmFrame(
                            PcmAudioFrame(
                                data = frameBytes,
                                size = frameBytes.size,
                                presentationTimeUs = bufferInfo.presentationTimeUs,
                                format = pcmFormat,
                            ),
                        )
                    }
                    activeCodec.releaseOutputBuffer(outputIndex, false)
                    if ((bufferInfo.flags and MediaCodec.BUFFER_FLAG_END_OF_STREAM) != 0) {
                        return
                    }
                }

                outputIndex == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED -> {
                    val format = activeCodec.outputFormat
                    currentPcmFormat = parsePcmFormat(format)
                    Timber.i("Audio decoder output format changed: $format")
                }

                outputIndex == MediaCodec.INFO_OUTPUT_BUFFERS_CHANGED -> continue
                outputIndex == MediaCodec.INFO_TRY_AGAIN_LATER -> return
                else -> return
            }
        }
    }

    private fun copyOutputBuffer(buffer: ByteBuffer, size: Int): ByteArray {
        val bytes = ByteArray(size)
        val oldPos = buffer.position()
        val oldLimit = buffer.limit()
        buffer.position(bufferInfo.offset)
        buffer.limit(bufferInfo.offset + size)
        buffer.get(bytes)
        buffer.position(oldPos)
        buffer.limit(oldLimit)
        return bytes
    }

    private fun parsePcmFormat(format: MediaFormat): PcmAudioFormat {
        val sampleRate =
            runCatching { format.getInteger(MediaFormat.KEY_SAMPLE_RATE) }
                .getOrNull()
                ?: (config?.sampleRate ?: 48_000)
        val channelCount =
            runCatching { format.getInteger(MediaFormat.KEY_CHANNEL_COUNT) }
                .getOrNull()
                ?: (config?.channelCount ?: 2)
        val pcmEncoding =
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.N) {
                runCatching { format.getInteger(MediaFormat.KEY_PCM_ENCODING) }
                    .getOrNull()
                    ?: AudioFormat.ENCODING_PCM_16BIT
            } else {
                AudioFormat.ENCODING_PCM_16BIT
            }
        return PcmAudioFormat(sampleRate = sampleRate, channelCount = channelCount, pcmEncoding = pcmEncoding)
    }

    private fun createCodec(config: AudioDecoderConfig): MediaCodec {
        val format = MediaFormat.createAudioFormat(config.mimeType, config.sampleRate, config.channelCount)
        config.maxInputSize?.let { format.setInteger(MediaFormat.KEY_MAX_INPUT_SIZE, it) }
        if (config.codecConfig0.isNotEmpty()) {
            format.setByteBuffer("csd-0", ByteBuffer.wrap(config.codecConfig0))
        }
        if (config.codecConfig1.isNotEmpty()) {
            format.setByteBuffer("csd-1", ByteBuffer.wrap(config.codecConfig1))
        }
        if (config.lowLatency && Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            format.setInteger(MediaFormat.KEY_LOW_LATENCY, 1)
        }

        return MediaCodec.createDecoderByType(config.mimeType).apply {
            configure(format, null, null, 0)
            start()
        }
    }

    private fun reconfigure(next: AudioDecoderConfig) {
        releaseCodec()
        this.config = next
        this.codec = runCatching { createCodec(next) }
            .onFailure { Timber.e(it, "Failed to reconfigure audio decoder") }
            .getOrNull()
    }

    private fun resetCodec() {
        synchronized(codecLock) {
            codecResets.incrementAndGet()
            clearQueuedFrames()
            releaseCodec()
            val activeConfig = config ?: return
            codec = runCatching { createCodec(activeConfig) }
                .onFailure { Timber.e(it, "Failed to recreate audio decoder") }
                .getOrNull()
        }
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
                .onFailure { Timber.w(it, "Audio decoder stop failed") }
            runCatching { activeCodec.release() }
                .onFailure { Timber.w(it, "Audio decoder release failed") }
        }
        codec = null
    }
}

data class AudioDecoderStats(
    val queuedFrames: Int,
    val droppedFrames: Long,
    val codecResets: Long,
    val lastError: String?,
)

