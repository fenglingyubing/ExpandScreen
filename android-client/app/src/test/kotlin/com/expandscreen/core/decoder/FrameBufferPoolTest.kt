package com.expandscreen.core.decoder

import org.junit.Assert.assertEquals
import org.junit.Assert.assertSame
import org.junit.Test

class FrameBufferPoolTest {

    @Test
    fun reuseReleasedBufferWhenSizeFits() {
        val pool = FrameBufferPool(maxPoolSize = 2, maxBufferSizeBytes = 1024)
        val buffer = pool.acquire(512)
        pool.release(buffer)

        val reused = pool.acquire(256)
        assertSame(buffer, reused)
    }

    @Test
    fun respectsMaxPoolSize() {
        val pool = FrameBufferPool(maxPoolSize = 1, maxBufferSizeBytes = 1024)
        val bufferA = pool.acquire(128)
        val bufferB = pool.acquire(128)

        pool.release(bufferA)
        pool.release(bufferB)

        assertEquals(1, pool.sizeForTest())
    }

    @Test
    fun dropsBuffersOverMaxSize() {
        val pool = FrameBufferPool(maxPoolSize = 2, maxBufferSizeBytes = 16)
        val largeBuffer = ByteArray(64)

        pool.release(largeBuffer)

        assertEquals(0, pool.sizeForTest())
    }

    @Test
    fun encodedFrameReleaseReturnsBuffer() {
        val pool = FrameBufferPool(maxPoolSize = 1, maxBufferSizeBytes = 1024)
        val buffer = pool.acquire(128)
        assertEquals(0, pool.sizeForTest())

        val frame =
            EncodedFrame(
                data = buffer,
                size = buffer.size,
                presentationTimeUs = 0,
                isKeyFrame = true,
                width = 1920,
                height = 1080,
                onRelease = pool::release,
            )

        frame.release()
        assertEquals(1, pool.sizeForTest())
    }
}
