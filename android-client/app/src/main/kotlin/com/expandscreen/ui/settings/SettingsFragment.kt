package com.expandscreen.ui.settings

import android.content.Intent
import android.os.Bundle
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.preference.ListPreference
import androidx.preference.Preference
import androidx.preference.PreferenceFragmentCompat
import androidx.lifecycle.lifecycleScope
import com.expandscreen.BuildConfig
import com.expandscreen.R
import com.expandscreen.data.repository.SettingsRepository
import dagger.hilt.android.AndroidEntryPoint
import javax.inject.Inject
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

@AndroidEntryPoint
class SettingsFragment : PreferenceFragmentCompat() {

    @Inject lateinit var settingsRepository: SettingsRepository

    private val exportLauncher =
        registerForActivityResult(ActivityResultContracts.CreateDocument("application/json")) { uri ->
            if (uri == null) return@registerForActivityResult
            viewLifecycleOwner.lifecycleScope.launch(Dispatchers.IO) {
                val payload = settingsRepository.exportToJson(pretty = true)
                val result =
                    runCatching {
                        requireContext().contentResolver.openOutputStream(uri)?.use { out ->
                            out.write(payload.toByteArray(Charsets.UTF_8))
                        } ?: error("Failed to open output stream")
                    }
                withContext(Dispatchers.Main) {
                    result
                        .onSuccess { toast("Exported") }
                        .onFailure { toast("Export failed: ${it.message ?: it.javaClass.simpleName}") }
                }
            }
        }

    private val importLauncher =
        registerForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
            if (uri == null) return@registerForActivityResult
            viewLifecycleOwner.lifecycleScope.launch(Dispatchers.IO) {
                val content =
                    runCatching {
                        requireContext().contentResolver.openInputStream(uri)?.use { input ->
                            input.readBytes().toString(Charsets.UTF_8)
                        } ?: error("Failed to open input stream")
                    }
                val result =
                    content.fold(
                        onSuccess = { json -> settingsRepository.importFromJson(json) },
                        onFailure = { Result.failure(it) },
                    )
                withContext(Dispatchers.Main) {
                    result
                        .onSuccess { toast("Imported") }
                        .onFailure { toast("Import failed: ${it.message ?: it.javaClass.simpleName}") }
                }
            }
        }

    override fun onCreatePreferences(savedInstanceState: Bundle?, rootKey: String?) {
        preferenceManager.sharedPreferencesName = SettingsRepository.PREFS_NAME
        setPreferencesFromResource(R.xml.settings_preferences, rootKey)

        findPreference<ListPreference>("pref_video_resolution")?.summaryProvider =
            ListPreference.SimpleSummaryProvider.getInstance()
        findPreference<ListPreference>("pref_video_frame_rate")?.summaryProvider =
            ListPreference.SimpleSummaryProvider.getInstance()
        findPreference<ListPreference>("pref_video_quality")?.summaryProvider =
            ListPreference.SimpleSummaryProvider.getInstance()
        findPreference<ListPreference>("pref_perf_preset")?.summaryProvider =
            ListPreference.SimpleSummaryProvider.getInstance()
        findPreference<ListPreference>("pref_display_theme_mode")?.summaryProvider =
            ListPreference.SimpleSummaryProvider.getInstance()
        findPreference<ListPreference>("pref_network_preferred_connection")?.summaryProvider =
            ListPreference.SimpleSummaryProvider.getInstance()

        findPreference<Preference>("pref_about_version")?.summary =
            "${BuildConfig.VERSION_NAME} (${BuildConfig.VERSION_CODE}) • ${BuildConfig.BUILD_TYPE}"

        findPreference<Preference>("pref_config_export")?.setOnPreferenceClickListener {
            exportLauncher.launch("expandscreen-settings.json")
            true
        }
        findPreference<Preference>("pref_config_import")?.setOnPreferenceClickListener {
            importLauncher.launch(arrayOf("application/json", "text/plain", "*/*"))
            true
        }

        findPreference<Preference>("pref_about_licenses")?.setOnPreferenceClickListener {
            startActivity(Intent(requireContext(), OpenSourceLicensesActivity::class.java))
            true
        }
    }

    private fun toast(message: String) {
        Toast.makeText(requireContext(), message, Toast.LENGTH_SHORT).show()
    }
}
