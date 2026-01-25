package com.expandscreen.core.security

import android.content.Context
import android.content.SharedPreferences
import dagger.hilt.android.qualifiers.ApplicationContext
import javax.inject.Inject
import javax.inject.Singleton
import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json

@Singleton
class TrustedHostStore @Inject constructor(
    @ApplicationContext context: Context,
) {
    private val prefs: SharedPreferences =
        context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)

    private val json =
        Json {
            ignoreUnknownKeys = true
            encodeDefaults = true
        }

    fun listTrustedHosts(): List<TrustedHost> {
        return readAll().entries
            .map { (host, entry) -> TrustedHost(host = host, fingerprintSha256Hex = entry.fingerprintSha256Hex, addedAtMs = entry.addedAtMs) }
            .sortedByDescending { it.addedAtMs }
    }

    fun getPinnedFingerprint(host: String): String? {
        return readAll()[host]?.fingerprintSha256Hex
    }

    fun trustHost(host: String, fingerprintSha256Hex: String) {
        val normalized = fingerprintSha256Hex.replace(":", "").replace(" ", "").trim().uppercase()
        val all = readAll().toMutableMap()
        all[host] = TrustedEntry(fingerprintSha256Hex = normalized, addedAtMs = System.currentTimeMillis())
        writeAll(all)
    }

    fun revokeHost(host: String) {
        val all = readAll().toMutableMap()
        all.remove(host)
        writeAll(all)
    }

    fun clearAll() {
        prefs.edit().remove(KEY_TRUSTED_HOSTS_JSON).apply()
    }

    private fun readAll(): Map<String, TrustedEntry> {
        val raw = prefs.getString(KEY_TRUSTED_HOSTS_JSON, null) ?: return emptyMap()
        return runCatching { json.decodeFromString(TrustedMap.serializer(), raw).hosts }.getOrElse { emptyMap() }
    }

    private fun writeAll(map: Map<String, TrustedEntry>) {
        val payload = json.encodeToString(TrustedMap.serializer(), TrustedMap(hosts = map))
        prefs.edit().putString(KEY_TRUSTED_HOSTS_JSON, payload).apply()
    }

    @Serializable
    private data class TrustedMap(
        @SerialName("hosts")
        val hosts: Map<String, TrustedEntry> = emptyMap(),
    )

    @Serializable
    private data class TrustedEntry(
        @SerialName("fingerprintSha256Hex")
        val fingerprintSha256Hex: String,
        @SerialName("addedAtMs")
        val addedAtMs: Long,
    )

    data class TrustedHost(
        val host: String,
        val fingerprintSha256Hex: String,
        val addedAtMs: Long,
    )

    companion object {
        private const val PREFS_NAME = "expandscreen_trusted_hosts"
        private const val KEY_TRUSTED_HOSTS_JSON = "trusted_hosts_json"
    }
}

