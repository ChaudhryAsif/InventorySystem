BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019113219_GroupContactDetails'
)
BEGIN
    ALTER TABLE [ItemCategory] ADD [Address] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019113219_GroupContactDetails'
)
BEGIN
    ALTER TABLE [ItemCategory] ADD [ContactNumber] nvarchar(32) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019113219_GroupContactDetails'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261019113219_GroupContactDetails', N'9.0.10');
END;

COMMIT;
GO

