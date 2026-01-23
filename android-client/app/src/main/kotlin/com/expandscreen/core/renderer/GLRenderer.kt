package com.expandscreen.core.renderer

import android.graphics.SurfaceTexture
import android.opengl.GLES11Ext
import android.opengl.GLES20
import android.opengl.GLSurfaceView
import android.opengl.Matrix
import android.view.Surface
import timber.log.Timber
import java.lang.ref.WeakReference
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.nio.FloatBuffer
import javax.microedition.khronos.egl.EGLConfig
import javax.microedition.khronos.opengles.GL10

class GLRenderer(
    private val onDecoderSurfaceReady: ((Surface) -> Unit)? = null,
) : VideoRenderer, SurfaceTexture.OnFrameAvailableListener {

    @Volatile
    private var glViewRef: WeakReference<GLSurfaceView>? = null

    private val frameSyncObject = Any()

    @Volatile
    private var frameAvailable = false

    private var viewWidth = 0
    private var viewHeight = 0

    private var videoWidth = 0
    private var videoHeight = 0

    private var rotationDegrees = 0

    private val mvpMatrix = FloatArray(16)
    private val texMatrix = FloatArray(16)

    private var programId = 0
    private var oesTextureId = 0

    private var aPosition = 0
    private var aTexCoord = 0
    private var uMvpMatrix = 0
    private var uTexMatrix = 0
    private var uTextureSampler = 0

    private val vertexBuffer: FloatBuffer =
        floatBufferOf(
            floatArrayOf(
                -1f,
                -1f,
                1f,
                -1f,
                -1f,
                1f,
                1f,
                1f,
            ),
        )

    private val texCoordBuffer: FloatBuffer =
        floatBufferOf(
            floatArrayOf(
                0f,
                1f,
                1f,
                1f,
                0f,
                0f,
                1f,
                0f,
            ),
        )

    private var surfaceTexture: SurfaceTexture? = null
    private var decoderOutputSurface: Surface? = null

    override fun setVideoSize(width: Int, height: Int) {
        if (width <= 0 || height <= 0) return
        videoWidth = width
        videoHeight = height
        surfaceTexture?.setDefaultBufferSize(width, height)
        updateMvpMatrix()
    }

    override fun setVideoRotationDegrees(degrees: Int) {
        rotationDegrees = ((degrees % 360) + 360) % 360
        updateMvpMatrix()
    }

    fun bindTo(glSurfaceView: GLSurfaceView) {
        glViewRef = WeakReference(glSurfaceView)
        glSurfaceView.setEGLContextClientVersion(2)
        glSurfaceView.preserveEGLContextOnPause = true
        glSurfaceView.setRenderer(this)
        glSurfaceView.renderMode = GLSurfaceView.RENDERMODE_WHEN_DIRTY
    }

    override fun onSurfaceCreated(gl: GL10?, config: EGLConfig?) {
        Matrix.setIdentityM(mvpMatrix, 0)
        Matrix.setIdentityM(texMatrix, 0)

        programId = createProgram(VERTEX_SHADER, FRAGMENT_SHADER)
        if (programId == 0) {
            Timber.e("OpenGL program creation failed")
            return
        }

        aPosition = GLES20.glGetAttribLocation(programId, "aPosition")
        aTexCoord = GLES20.glGetAttribLocation(programId, "aTexCoord")
        uMvpMatrix = GLES20.glGetUniformLocation(programId, "uMvpMatrix")
        uTexMatrix = GLES20.glGetUniformLocation(programId, "uTexMatrix")
        uTextureSampler = GLES20.glGetUniformLocation(programId, "sTexture")

        oesTextureId = createExternalOesTexture()
        surfaceTexture =
            SurfaceTexture(oesTextureId).apply {
                setOnFrameAvailableListener(this@GLRenderer)
                if (videoWidth > 0 && videoHeight > 0) {
                    setDefaultBufferSize(videoWidth, videoHeight)
                }
            }
        decoderOutputSurface = Surface(requireNotNull(surfaceTexture))
        decoderOutputSurface?.let { onDecoderSurfaceReady?.invoke(it) }

        GLES20.glClearColor(0f, 0f, 0f, 1f)
    }

    override fun onSurfaceChanged(gl: GL10?, width: Int, height: Int) {
        viewWidth = width
        viewHeight = height
        GLES20.glViewport(0, 0, width, height)
        updateMvpMatrix()
    }

    override fun onDrawFrame(gl: GL10?) {
        if (programId == 0 || oesTextureId == 0) return

        val st = surfaceTexture
        if (st != null) {
            var shouldUpdate = false
            synchronized(frameSyncObject) {
                if (frameAvailable) {
                    frameAvailable = false
                    shouldUpdate = true
                }
            }
            if (shouldUpdate) {
                runCatching {
                    st.updateTexImage()
                    st.getTransformMatrix(texMatrix)
                }.onFailure {
                    Timber.w(it, "SurfaceTexture update failed")
                }
            }
        }

        GLES20.glClear(GLES20.GL_COLOR_BUFFER_BIT)

        GLES20.glUseProgram(programId)
        GLES20.glActiveTexture(GLES20.GL_TEXTURE0)
        GLES20.glBindTexture(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, oesTextureId)
        GLES20.glUniform1i(uTextureSampler, 0)

        GLES20.glUniformMatrix4fv(uMvpMatrix, 1, false, mvpMatrix, 0)
        GLES20.glUniformMatrix4fv(uTexMatrix, 1, false, texMatrix, 0)

        GLES20.glEnableVertexAttribArray(aPosition)
        GLES20.glVertexAttribPointer(aPosition, 2, GLES20.GL_FLOAT, false, 0, vertexBuffer)

        GLES20.glEnableVertexAttribArray(aTexCoord)
        GLES20.glVertexAttribPointer(aTexCoord, 2, GLES20.GL_FLOAT, false, 0, texCoordBuffer)

        GLES20.glDrawArrays(GLES20.GL_TRIANGLE_STRIP, 0, 4)

        GLES20.glDisableVertexAttribArray(aPosition)
        GLES20.glDisableVertexAttribArray(aTexCoord)
        GLES20.glBindTexture(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, 0)
        GLES20.glUseProgram(0)
    }

    override fun onFrameAvailable(surfaceTexture: SurfaceTexture?) {
        synchronized(frameSyncObject) { frameAvailable = true }
        glViewRef?.get()?.requestRender()
    }

    override val decoderSurface: Surface?
        get() = decoderOutputSurface

    override fun release() {
        runCatching { decoderOutputSurface?.release() }
        decoderOutputSurface = null

        runCatching { surfaceTexture?.setOnFrameAvailableListener(null) }
        runCatching { surfaceTexture?.release() }
        surfaceTexture = null

        if (oesTextureId != 0) {
            val textures = intArrayOf(oesTextureId)
            GLES20.glDeleteTextures(1, textures, 0)
            oesTextureId = 0
        }

        if (programId != 0) {
            GLES20.glDeleteProgram(programId)
            programId = 0
        }
    }

    private fun updateMvpMatrix() {
        val localViewWidth = viewWidth
        val localViewHeight = viewHeight
        if (localViewWidth <= 0 || localViewHeight <= 0) return

        val localVideoWidth = videoWidth
        val localVideoHeight = videoHeight
        if (localVideoWidth <= 0 || localVideoHeight <= 0) {
            Matrix.setIdentityM(mvpMatrix, 0)
            return
        }

        val isQuarterTurn = (rotationDegrees % 180) != 0
        val contentWidth = if (isQuarterTurn) localVideoHeight else localVideoWidth
        val contentHeight = if (isQuarterTurn) localVideoWidth else localVideoHeight

        val viewAspect = localViewWidth.toFloat() / localViewHeight.toFloat()
        val contentAspect = contentWidth.toFloat() / contentHeight.toFloat()

        val scaleX: Float
        val scaleY: Float
        if (contentAspect > viewAspect) {
            scaleX = 1f
            scaleY = viewAspect / contentAspect
        } else {
            scaleX = contentAspect / viewAspect
            scaleY = 1f
        }

        Matrix.setIdentityM(mvpMatrix, 0)
        Matrix.rotateM(mvpMatrix, 0, rotationDegrees.toFloat(), 0f, 0f, 1f)
        Matrix.scaleM(mvpMatrix, 0, scaleX, scaleY, 1f)
    }

    private fun createExternalOesTexture(): Int {
        val textures = IntArray(1)
        GLES20.glGenTextures(1, textures, 0)
        val id = textures[0]
        GLES20.glBindTexture(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, id)
        GLES20.glTexParameteri(
            GLES11Ext.GL_TEXTURE_EXTERNAL_OES,
            GLES20.GL_TEXTURE_MIN_FILTER,
            GLES20.GL_LINEAR,
        )
        GLES20.glTexParameteri(
            GLES11Ext.GL_TEXTURE_EXTERNAL_OES,
            GLES20.GL_TEXTURE_MAG_FILTER,
            GLES20.GL_LINEAR,
        )
        GLES20.glTexParameteri(
            GLES11Ext.GL_TEXTURE_EXTERNAL_OES,
            GLES20.GL_TEXTURE_WRAP_S,
            GLES20.GL_CLAMP_TO_EDGE,
        )
        GLES20.glTexParameteri(
            GLES11Ext.GL_TEXTURE_EXTERNAL_OES,
            GLES20.GL_TEXTURE_WRAP_T,
            GLES20.GL_CLAMP_TO_EDGE,
        )
        GLES20.glBindTexture(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, 0)
        return id
    }

    private fun createProgram(vertexSource: String, fragmentSource: String): Int {
        val vertexShader = loadShader(GLES20.GL_VERTEX_SHADER, vertexSource)
        val fragmentShader = loadShader(GLES20.GL_FRAGMENT_SHADER, fragmentSource)
        if (vertexShader == 0 || fragmentShader == 0) return 0

        val program = GLES20.glCreateProgram()
        GLES20.glAttachShader(program, vertexShader)
        GLES20.glAttachShader(program, fragmentShader)
        GLES20.glLinkProgram(program)

        val linkStatus = IntArray(1)
        GLES20.glGetProgramiv(program, GLES20.GL_LINK_STATUS, linkStatus, 0)
        if (linkStatus[0] != GLES20.GL_TRUE) {
            Timber.e("Program link failed: ${GLES20.glGetProgramInfoLog(program)}")
            GLES20.glDeleteProgram(program)
            return 0
        }

        GLES20.glDeleteShader(vertexShader)
        GLES20.glDeleteShader(fragmentShader)
        return program
    }

    private fun loadShader(type: Int, source: String): Int {
        val shader = GLES20.glCreateShader(type)
        GLES20.glShaderSource(shader, source)
        GLES20.glCompileShader(shader)

        val compileStatus = IntArray(1)
        GLES20.glGetShaderiv(shader, GLES20.GL_COMPILE_STATUS, compileStatus, 0)
        if (compileStatus[0] != GLES20.GL_TRUE) {
            Timber.e("Shader compile failed: ${GLES20.glGetShaderInfoLog(shader)}")
            GLES20.glDeleteShader(shader)
            return 0
        }
        return shader
    }

    private fun floatBufferOf(values: FloatArray): FloatBuffer {
        return ByteBuffer.allocateDirect(values.size * 4)
            .order(ByteOrder.nativeOrder())
            .asFloatBuffer()
            .apply {
                put(values)
                position(0)
            }
    }

    private companion object {
        private const val VERTEX_SHADER =
            """
            uniform mat4 uMvpMatrix;
            uniform mat4 uTexMatrix;
            attribute vec4 aPosition;
            attribute vec4 aTexCoord;
            varying vec2 vTexCoord;
            void main() {
                gl_Position = uMvpMatrix * aPosition;
                vTexCoord = (uTexMatrix * aTexCoord).xy;
            }
            """

        private const val FRAGMENT_SHADER =
            """
            #extension GL_OES_EGL_image_external : require
            precision mediump float;
            varying vec2 vTexCoord;
            uniform samplerExternalOES sTexture;
            void main() {
                gl_FragColor = texture2D(sTexture, vTexCoord);
            }
            """
    }
}
