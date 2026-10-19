BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019090245_GroupClassification'
)
BEGIN
    ALTER TABLE [ItemCategory] ADD [GroupType] nvarchar(32) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019090245_GroupClassification'
)
BEGIN
    EXEC(N'
    UPDATE g SET GroupType = types.ItemType
    FROM ItemCategory AS g
    CROSS APPLY (
        SELECT MIN(i.ItemType) AS ItemType
        FROM Item AS i WHERE i.CategoryID = g.CategoryId
        HAVING COUNT(*) > 0
           AND COUNT(i.ItemType) = COUNT(*)
           AND MIN(i.ItemType) = MAX(i.ItemType)
           AND MIN(i.ItemType) IN (''RawMaterial'', ''FinishedGoods'')
    ) AS types
    WHERE g.GroupType IS NULL AND types.ItemType IS NOT NULL;
    UPDATE MenuItems SET Name = N''Groups''
    WHERE Controller = ''Category'' AND Action = ''Index''
      AND Name IN (N''Item Groups'', N''Item Group'', N''Item Category'', N''Item Categories'');');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019090245_GroupClassification'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261019090245_GroupClassification', N'9.0.10');
END;

COMMIT;
GO

