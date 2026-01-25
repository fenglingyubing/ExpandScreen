package com.expandscreen.ui.qr

import java.net.URI
import java.net.URLDecoder
import java.nio.charset.StandardCharsets
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json

object QrCodeParser {
    private val json =
        Json {
            ignoreUnknownKeys = true
            isLenient = true
        }

    fun parse(raw: String): Result<QrConnectInfo> {
        val trimmed = raw.trim()
        if (trimmed.isBlank()) return Result.failure(IllegalArgumentException("二维码内容为空"))

        parseAsJson(trimmed)?.let { return it }
        parseAsUriOrHostPort(trimmed)?.let { return it }

        return Result.failure(IllegalArgumentException("不支持的二维码格式"))
    }

    private fun parseAsJson(trimmed: String): Result<QrConnectInfo>? {
        if (!trimmed.startsWith("{")) return null

        val model =
            runCatching { json.decodeFromString(QrConnectJson.serializer(), trimmed) }.getOrNull()
                ?: return Result.failure(IllegalArgumentException("二维码JSON解析失败"))

        val host = (model.host ?: model.ip)?.trim().orEmpty()
        val port = model.port
        if (host.isBlank()) return Result.failure(IllegalArgumentException("二维码缺少 host/ip"))
        if (port == null) return Result.failure(IllegalArgumentException("二维码缺少 port"))

        return validateAndBuild(host = host, port = port, token = model.token ?: model.auth, deviceName = model.name)
    }

    private fun parseAsUriOrHostPort(trimmed: String): Result<QrConnectInfo>? {
        parseAsHostPort(trimmed)?.let { return it }

        val normalized =
            when {
                trimmed.contains("://") -> trimmed
                looksLikeQueryOnly(trimmed) -> "expandscreen://connect?${trimmed.replace(';', '&')}"
                else -> "expandscreen://${trimmed}"
            }

        val uri = runCatching { URI(normalized) }.getOrNull() ?: return null
        val query = parseQuery(uri.rawQuery)

        val host =
            sequenceOf(
                query["host"],
                query["ip"],
                uri.host?.takeIf { it.isNotBlank() && it.lowercase() !in setOf("connect", "scan") },
                parseHostFromPath(uri.rawPath),
            ).firstOrNull { !it.isNullOrBlank() }?.trim().orEmpty()

        val port =
            sequenceOf(
                query["port"]?.toIntOrNull(),
                uri.port.takeIf { it in 1..65535 },
                parsePortFromPath(uri.rawPath),
            ).firstOrNull()

        if (host.isBlank()) return Result.failure(IllegalArgumentException("二维码缺少 host/ip"))
        if (port == null) return Result.failure(IllegalArgumentException("二维码缺少 port"))

        val token = query["token"] ?: query["auth"] ?: query["secret"]
        val deviceName = query["name"] ?: query["deviceName"] ?: query["serverName"]

        return validateAndBuild(host = host, port = port, token = token, deviceName = deviceName)
    }

    private fun parseAsHostPort(trimmed: String): Result<QrConnectInfo>? {
        val base = trimmed.substringBefore('?').trim()
        val match = HOST_PORT_REGEX.matchEntire(base) ?: return null

        val host = match.groupValues[1].trim()
        val port = match.groupValues[2].toIntOrNull()
            ?: return Result.failure(IllegalArgumentException("port 无效"))

        val query = parseQuery(trimmed.substringAfter('?', missingDelimiterValue = ""))
        val token = query["token"] ?: query["auth"] ?: query["secret"]
        val deviceName = query["name"] ?: query["deviceName"] ?: query["serverName"]
        return validateAndBuild(host = host, port = port, token = token, deviceName = deviceName)
    }

    private fun looksLikeQueryOnly(value: String): Boolean {
        if (!value.contains("=")) return false
        if (value.contains("://")) return false
        if (value.contains(" ")) return false
        return true
    }

    private fun parseHostFromPath(path: String?): String? {
        val p = path?.trim('/')?.takeIf { it.isNotBlank() } ?: return null
        val hostPort = p.substringAfterLast('/').trim()
        val host = hostPort.substringBefore(':').trim()
        return host.takeIf { it.isNotBlank() && hostPort.contains(':') }
    }

    private fun parsePortFromPath(path: String?): Int? {
        val p = path?.trim('/')?.takeIf { it.isNotBlank() } ?: return null
        val hostPort = p.substringAfterLast('/').trim()
        val port = hostPort.substringAfter(':', missingDelimiterValue = "").trim()
        return port.toIntOrNull()
    }

    private fun parseQuery(query: String?): Map<String, String> {
        if (query.isNullOrBlank()) return emptyMap()
        return query
            .split('&')
            .asSequence()
            .mapNotNull { part ->
                val cleaned = part.trim().takeIf { it.isNotBlank() } ?: return@mapNotNull null
                val idx = cleaned.indexOf('=')
                if (idx <= 0) return@mapNotNull null
                val key = decode(cleaned.substring(0, idx)).trim()
                val value = decode(cleaned.substring(idx + 1)).trim()
                if (key.isBlank() || value.isBlank()) return@mapNotNull null
                key to value
            }
            .toMap()
    }

    private fun decode(value: String): String {
        return runCatching { URLDecoder.decode(value, StandardCharsets.UTF_8.name()) }.getOrElse { value }
    }

    private fun validateAndBuild(
        host: String,
        port: Int,
        token: String?,
        deviceName: String?,
    ): Result<QrConnectInfo> {
        val normalizedHost = host.trim()
        if (normalizedHost.isBlank() || normalizedHost.any { it.isWhitespace() }) {
            return Result.failure(IllegalArgumentException("host 无效"))
        }
        if (port !in 1..65535) {
            return Result.failure(IllegalArgumentException("port 无效"))
        }

        return Result.success(
            QrConnectInfo(
                host = normalizedHost,
                port = port,
                token = token?.trim()?.takeIf { it.isNotBlank() },
                deviceName = deviceName?.trim()?.takeIf { it.isNotBlank() },
            ),
        )
    }

    @Serializable
    private data class QrConnectJson(
        val host: String? = null,
        val ip: String? = null,
        val port: Int? = null,
        val token: String? = null,
        val auth: String? = null,
        val name: String? = null,
    )

    private val HOST_PORT_REGEX = Regex("^([^\\s:/?#]+):(\\d{1,5})\$")
}
