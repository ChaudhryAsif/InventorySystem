BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019095402_ClientCartonCosting'
)
BEGIN
    DECLARE @var sysname;
    SELECT @var = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[CostSheet]') AND [c].[name] = N'WithFlap');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [CostSheet] DROP CONSTRAINT [' + @var + '];');
    ALTER TABLE [CostSheet] ALTER COLUMN [WithFlap] decimal(22,6) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019095402_ClientCartonCosting'
)
BEGIN
    DECLARE @var1 sysname;
    SELECT @var1 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[CostSheet]') AND [c].[name] = N'Width');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [CostSheet] DROP CONSTRAINT [' + @var1 + '];');
    ALTER TABLE [CostSheet] ALTER COLUMN [Width] decimal(22,6) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019095402_ClientCartonCosting'
)
BEGIN
    DECLARE @var2 sysname;
    SELECT @var2 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[CostSheet]') AND [c].[name] = N'TotalSheetSqIn');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [CostSheet] DROP CONSTRAINT [' + @var2 + '];');
    ALTER TABLE [CostSheet] ALTER COLUMN [TotalSheetSqIn] decimal(22,6) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019095402_ClientCartonCosting'
)
BEGIN
    DECLARE @var3 sysname;
    SELECT @var3 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[CostSheet]') AND [c].[name] = N'SheetWidthIn');
    IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [CostSheet] DROP CONSTRAINT [' + @var3 + '];');
    ALTER TABLE [CostSheet] ALTER COLUMN [SheetWidthIn] decimal(22,6) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019095402_ClientCartonCosting'
)
BEGIN
    DECLARE @var4 sysname;
    SELECT @var4 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[CostSheet]') AND [c].[name] = N'SheetLengthIn');
    IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [CostSheet] DROP CONSTRAINT [' + @var4 + '];');
    ALTER TABLE [CostSheet] ALTER COLUMN [SheetLengthIn] decimal(22,6) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019095402_ClientCartonCosting'
)
BEGIN
    DECLARE @var5 sysname;
    SELECT @var5 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[CostSheet]') AND [c].[name] = N'Length');
    IF @var5 IS NOT NULL EXEC(N'ALTER TABLE [CostSheet] DROP CONSTRAINT [' + @var5 + '];');
    ALTER TABLE [CostSheet] ALTER COLUMN [Length] decimal(22,6) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019095402_ClientCartonCosting'
)
BEGIN
    DECLARE @var6 sysname;
    SELECT @var6 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[CostSheet]') AND [c].[name] = N'Height');
    IF @var6 IS NOT NULL EXEC(N'ALTER TABLE [CostSheet] DROP CONSTRAINT [' + @var6 + '];');
    ALTER TABLE [CostSheet] ALTER COLUMN [Height] decimal(22,6) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019095402_ClientCartonCosting'
)
BEGIN
    DECLARE @var7 sysname;
    SELECT @var7 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[CostSheet]') AND [c].[name] = N'Flap');
    IF @var7 IS NOT NULL EXEC(N'ALTER TABLE [CostSheet] DROP CONSTRAINT [' + @var7 + '];');
    ALTER TABLE [CostSheet] ALTER COLUMN [Flap] decimal(22,6) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019095402_ClientCartonCosting'
)
BEGIN
    DECLARE @var8 sysname;
    SELECT @var8 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[CostSheet]') AND [c].[name] = N'AutoFlapGap');
    IF @var8 IS NOT NULL EXEC(N'ALTER TABLE [CostSheet] DROP CONSTRAINT [' + @var8 + '];');
    ALTER TABLE [CostSheet] ALTER COLUMN [AutoFlapGap] decimal(22,6) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019095402_ClientCartonCosting'
)
BEGIN
    DECLARE @var9 sysname;
    SELECT @var9 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[CostSheet]') AND [c].[name] = N'AdjWidthMm');
    IF @var9 IS NOT NULL EXEC(N'ALTER TABLE [CostSheet] DROP CONSTRAINT [' + @var9 + '];');
    ALTER TABLE [CostSheet] ALTER COLUMN [AdjWidthMm] decimal(22,6) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019095402_ClientCartonCosting'
)
BEGIN
    DECLARE @var10 sysname;
    SELECT @var10 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[CostSheet]') AND [c].[name] = N'AdjWidthIn');
    IF @var10 IS NOT NULL EXEC(N'ALTER TABLE [CostSheet] DROP CONSTRAINT [' + @var10 + '];');
    ALTER TABLE [CostSheet] ALTER COLUMN [AdjWidthIn] decimal(22,6) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019095402_ClientCartonCosting'
)
BEGIN
    DECLARE @var11 sysname;
    SELECT @var11 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[CostSheet]') AND [c].[name] = N'AdjLengthMm');
    IF @var11 IS NOT NULL EXEC(N'ALTER TABLE [CostSheet] DROP CONSTRAINT [' + @var11 + '];');
    ALTER TABLE [CostSheet] ALTER COLUMN [AdjLengthMm] decimal(22,6) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019095402_ClientCartonCosting'
)
BEGIN
    DECLARE @var12 sysname;
    SELECT @var12 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[CostSheet]') AND [c].[name] = N'AdjLength2In');
    IF @var12 IS NOT NULL EXEC(N'ALTER TABLE [CostSheet] DROP CONSTRAINT [' + @var12 + '];');
    ALTER TABLE [CostSheet] ALTER COLUMN [AdjLength2In] decimal(22,6) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019095402_ClientCartonCosting'
)
BEGIN
    DECLARE @var13 sysname;
    SELECT @var13 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[CostSheet]') AND [c].[name] = N'AdjLength1In');
    IF @var13 IS NOT NULL EXEC(N'ALTER TABLE [CostSheet] DROP CONSTRAINT [' + @var13 + '];');
    ALTER TABLE [CostSheet] ALTER COLUMN [AdjLength1In] decimal(22,6) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019095402_ClientCartonCosting'
)
BEGIN
    ALTER TABLE [CostSheet] ADD [ClientCostingJson] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019095402_ClientCartonCosting'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261019095402_ClientCartonCosting', N'9.0.10');
END;

COMMIT;
GO

