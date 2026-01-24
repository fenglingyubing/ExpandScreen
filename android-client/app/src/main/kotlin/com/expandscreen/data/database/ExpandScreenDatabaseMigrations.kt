package com.expandscreen.data.database

import androidx.room.migration.Migration

/**
 * Room migrations for [ExpandScreenDatabase].
 *
 * Migration strategy:
 * - Database upgrades MUST provide explicit migrations and bump schema version.
 * - Downgrades are treated as dev-only (destructive) to avoid undefined behavior.
 */
object ExpandScreenDatabaseMigrations {
    val ALL: Array<Migration> = emptyArray()
}

