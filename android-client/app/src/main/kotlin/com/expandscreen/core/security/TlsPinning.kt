package com.expandscreen.core.security

import java.net.InetSocketAddress
import java.security.MessageDigest
import java.security.SecureRandom
import java.security.cert.X509Certificate
import javax.net.ssl.SSLContext
import javax.net.ssl.SSLSocket
import javax.net.ssl.X509TrustManager

object TlsPinning {
    data class PeerInfo(
        val fingerprintSha256Hex: String,
        val pairingCode6: String,
    )

    class PairingRequiredException(
        val peer: PeerInfo,
        val reason: Reason,
    ) : Exception(
        when (reason) {
            Reason.NotTrusted -> "TLS peer not trusted (pairing required)"
            Reason.FingerprintMismatch -> "TLS peer certificate changed (re-pair required)"
        },
    ) {
        enum class Reason {
            NotTrusted,
            FingerprintMismatch,
        }
    }

    fun connectPinned(
        host: String,
        port: Int,
        connectTimeoutMs: Int,
        pinnedFingerprintSha256Hex: String?,
    ): SSLSocket {
        val trustAll =
            object : X509TrustManager {
                override fun checkClientTrusted(chain: Array<out X509Certificate>?, authType: String?) = Unit
                override fun checkServerTrusted(chain: Array<out X509Certificate>?, authType: String?) = Unit
                override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
            }

        val context = SSLContext.getInstance("TLS")
        context.init(null, arrayOf(trustAll), SecureRandom())

        val socket = context.socketFactory.createSocket() as SSLSocket
        socket.useClientMode = true
        socket.tcpNoDelay = true
        socket.keepAlive = true
        socket.connect(InetSocketAddress(host, port), connectTimeoutMs)
        socket.enabledProtocols =
            socket.supportedProtocols.filter { it == "TLSv1.3" || it == "TLSv1.2" }.toTypedArray().ifEmpty {
                socket.enabledProtocols
            }

        socket.startHandshake()

        val peerCert = socket.session.peerCertificates.firstOrNull() as? X509Certificate
            ?: throw IllegalStateException("No peer certificate")

        val peer = peerInfo(peerCert)
        val pinnedNormalized = pinnedFingerprintSha256Hex?.normalizeFingerprint()
        if (pinnedNormalized.isNullOrBlank()) {
            socket.close()
            throw PairingRequiredException(peer = peer, reason = PairingRequiredException.Reason.NotTrusted)
        }

        if (!pinnedNormalized.equals(peer.fingerprintSha256Hex.normalizeFingerprint(), ignoreCase = true)) {
            socket.close()
            throw PairingRequiredException(peer = peer, reason = PairingRequiredException.Reason.FingerprintMismatch)
        }

        return socket
    }

    fun peerInfo(certificate: X509Certificate): PeerInfo {
        val fingerprint = sha256(certificate.encoded)
        val hex = fingerprint.toHex()
        val code = pairingCode6(fingerprint)
        return PeerInfo(fingerprintSha256Hex = hex, pairingCode6 = code)
    }

    private fun sha256(data: ByteArray): ByteArray {
        return MessageDigest.getInstance("SHA-256").digest(data)
    }

    private fun pairingCode6(fingerprintSha256: ByteArray): String {
        require(fingerprintSha256.size >= 4)
        val value =
            ((fingerprintSha256[0].toInt() and 0xFF) shl 24) or
                ((fingerprintSha256[1].toInt() and 0xFF) shl 16) or
                ((fingerprintSha256[2].toInt() and 0xFF) shl 8) or
                (fingerprintSha256[3].toInt() and 0xFF)

        val code = (value.toLong() and 0xFFFF_FFFFL) % 1_000_000L
        return code.toString().padStart(6, '0')
    }
}

private fun ByteArray.toHex(): String {
    val chars = CharArray(size * 2)
    var i = 0
    for (b in this) {
        val v = b.toInt() and 0xFF
        chars[i++] = "0123456789ABCDEF"[v ushr 4]
        chars[i++] = "0123456789ABCDEF"[v and 0x0F]
    }
    return String(chars)
}

private fun String.normalizeFingerprint(): String {
    return this.replace(":", "").replace(" ", "").trim()
}

