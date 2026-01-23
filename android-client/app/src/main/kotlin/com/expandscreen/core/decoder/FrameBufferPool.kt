package com.expandscreen.core.decoder

/**
 * Simple pool for reusing encoded frame buffers to reduce GC churn.
 */
class FrameBufferPool(
    private val maxPoolSize: Int = 12,
    private val maxBufferSizeBytes: Int = 4 * 1024 * 1024,
) {
    private val lock = Any()
    private val pool = ArrayDeque<ByteArray>()

    fun acquire(minSize: Int): ByteArray {
        if (minSize <= 0) return ByteArray(0)
        synchronized(lock) {
            val iterator = pool.iterator()
            while (iterator.hasNext()) {
                val buffer = iterator.next()
                if (buffer.size >= minSize) {
                    iterator.remove()
                    return buffer
                }
            }
        }
        return ByteArray(minSize)
    }

    fun release(buffer: ByteArray) {
        if (buffer.isEmpty() || buffer.size > maxBufferSizeBytes) return
        synchronized(lock) {
            if (pool.size >= maxPoolSize) return
            pool.addLast(buffer)
        }
    }

    internal fun sizeForTest(): Int {
        synchronized(lock) {
            return pool.size
        }
    }
}
