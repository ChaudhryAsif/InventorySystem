BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001060650_PosIntegrityAndAccess'
)
BEGIN
    ALTER TABLE [SaleReturn] ADD [CancelledBy] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001060650_PosIntegrityAndAccess'
)
BEGIN
    ALTER TABLE [SaleReturn] ADD [CancelledUtc] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001060650_PosIntegrityAndAccess'
)
BEGIN
    ALTER TABLE [SaleReturn] ADD [IsCancelled] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001060650_PosIntegrityAndAccess'
)
BEGIN
    ALTER TABLE [SaleInvoice] ADD [CancelledBy] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001060650_PosIntegrityAndAccess'
)
BEGIN
    ALTER TABLE [SaleInvoice] ADD [CancelledUtc] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001060650_PosIntegrityAndAccess'
)
BEGIN
    ALTER TABLE [SaleInvoice] ADD [IsCancelled] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001060650_PosIntegrityAndAccess'
)
BEGIN
    ALTER TABLE [PurchaseReturn] ADD [CancelledBy] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001060650_PosIntegrityAndAccess'
)
BEGIN
    ALTER TABLE [PurchaseReturn] ADD [CancelledUtc] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001060650_PosIntegrityAndAccess'
)
BEGIN
    ALTER TABLE [PurchaseReturn] ADD [IsCancelled] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001060650_PosIntegrityAndAccess'
)
BEGIN
    ALTER TABLE [PurchaseInvoice] ADD [CancelledBy] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001060650_PosIntegrityAndAccess'
)
BEGIN
    ALTER TABLE [PurchaseInvoice] ADD [CancelledUtc] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001060650_PosIntegrityAndAccess'
)
BEGIN
    ALTER TABLE [PurchaseInvoice] ADD [IsCancelled] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001060650_PosIntegrityAndAccess'
)
BEGIN
    ALTER TABLE [Item] ADD [Barcode] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001060650_PosIntegrityAndAccess'
)
BEGIN
    ALTER TABLE [Item] ADD [ReorderLevel] decimal(18,2) NOT NULL DEFAULT 50.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001060650_PosIntegrityAndAccess'
)
BEGIN
    ALTER TABLE [AppUsers] ADD [BranchIds] nvarchar(200) NOT NULL DEFAULT N'1';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001060650_PosIntegrityAndAccess'
)
BEGIN
    CREATE TABLE [OperationRecords] (
        [Id] bigint NOT NULL IDENTITY,
        [UserId] nvarchar(100) NOT NULL,
        [Action] nvarchar(100) NOT NULL,
        [Reference] nvarchar(100) NULL,
        [RequestId] nvarchar(64) NULL,
        [RequestHash] nvarchar(64) NULL,
        [ResponseJson] nvarchar(max) NULL,
        [CreatedUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_OperationRecords] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001060650_PosIntegrityAndAccess'
)
BEGIN
    EXEC(N'UPDATE [AppUsers] SET [BranchIds] = N''1''
    WHERE [Id] = 1;
    SELECT @@ROWCOUNT');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001060650_PosIntegrityAndAccess'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Action', N'Code', N'CreatedAt', N'Description', N'IsActive', N'Module', N'Name') AND [object_id] = OBJECT_ID(N'[Permissions]'))
        SET IDENTITY_INSERT [Permissions] ON;
    EXEC(N'INSERT INTO [Permissions] ([Id], [Action], [Code], [CreatedAt], [Description], [IsActive], [Module], [Name])
    VALUES (32, N''Edit'', N''Sale.Edit'', ''0001-01-01T00:00:00.0000000'', N'''', CAST(1 AS bit), N''Sale'', N''Sale Edit''),
    (33, N''Delete'', N''Sale.Delete'', ''0001-01-01T00:00:00.0000000'', N'''', CAST(1 AS bit), N''Sale'', N''Sale Delete''),
    (34, N''Edit'', N''Production.Edit'', ''0001-01-01T00:00:00.0000000'', N'''', CAST(1 AS bit), N''Production'', N''Production Edit''),
    (35, N''Delete'', N''Production.Delete'', ''0001-01-01T00:00:00.0000000'', N'''', CAST(1 AS bit), N''Production'', N''Production Delete''),
    (36, N''Post'', N''StockAdjustment.Post'', ''0001-01-01T00:00:00.0000000'', N'''', CAST(1 AS bit), N''StockAdjustment'', N''StockAdjustment Post''),
    (37, N''Delete'', N''StockAdjustment.Delete'', ''0001-01-01T00:00:00.0000000'', N'''', CAST(1 AS bit), N''StockAdjustment'', N''StockAdjustment Delete''),
    (38, N''View'', N''Parties.View'', ''0001-01-01T00:00:00.0000000'', N'''', CAST(1 AS bit), N''Parties'', N''Parties View''),
    (39, N''Create'', N''Parties.Create'', ''0001-01-01T00:00:00.0000000'', N'''', CAST(1 AS bit), N''Parties'', N''Parties Create''),
    (40, N''Edit'', N''Parties.Edit'', ''0001-01-01T00:00:00.0000000'', N'''', CAST(1 AS bit), N''Parties'', N''Parties Edit''),
    (41, N''Delete'', N''Parties.Delete'', ''0001-01-01T00:00:00.0000000'', N'''', CAST(1 AS bit), N''Parties'', N''Parties Delete''),
    (42, N''Edit'', N''Accounts.Edit'', ''0001-01-01T00:00:00.0000000'', N'''', CAST(1 AS bit), N''Accounts'', N''Accounts Edit''),
    (43, N''Post'', N''Accounts.Post'', ''0001-01-01T00:00:00.0000000'', N'''', CAST(1 AS bit), N''Accounts'', N''Accounts Post''),
    (44, N''View'', N''SaleReturn.View'', ''0001-01-01T00:00:00.0000000'', N'''', CAST(1 AS bit), N''SaleReturn'', N''SaleReturn View''),
    (45, N''Create'', N''SaleReturn.Create'', ''0001-01-01T00:00:00.0000000'', N'''', CAST(1 AS bit), N''SaleReturn'', N''SaleReturn Create''),
    (46, N''Edit'', N''SaleReturn.Edit'', ''0001-01-01T00:00:00.0000000'', N'''', CAST(1 AS bit), N''SaleReturn'', N''SaleReturn Edit''),
    (47, N''Delete'', N''SaleReturn.Delete'', ''0001-01-01T00:00:00.0000000'', N'''', CAST(1 AS bit), N''SaleReturn'', N''SaleReturn Delete''),
    (48, N''View'', N''PurchaseReturn.View'', ''0001-01-01T00:00:00.0000000'', N'''', CAST(1 AS bit), N''PurchaseReturn'', N''PurchaseReturn View''),
    (49, N''Create'', N''PurchaseReturn.Create'', ''0001-01-01T00:00:00.0000000'', N'''', CAST(1 AS bit), N''PurchaseReturn'', N''PurchaseReturn Create''),
    (50, N''Edit'', N''PurchaseReturn.Edit'', ''0001-01-01T00:00:00.0000000'', N'''', CAST(1 AS bit), N''PurchaseReturn'', N''PurchaseReturn Edit''),
    (51, N''Delete'', N''PurchaseReturn.Delete'', ''0001-01-01T00:00:00.0000000'', N'''', CAST(1 AS bit), N''PurchaseReturn'', N''PurchaseReturn Delete'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Action', N'Code', N'CreatedAt', N'Description', N'IsActive', N'Module', N'Name') AND [object_id] = OBJECT_ID(N'[Permissions]'))
        SET IDENTITY_INSERT [Permissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001060650_PosIntegrityAndAccess'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'CreatedAt', N'PermissionId', N'RoleId') AND [object_id] = OBJECT_ID(N'[RolePermissions]'))
        SET IDENTITY_INSERT [RolePermissions] ON;
    EXEC(N'INSERT INTO [RolePermissions] ([Id], [CreatedAt], [PermissionId], [RoleId])
    VALUES (1032, ''0001-01-01T00:00:00.0000000'', 32, 1),
    (1033, ''0001-01-01T00:00:00.0000000'', 33, 1),
    (1034, ''0001-01-01T00:00:00.0000000'', 34, 1),
    (1035, ''0001-01-01T00:00:00.0000000'', 35, 1),
    (1036, ''0001-01-01T00:00:00.0000000'', 36, 1),
    (1037, ''0001-01-01T00:00:00.0000000'', 37, 1),
    (1038, ''0001-01-01T00:00:00.0000000'', 38, 1),
    (1039, ''0001-01-01T00:00:00.0000000'', 39, 1),
    (1040, ''0001-01-01T00:00:00.0000000'', 40, 1),
    (1041, ''0001-01-01T00:00:00.0000000'', 41, 1),
    (1042, ''0001-01-01T00:00:00.0000000'', 42, 1),
    (1043, ''0001-01-01T00:00:00.0000000'', 43, 1),
    (1044, ''0001-01-01T00:00:00.0000000'', 44, 1),
    (1045, ''0001-01-01T00:00:00.0000000'', 45, 1),
    (1046, ''0001-01-01T00:00:00.0000000'', 46, 1),
    (1047, ''0001-01-01T00:00:00.0000000'', 47, 1),
    (1048, ''0001-01-01T00:00:00.0000000'', 48, 1),
    (1049, ''0001-01-01T00:00:00.0000000'', 49, 1),
    (1050, ''0001-01-01T00:00:00.0000000'', 50, 1),
    (1051, ''0001-01-01T00:00:00.0000000'', 51, 1)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'CreatedAt', N'PermissionId', N'RoleId') AND [object_id] = OBJECT_ID(N'[RolePermissions]'))
        SET IDENTITY_INSERT [RolePermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001060650_PosIntegrityAndAccess'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Item_Barcode] ON [Item] ([Barcode]) WHERE [Barcode] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001060650_PosIntegrityAndAccess'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_OperationRecords_UserId_Action_RequestId] ON [OperationRecords] ([UserId], [Action], [RequestId]) WHERE [RequestId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001060650_PosIntegrityAndAccess'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261001060650_PosIntegrityAndAccess', N'9.0.10');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001072449_CancellationReasons'
)
BEGIN
    ALTER TABLE [SaleReturn] ADD [CancellationReason] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001072449_CancellationReasons'
)
BEGIN
    ALTER TABLE [SaleInvoice] ADD [CancellationReason] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001072449_CancellationReasons'
)
BEGIN
    ALTER TABLE [PurchaseReturn] ADD [CancellationReason] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001072449_CancellationReasons'
)
BEGIN
    ALTER TABLE [PurchaseInvoice] ADD [CancellationReason] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001072449_CancellationReasons'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261001072449_CancellationReasons', N'9.0.10');
END;

COMMIT;
GO

