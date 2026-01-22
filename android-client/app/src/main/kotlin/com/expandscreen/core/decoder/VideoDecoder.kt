package com.expandscreen.core.decoder

import timber.log.Timber

/**
 * Video Decoder Interface
 *
 * Defines the contract for video decoding implementations.
 * Uses Android MediaCodec for H.264 decoding with low latency.
 */
interface VideoDecoder {
    fun initialize()
    fun decode(data: ByteArray): Boolean
    fun release()
}

/**
 * H.264 Video Decoder Implementation using MediaCodec
 */
class H264Decoder : VideoDecoder {

    override fun initialize() {
        Timber.d("H264Decoder initialized")
        // TODO: Initialize MediaCodec
        // - Configure for H.264 (video/avc)
        // - Set low-latency mode
        // - Configure output surface
    }

    override fun decode(data: ByteArray): Boolean {
        // TODO: Feed encoded data to MediaCodec
        // - Queue input buffer
        // - Dequeue output buffer
        // - Render to surface
        return true
    }

    override fun release() {
        Timber.d("H264Decoder released")
        // TODO: Release MediaCodec resources
    }
}
