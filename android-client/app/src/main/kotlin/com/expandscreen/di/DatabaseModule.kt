package com.expandscreen.di

import android.content.Context
import androidx.room.Room
import com.expandscreen.data.database.ConnectionLogDao
import com.expandscreen.data.database.DeviceDao
import com.expandscreen.data.database.ExpandScreenDatabase
import com.expandscreen.data.database.ExpandScreenDatabaseMigrations
import dagger.Module
import dagger.Provides
import dagger.hilt.InstallIn
import dagger.hilt.android.qualifiers.ApplicationContext
import dagger.hilt.components.SingletonComponent
import javax.inject.Singleton

/**
 * Database Module
 *
 * Provides Room database and DAO instances.
 */
@Module
@InstallIn(SingletonComponent::class)
object DatabaseModule {

    @Provides
    @Singleton
    fun provideDatabase(
        @ApplicationContext context: Context
    ): ExpandScreenDatabase {
        return Room.databaseBuilder(
            context,
            ExpandScreenDatabase::class.java,
            "expandscreen_db"
        )
            .addMigrations(*ExpandScreenDatabaseMigrations.ALL)
            .fallbackToDestructiveMigrationOnDowngrade()
            .build()
    }

    @Provides
    @Singleton
    fun provideDeviceDao(database: ExpandScreenDatabase): DeviceDao {
        return database.deviceDao()
    }

    @Provides
    @Singleton
    fun provideConnectionLogDao(database: ExpandScreenDatabase): ConnectionLogDao {
        return database.connectionLogDao()
    }
}
