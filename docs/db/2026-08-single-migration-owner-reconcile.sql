/* ============================================================================
   Reconcile __EFMigrationsHistory for single-migration-owner (gbw-api#5)
   ----------------------------------------------------------------------------
   Context
     Historically both gbw-api and gbw-ingestor maintained their own EF Core
     migration histories against the SAME physical database (eBirdIngestor),
     sharing one __EFMigrationsHistory table.

     We are moving to Option A: gbw-ingestor is the SINGLE owner of the schema
     and of all migrations. gbw-api keeps no migrations of its own.

   What this script does (run ONCE against the shared eBirdIngestor database)
     1. Removes the four retired gbw-api migration rows from the shared history.
        The objects they created (AppUsers, SavedLocations, SavedTargets and the
        Species taxonomy columns) ALREADY EXIST and are kept — only the history
        bookkeeping rows are removed.
     2. Registers the new gbw-ingestor migration
        '20260804043125_AbsorbApiOwnedSchema' as already-applied, because the
        objects it describes already physically exist. This stops the ingestor's
        startup MigrateAsync() from trying to re-create them.

   Safety
     - Idempotent: safe to run more than once.
     - Wrapped in a transaction.
     - Does NOT drop or alter any data/table/column — history bookkeeping only.

   How to run
     Apply BEFORE deploying the new gbw-ingestor build (the one that contains the
     AbsorbApiOwnedSchema migration). Back up the database first.
   ============================================================================ */

SET XACT_ABORT ON;
BEGIN TRANSACTION;

/* 1. Retire the four gbw-api migration rows (objects are intentionally kept). */
DELETE FROM [dbo].[__EFMigrationsHistory]
WHERE [MigrationId] IN (
    N'20260410004347_AddUsersAndSavedLocations',
    N'20260414034538_AddTaxonomyFields',
    N'20260414160156_AddSavedTargets',
    N'20260414163440_AddSearchTermToSavedTarget'
);

/* 2. Register the consolidating gbw-ingestor migration as already-applied. */
IF NOT EXISTS (
    SELECT 1 FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804043125_AbsorbApiOwnedSchema'
)
BEGIN
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260804043125_AbsorbApiOwnedSchema', N'9.0.8');
END

COMMIT TRANSACTION;

/* Verify (optional): the four API rows are gone and the ingestor row is present. */
SELECT [MigrationId], [ProductVersion]
FROM [dbo].[__EFMigrationsHistory]
ORDER BY [MigrationId];
