package com.expandscreen.core.renderer

import android.opengl.GLSurfaceView
import android.view.Surface

/**
 * OpenGL Renderer Interface
 *
 * Handles rendering decoded video frames using OpenGL ES.
 */
interface VideoRenderer : GLSurfaceView.Renderer {
    val decoderSurface: Surface?
    fun setVideoSize(width: Int, height: Int)
    fun setVideoRotationDegrees(degrees: Int)
    fun release()
}

typealias GLVideoRenderer = GLRenderer
