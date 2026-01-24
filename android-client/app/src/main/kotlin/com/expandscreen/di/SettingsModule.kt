package com.expandscreen.di

import com.expandscreen.data.repository.SettingsRepository
import com.expandscreen.data.repository.SharedPreferencesSettingsRepository
import dagger.Binds
import dagger.Module
import dagger.hilt.InstallIn
import dagger.hilt.components.SingletonComponent
import javax.inject.Singleton

@Module
@InstallIn(SingletonComponent::class)
abstract class SettingsModule {

    @Binds
    @Singleton
    abstract fun bindSettingsRepository(impl: SharedPreferencesSettingsRepository): SettingsRepository
}

