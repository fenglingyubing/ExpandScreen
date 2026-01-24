package com.expandscreen.protocol

import kotlinx.serialization.KSerializer
import kotlinx.serialization.json.Json

object DiscoveryCodec {
    private val json =
        Json {
            ignoreUnknownKeys = true
            encodeDefaults = true
        }

    fun <T> encodeJsonPayload(payload: T, serializer: KSerializer<T>): ByteArray {
        return json.encodeToString(serializer, payload).encodeToByteArray()
    }

    fun <T> decodeJsonPayload(payload: ByteArray, serializer: KSerializer<T>): T {
        return json.decodeFromString(serializer, payload.decodeToString())
    }
}

