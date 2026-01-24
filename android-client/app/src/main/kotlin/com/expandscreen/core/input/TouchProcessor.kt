package com.expandscreen.core.input

import android.os.SystemClock
import android.view.MotionEvent
import com.expandscreen.protocol.TouchEventMessage
import kotlin.math.abs
import kotlin.math.max
import kotlin.math.min

/**
 * TouchProcessor (AND-102)
 *
 * Converts Android MotionEvent into protocol TouchEventMessage list:
 * - Multi-touch: emits per-pointer messages (Down/Move/Up)
 * - Coordinates: mapped into the video content area then scaled into the given screen space
 * - Batching: returns a list so callers can send in one network flush
 */
class TouchProcessor(
    private val screenWidthPxProvider: () -> Int,
    private val screenHeightPxProvider: () -> Int,
    private val viewWidthPxProvider: () -> Int,
    private val viewHeightPxProvider: () -> Int,
    private val videoWidthPxProvider: () -> Int,
    private val videoHeightPxProvider: () -> Int,
    private val rotationDegreesProvider: () -> Int = { 0 },
    private val minMoveIntervalMsProvider: () -> Long = { 8L },
) {
    private var lastMoveSentAtMs: Long = 0L

    fun process(event: MotionEvent): List<TouchEventMessage> {
        val actionMasked = event.actionMasked

        val touchAction =
            when (actionMasked) {
                MotionEvent.ACTION_DOWN,
                MotionEvent.ACTION_POINTER_DOWN,
                -> TouchAction.Down
                MotionEvent.ACTION_MOVE -> TouchAction.Move
                MotionEvent.ACTION_UP,
                MotionEvent.ACTION_POINTER_UP,
                MotionEvent.ACTION_CANCEL,
                -> TouchAction.Up
                else -> return emptyList()
            }

        if (touchAction == TouchAction.Move) {
            val now = SystemClock.uptimeMillis()
            val minMoveIntervalMs = minMoveIntervalMsProvider().coerceAtLeast(0L)
            if (now - lastMoveSentAtMs < minMoveIntervalMs) {
                return emptyList()
            }
            lastMoveSentAtMs = now
        }

        val pointerIndexes: IntArray =
            when (actionMasked) {
                MotionEvent.ACTION_DOWN,
                MotionEvent.ACTION_UP,
                MotionEvent.ACTION_POINTER_DOWN,
                MotionEvent.ACTION_POINTER_UP,
                -> intArrayOf(event.actionIndex)
                MotionEvent.ACTION_CANCEL,
                MotionEvent.ACTION_MOVE,
                -> IntArray(event.pointerCount) { it }
                else -> return emptyList()
            }

        val result = ArrayList<TouchEventMessage>(pointerIndexes.size)
        for (pointerIndex in pointerIndexes) {
            val pointerId = event.getPointerId(pointerIndex)
            val xView = event.getX(pointerIndex)
            val yView = event.getY(pointerIndex)

            val mapped = mapViewToScreenPx(xView, yView) ?: continue

            result +=
                TouchEventMessage(
                    action = touchAction.protocolValue,
                    pointerId = pointerId,
                    x = mapped.first,
                    y = mapped.second,
                    pressure = event.getPressure(pointerIndex).coerceIn(0f, 1f),
                )
        }

        return result
    }

    private fun mapViewToScreenPx(xViewPx: Float, yViewPx: Float): Pair<Float, Float>? {
        val viewWidth = viewWidthPxProvider().coerceAtLeast(1)
        val viewHeight = viewHeightPxProvider().coerceAtLeast(1)

        val screenWidth = screenWidthPxProvider().coerceAtLeast(1)
        val screenHeight = screenHeightPxProvider().coerceAtLeast(1)

        val videoWidth = videoWidthPxProvider()
        val videoHeight = videoHeightPxProvider()
        val rotation = normalizeRotation(rotationDegreesProvider())

        val normalizedInContent =
            if (videoWidth > 0 && videoHeight > 0) {
                mapViewToContentNormalized(
                    xViewPx = xViewPx,
                    yViewPx = yViewPx,
                    viewWidthPx = viewWidth,
                    viewHeightPx = viewHeight,
                    videoWidthPx = videoWidth,
                    videoHeightPx = videoHeight,
                    rotationDegrees = rotation,
                )
            } else {
                // Fallback: no video dimensions yet, treat full view as content.
                val nx = (xViewPx / viewWidth.toFloat()).coerceIn(0f, 1f)
                val ny = (yViewPx / viewHeight.toFloat()).coerceIn(0f, 1f)
                Pair(nx, ny)
            }

        val (nx, ny) = normalizedInContent ?: return null

        val x = nx * max(1, screenWidth - 1).toFloat()
        val y = ny * max(1, screenHeight - 1).toFloat()
        return Pair(x, y)
    }

    private fun mapViewToContentNormalized(
        xViewPx: Float,
        yViewPx: Float,
        viewWidthPx: Int,
        viewHeightPx: Int,
        videoWidthPx: Int,
        videoHeightPx: Int,
        rotationDegrees: Int,
    ): Pair<Float, Float>? {
        val isQuarterTurn = (rotationDegrees % 180) != 0
        val contentWidth = if (isQuarterTurn) videoHeightPx else videoWidthPx
        val contentHeight = if (isQuarterTurn) videoWidthPx else videoHeightPx

        val viewAspect = viewWidthPx.toFloat() / viewHeightPx.toFloat()
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

        // View px -> NDC (OpenGL-style, origin at center, Y up)
        val xNdc = (xViewPx / viewWidthPx.toFloat()) * 2f - 1f
        val yNdc = 1f - (yViewPx / viewHeightPx.toFloat()) * 2f

        // Inverse of GLRenderer: v' = rotate(scale(v))
        val (xRotInv, yRotInv) = applyInverseRotation(xNdc, yNdc, rotationDegrees)
        val x = xRotInv / max(1e-6f, scaleX)
        val y = yRotInv / max(1e-6f, scaleY)

        // If outside content (letterbox bars), ignore.
        if (abs(x) > 1f || abs(y) > 1f) return null

        val nx = ((x + 1f) / 2f).coerceIn(0f, 1f)
        val ny = ((1f - y) / 2f).coerceIn(0f, 1f) // top-left origin
        return Pair(nx, ny)
    }

    private fun applyInverseRotation(x: Float, y: Float, rotationDegrees: Int): Pair<Float, Float> {
        return when (rotationDegrees) {
            0 -> Pair(x, y)
            90 -> Pair(y, -x)
            180 -> Pair(-x, -y)
            270 -> Pair(-y, x)
            else -> Pair(x, y)
        }
    }

    private fun normalizeRotation(degrees: Int): Int {
        val normalized = ((degrees % 360) + 360) % 360
        return when (normalized) {
            0, 90, 180, 270 -> normalized
            else -> {
                // Only right-angle rotations are expected in this pipeline; snap to nearest.
                val snapped = (normalized / 90f).let { (it + 0.5f).toInt() * 90 }
                min(270, max(0, snapped))
            }
        }
    }

    private enum class TouchAction(val protocolValue: Int) {
        Down(0),
        Move(1),
        Up(2),
    }
}
