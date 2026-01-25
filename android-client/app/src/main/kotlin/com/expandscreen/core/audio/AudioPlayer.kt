package com.expandscreen.core.audio

import android.content.Context
import android.media.AudioAttributes
import android.media.AudioFocusRequest
import android.media.AudioFormat
import android.media.AudioManager
import android.media.AudioTrack
import java.util.concurrent.ArrayBlockingQueue
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicLong
import timber.log.Timber

class AudioPlayer(
    context: Context,
    private val frameQueueCapacity: Int = 48,
) {

    private val audioManager = context.getSystemService(AudioManager::class.java)
    private val frameQueue = ArrayBlockingQueue<PcmAudioFrame>(frameQueueCapacity)
    private val running = AtomicBoolean(false)

    private val droppedFrames = AtomicLong(0)

    @Volatile
    private var currentFormat: PcmAudioFormat? = null

    @Volatile
    private var audioTrack: AudioTrack? = null

    @Volatile
    private var volume: Float = 1.0f

    @Volatile
    private var focusRequest: AudioFocusRequest? = null

    private var playerThread: Thread? = null

    @Volatile
    private var basePtsUs: Long? = null

    @Volatile
    private var baseTimeNs: Long? = null

    fun start() {
        if (running.get()) return
        running.set(true)
        playerThread =
            Thread({ playLoop() }, "AudioPlayerThread").apply {
                isDaemon = true
                start()
            }
    }

    fun enqueue(frame: PcmAudioFrame): Boolean {
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

    fun setVolume(value: Float) {
        volume = value.coerceIn(0f, 1f)
        val track = audioTrack ?: return
        runCatching { track.setVolume(volume) }
            .onFailure { Timber.w(it, "AudioTrack setVolume failed") }
    }

    fun flush() {
        clearQueuedFrames()
        val track = audioTrack ?: return
        runCatching { track.flush() }
            .onFailure { Timber.w(it, "AudioTrack flush failed") }
    }

    fun release() {
        stopLoop()
        clearQueuedFrames()
        abandonAudioFocus()
        releaseTrack()
        basePtsUs = null
        baseTimeNs = null
    }

    fun snapshotStats(): AudioPlayerStats {
        return AudioPlayerStats(
            queuedFrames = frameQueue.size,
            droppedFrames = droppedFrames.get(),
        )
    }

    private fun stopLoop() {
        if (!running.get()) return
        running.set(false)
        playerThread?.interrupt()
        if (playerThread != Thread.currentThread()) {
            runCatching { playerThread?.join(1_000) }
                .onFailure { Timber.w(it, "Audio player thread join failed") }
        }
        playerThread = null
    }

    private fun playLoop() {
        requestAudioFocus()

        while (running.get()) {
            val frame =
                try {
                    frameQueue.poll(20, TimeUnit.MILLISECONDS)
                } catch (e: InterruptedException) {
                    return
                } ?: continue

            try {
                ensureTrack(frame.format)
                applySyncDelay(frame.presentationTimeUs)
                val track = audioTrack ?: continue
                if (track.playState != AudioTrack.PLAYSTATE_PLAYING) {
                    runCatching { track.play() }
                        .onFailure { Timber.w(it, "AudioTrack play failed") }
                }
                val written = track.write(frame.data, 0, frame.size, AudioTrack.WRITE_BLOCKING)
                if (written < 0) {
                    Timber.w("AudioTrack write failed: $written")
                }
            } catch (e: IllegalStateException) {
                Timber.w(e, "Audio playback error; resetting track")
                releaseTrack()
                basePtsUs = null
                baseTimeNs = null
            } finally {
                frame.release()
            }
        }
    }

    private fun ensureTrack(format: PcmAudioFormat) {
        val existingFormat = currentFormat
        if (audioTrack != null && existingFormat != null && existingFormat == format) return

        releaseTrack()
        currentFormat = format

        val channelMask =
            when (format.channelCount) {
                1 -> AudioFormat.CHANNEL_OUT_MONO
                2 -> AudioFormat.CHANNEL_OUT_STEREO
                else -> AudioFormat.CHANNEL_OUT_DEFAULT
            }

        val audioFormat =
            AudioFormat.Builder()
                .setSampleRate(format.sampleRate)
                .setEncoding(format.pcmEncoding)
                .setChannelMask(channelMask)
                .build()

        val attributes =
            AudioAttributes.Builder()
                .setUsage(AudioAttributes.USAGE_MEDIA)
                .setContentType(AudioAttributes.CONTENT_TYPE_MUSIC)
                .build()

        val minBufferSize = AudioTrack.getMinBufferSize(format.sampleRate, channelMask, format.pcmEncoding)
        val bufferSizeInBytes = (minBufferSize * 2).coerceAtLeast(minBufferSize)
        audioTrack =
            AudioTrack.Builder()
                .setAudioAttributes(attributes)
                .setAudioFormat(audioFormat)
                .setBufferSizeInBytes(bufferSizeInBytes)
                .setTransferMode(AudioTrack.MODE_STREAM)
                .build()
                .also { created ->
                    runCatching { created.setVolume(volume) }
                        .onFailure { Timber.w(it, "AudioTrack setVolume failed") }
                }
    }

    private fun applySyncDelay(presentationTimeUs: Long) {
        if (presentationTimeUs <= 0) return
        val basePts = basePtsUs
        val baseTime = baseTimeNs
        if (basePts == null || baseTime == null) {
            basePtsUs = presentationTimeUs
            baseTimeNs = System.nanoTime()
            return
        }

        val nowNs = System.nanoTime()
        val desiredNs = baseTime + ((presentationTimeUs - basePts).coerceAtLeast(0) * 1_000)
        val deltaNs = desiredNs - nowNs
        if (deltaNs <= 0) return

        val sleepNs = deltaNs.coerceAtMost(50_000_000L)
        sleepNanos(sleepNs)
    }

    private fun sleepNanos(nanos: Long) {
        if (nanos <= 0) return
        val sleepMs = nanos / 1_000_000L
        val sleepNsRemainder = (nanos % 1_000_000L).toInt()
        runCatching { Thread.sleep(sleepMs, sleepNsRemainder) }
            .onFailure { }
    }

    private fun requestAudioFocus() {
        val manager = audioManager ?: return
        if (focusRequest != null) return

        val attributes =
            AudioAttributes.Builder()
                .setUsage(AudioAttributes.USAGE_MEDIA)
                .setContentType(AudioAttributes.CONTENT_TYPE_MUSIC)
                .build()

        val request =
            AudioFocusRequest.Builder(AudioManager.AUDIOFOCUS_GAIN)
                .setAudioAttributes(attributes)
                .setOnAudioFocusChangeListener { change ->
                    if (change == AudioManager.AUDIOFOCUS_LOSS) {
                        flush()
                    }
                }
                .build()

        val result = manager.requestAudioFocus(request)
        if (result == AudioManager.AUDIOFOCUS_REQUEST_GRANTED) {
            focusRequest = request
        }
    }

    private fun abandonAudioFocus() {
        val manager = audioManager ?: return
        val request = focusRequest ?: return
        focusRequest = null
        runCatching { manager.abandonAudioFocusRequest(request) }
            .onFailure { }
    }

    private fun clearQueuedFrames() {
        var pending = frameQueue.poll()
        while (pending != null) {
            pending.release()
            pending = frameQueue.poll()
        }
    }

    private fun releaseTrack() {
        val track = audioTrack ?: return
        audioTrack = null
        currentFormat = null
        runCatching { track.pause() }
        runCatching { track.flush() }
        runCatching { track.release() }
    }
}

data class AudioPlayerStats(
    val queuedFrames: Int,
    val droppedFrames: Long,
)

