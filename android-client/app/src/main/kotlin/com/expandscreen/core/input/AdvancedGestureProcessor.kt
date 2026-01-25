package com.expandscreen.core.input

import android.os.SystemClock
import android.view.MotionEvent
import com.expandscreen.data.repository.GestureMappedAction
import com.expandscreen.data.repository.GestureSettings
import kotlin.math.abs
import kotlin.math.hypot
import kotlin.math.atan2

class AdvancedGestureProcessor(
    private val settingsProvider: () -> GestureSettings,
    private val viewWidthPxProvider: () -> Int,
    private val viewHeightPxProvider: () -> Int,
    private val densityProvider: () -> Float,
) {
    data class Result(
        val consume: Boolean,
        val cancelRemote: Boolean,
        val action: GestureMappedAction? = null,
        val feedback: Feedback? = null,
    )

    enum class Feedback {
        Zoom,
        Rotate,
    }

    private var captured = false
    private var sentCancelForCapture = false

    private var twoFingerDownAtMs: Long? = null
    private var twoFingerStart: TwoFingerStart? = null
    private var transformStart: TransformStart? = null
    private var transformZoomFeedbackSent: Boolean = false
    private var transformRotateFeedbackSent: Boolean = false

    private var threeFingerStart: ThreeFingerStart? = null

    private var edgeSwipeStart: EdgeSwipeStart? = null

    fun onMotionEvent(event: MotionEvent): Result {
        val settings = settingsProvider()
        val actionMasked = event.actionMasked

        if (!settings.enabled) {
            if (remainingPointersAfter(event) == 0) resetAll()
            return Result(consume = false, cancelRemote = false, action = null)
        }

        if (captured) {
            val shouldReset = remainingPointersAfter(event) == 0
            if (!sentCancelForCapture) {
                sentCancelForCapture = true
                if (shouldReset) resetAll()
                return Result(consume = true, cancelRemote = true, action = null)
            }

            if (shouldReset) resetAll()
            return Result(consume = true, cancelRemote = false, action = null)
        }

        when (actionMasked) {
            MotionEvent.ACTION_DOWN -> {
                twoFingerDownAtMs = null
                twoFingerStart = null
                threeFingerStart = null
                edgeSwipeStart = startEdgeSwipeIfEligible(event)
            }

            MotionEvent.ACTION_POINTER_DOWN -> {
                edgeSwipeStart = null

                if (event.pointerCount == 2) {
                    startTwoFingerLongPress(event)
                    startTransform(event)
                } else {
                    twoFingerDownAtMs = null
                    twoFingerStart = null
                    transformStart = null
                    transformZoomFeedbackSent = false
                    transformRotateFeedbackSent = false
                }

                if (event.pointerCount == 3) {
                    startThreeFingerSwipe(event)
                } else {
                    threeFingerStart = null
                }
            }

            MotionEvent.ACTION_MOVE -> {
                if (event.pointerCount == 2) {
                    val transformFeedback = checkTransform(event, settings)
                    if (transformFeedback != null) {
                        return Result(consume = false, cancelRemote = false, action = null, feedback = transformFeedback)
                    }

                    val triggered = checkTwoFingerLongPress(event, settings)
                    if (triggered != null) return capture(triggered)
                } else {
                    twoFingerDownAtMs = null
                    twoFingerStart = null
                    transformStart = null
                    transformZoomFeedbackSent = false
                    transformRotateFeedbackSent = false
                }

                if (event.pointerCount == 3) {
                    val triggered = checkThreeFingerSwipeDown(event, settings)
                    if (triggered != null) return capture(triggered)
                } else {
                    threeFingerStart = null
                }

                if (event.pointerCount == 1) {
                    val triggered = checkEdgeSwipe(event, settings)
                    if (triggered != null) return capture(triggered)
                } else {
                    edgeSwipeStart = null
                }
            }

            MotionEvent.ACTION_POINTER_UP,
            MotionEvent.ACTION_UP,
            MotionEvent.ACTION_CANCEL,
            -> {
                if (remainingPointersAfter(event) == 0) {
                    resetAll()
                } else {
                    if (event.pointerCount - 1 < 2) {
                        twoFingerDownAtMs = null
                        twoFingerStart = null
                        transformStart = null
                        transformZoomFeedbackSent = false
                        transformRotateFeedbackSent = false
                    }
                    if (event.pointerCount - 1 < 3) {
                        threeFingerStart = null
                    }
                    if (event.pointerCount - 1 < 1) {
                        edgeSwipeStart = null
                    }
                }
            }
        }

        return Result(consume = false, cancelRemote = false, action = null)
    }

    private fun capture(action: GestureMappedAction): Result {
        if (action == GestureMappedAction.None) {
            return Result(consume = false, cancelRemote = false, action = null)
        }
        captured = true
        sentCancelForCapture = false
        return Result(consume = true, cancelRemote = true, action = action)
    }

    private fun startTwoFingerLongPress(event: MotionEvent) {
        twoFingerDownAtMs = SystemClock.uptimeMillis()
        twoFingerStart =
            TwoFingerStart(
                x0 = event.getX(0),
                y0 = event.getY(0),
                x1 = event.getX(1),
                y1 = event.getY(1),
                distance = distance(event),
            )
    }

    private fun startTransform(event: MotionEvent) {
        transformStart =
            TransformStart(
                distance = distance(event).coerceAtLeast(1e-3f),
                angleRad = angle(event),
            )
        transformZoomFeedbackSent = false
        transformRotateFeedbackSent = false
    }

    private fun checkTransform(event: MotionEvent, settings: GestureSettings): Feedback? {
        val start = transformStart ?: return null

        val scale = (distance(event) / start.distance).coerceIn(0.01f, 100f)
        val scaleDelta = abs(scale - 1f)
        val rotationDeg = abs(angleDeltaDegrees(angle(event), start.angleRad))

        if (!transformZoomFeedbackSent && scaleDelta >= scaleThreshold(settings)) {
            transformZoomFeedbackSent = true
            return Feedback.Zoom
        }
        if (!transformRotateFeedbackSent && rotationDeg >= rotationThresholdDeg(settings)) {
            transformRotateFeedbackSent = true
            return Feedback.Rotate
        }
        return null
    }

    private fun checkTwoFingerLongPress(event: MotionEvent, settings: GestureSettings): GestureMappedAction? {
        val startedAt = twoFingerDownAtMs ?: return null
        val start = twoFingerStart ?: return null

        val now = SystemClock.uptimeMillis()
        if (now - startedAt < longPressTimeoutMs(settings)) return null

        val slop = moveSlopPx(settings)
        val moved0 = hypot(event.getX(0) - start.x0, event.getY(0) - start.y0)
        val moved1 = hypot(event.getX(1) - start.x1, event.getY(1) - start.y1)
        val distanceDelta = abs(distance(event) - start.distance)

        val isStable = moved0 <= slop && moved1 <= slop && distanceDelta <= slop
        if (!isStable) {
            twoFingerDownAtMs = null
            twoFingerStart = null
            return null
        }

        twoFingerDownAtMs = null
        twoFingerStart = null
        return settings.twoFingerLongPress
    }

    private fun startThreeFingerSwipe(event: MotionEvent) {
        threeFingerStart =
            ThreeFingerStart(
                startedAtMs = SystemClock.uptimeMillis(),
                xAvg = (event.getX(0) + event.getX(1) + event.getX(2)) / 3f,
                yAvg = (event.getY(0) + event.getY(1) + event.getY(2)) / 3f,
            )
    }

    private fun checkThreeFingerSwipeDown(event: MotionEvent, settings: GestureSettings): GestureMappedAction? {
        val start = threeFingerStart ?: return null
        val now = SystemClock.uptimeMillis()
        if (now - start.startedAtMs > 900L) {
            threeFingerStart = null
            return null
        }

        val xAvg = (event.getX(0) + event.getX(1) + event.getX(2)) / 3f
        val yAvg = (event.getY(0) + event.getY(1) + event.getY(2)) / 3f

        val dx = xAvg - start.xAvg
        val dy = yAvg - start.yAvg
        if (dy <= 0f) return null

        val threshold = swipeThresholdPx(settings)
        if (dy < threshold) return null

        if (abs(dx) > dy * 0.7f) return null

        threeFingerStart = null
        return settings.threeFingerSwipeDown
    }

    private fun startEdgeSwipeIfEligible(event: MotionEvent): EdgeSwipeStart? {
        val viewWidth = viewWidthPxProvider().coerceAtLeast(0)
        val viewHeight = viewHeightPxProvider().coerceAtLeast(0)
        if (viewWidth <= 0 || viewHeight <= 0) return null

        val density = densityProvider().coerceAtLeast(0.5f)
        val edgePx = 24f * density

        val x = event.getX(0)
        val y = event.getY(0)
        val side =
            when {
                x <= edgePx -> EdgeSide.Left
                x >= viewWidth - edgePx -> EdgeSide.Right
                y <= edgePx -> EdgeSide.Top
                y >= viewHeight - edgePx -> EdgeSide.Bottom
                else -> null
            } ?: return null

        return EdgeSwipeStart(side = side, x0 = x, y0 = y)
    }

    private fun checkEdgeSwipe(event: MotionEvent, settings: GestureSettings): GestureMappedAction? {
        val start = edgeSwipeStart ?: return null
        val threshold = edgeSwipeThresholdPx(settings)

        val x = event.getX(0)
        val y = event.getY(0)
        val dx = x - start.x0
        val dy = y - start.y0

        val (primary, orth, towardInside) =
            when (start.side) {
                EdgeSide.Left -> Triple(dx, dy, dx >= 0f)
                EdgeSide.Right -> Triple(-dx, dy, dx <= 0f)
                EdgeSide.Top -> Triple(dy, dx, dy >= 0f)
                EdgeSide.Bottom -> Triple(-dy, dx, dy <= 0f)
            }

        if (!towardInside) return null
        if (primary < threshold) return null
        if (abs(orth) > primary * 0.85f) return null

        edgeSwipeStart = null
        return settings.edgeSwipe
    }

    private fun swipeThresholdPx(settings: GestureSettings): Float {
        val density = densityProvider().coerceAtLeast(0.5f)
        return 72f * density * thresholdScale(settings)
    }

    private fun edgeSwipeThresholdPx(settings: GestureSettings): Float {
        val density = densityProvider().coerceAtLeast(0.5f)
        return 64f * density * thresholdScale(settings)
    }

    private fun moveSlopPx(settings: GestureSettings): Float {
        val density = densityProvider().coerceAtLeast(0.5f)
        return 10f * density * thresholdScale(settings)
    }

    private fun scaleThreshold(settings: GestureSettings): Float {
        return 0.09f * thresholdScale(settings)
    }

    private fun rotationThresholdDeg(settings: GestureSettings): Float {
        return 14f * thresholdScale(settings)
    }

    private fun longPressTimeoutMs(settings: GestureSettings): Long {
        return (560 - (settings.sensitivity.coerceIn(0, 100) * 2)).toLong().coerceIn(320L, 560L)
    }

    private fun thresholdScale(settings: GestureSettings): Float {
        val s = settings.sensitivity.coerceIn(0, 100) / 100f
        return 1.25f - (0.5f * s)
    }

    private fun distance(event: MotionEvent): Float {
        val dx = event.getX(1) - event.getX(0)
        val dy = event.getY(1) - event.getY(0)
        return hypot(dx, dy)
    }

    private fun angle(event: MotionEvent): Float {
        val dx = event.getX(1) - event.getX(0)
        val dy = event.getY(1) - event.getY(0)
        return atan2(dy, dx)
    }

    private fun angleDeltaDegrees(currentRad: Float, startRad: Float): Float {
        var delta = currentRad - startRad
        while (delta > Math.PI) delta -= (2.0 * Math.PI).toFloat()
        while (delta < -Math.PI) delta += (2.0 * Math.PI).toFloat()
        return (delta * (180.0 / Math.PI)).toFloat()
    }

    private fun remainingPointersAfter(event: MotionEvent): Int {
        return when (event.actionMasked) {
            MotionEvent.ACTION_UP,
            MotionEvent.ACTION_CANCEL,
            -> 0
            MotionEvent.ACTION_POINTER_UP -> (event.pointerCount - 1).coerceAtLeast(0)
            else -> event.pointerCount
        }
    }

    private fun resetAll() {
        captured = false
        sentCancelForCapture = false

        twoFingerDownAtMs = null
        twoFingerStart = null
        transformStart = null
        transformZoomFeedbackSent = false
        transformRotateFeedbackSent = false
        threeFingerStart = null
        edgeSwipeStart = null
    }

    private data class TwoFingerStart(
        val x0: Float,
        val y0: Float,
        val x1: Float,
        val y1: Float,
        val distance: Float,
    )

    private data class TransformStart(
        val distance: Float,
        val angleRad: Float,
    )

    private data class ThreeFingerStart(
        val startedAtMs: Long,
        val xAvg: Float,
        val yAvg: Float,
    )

    private data class EdgeSwipeStart(
        val side: EdgeSide,
        val x0: Float,
        val y0: Float,
    )

    private enum class EdgeSide {
        Left,
        Right,
        Top,
        Bottom,
    }
}
