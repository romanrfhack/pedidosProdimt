BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102152307_AddCustomerProductSetup'
)
BEGIN
    ALTER TABLE [CustomerFrequentProducts] ADD [UseCustomerDefaultMachine] bit NOT NULL DEFAULT CAST(1 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102152307_AddCustomerProductSetup'
)
BEGIN
    CREATE TABLE [CustomerProductSetupRequests] (
        [Id] uniqueidentifier NOT NULL,
        [CustomerId] uniqueidentifier NOT NULL,
        [ProductId] uniqueidentifier NOT NULL,
        [ProductNameSnapshot] nvarchar(200) NOT NULL,
        [UnitCode] nvarchar(16) NOT NULL,
        [Version] int NOT NULL,
        [IsResolved] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [LastRequestedAt] datetimeoffset NOT NULL,
        [ResolvedAt] datetimeoffset NULL,
        [ResolvedBy] nvarchar(200) NULL,
        [ResolvedByName] nvarchar(200) NULL,
        CONSTRAINT [PK_CustomerProductSetupRequests] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ProductSetup_Version] CHECK ([Version] > 0),
        CONSTRAINT [FK_CustomerProductSetupRequests_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_CustomerProductSetupRequests_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102152307_AddCustomerProductSetup'
)
BEGIN
    CREATE TABLE [CustomerProductSetupOrders] (
        [SetupRequestId] uniqueidentifier NOT NULL,
        [OrderId] uniqueidentifier NOT NULL,
        [Source] nvarchar(16) NOT NULL,
        [RequestedAt] datetimeoffset NOT NULL,
        [ActorId] nvarchar(200) NULL,
        [ActorDisplayName] nvarchar(200) NULL,
        CONSTRAINT [PK_CustomerProductSetupOrders] PRIMARY KEY ([SetupRequestId], [OrderId]),
        CONSTRAINT [FK_CustomerProductSetupOrders_CustomerProductSetupRequests_SetupRequestId] FOREIGN KEY ([SetupRequestId]) REFERENCES [CustomerProductSetupRequests] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_CustomerProductSetupOrders_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102152307_AddCustomerProductSetup'
)
BEGIN
    CREATE INDEX [IX_CustomerProductSetupOrders_OrderId] ON [CustomerProductSetupOrders] ([OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102152307_AddCustomerProductSetup'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CustomerProductSetupRequests_CustomerId_ProductId] ON [CustomerProductSetupRequests] ([CustomerId], [ProductId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102152307_AddCustomerProductSetup'
)
BEGIN
    CREATE INDEX [IX_CustomerProductSetupRequests_IsResolved_LastRequestedAt] ON [CustomerProductSetupRequests] ([IsResolved], [LastRequestedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102152307_AddCustomerProductSetup'
)
BEGIN
    CREATE INDEX [IX_CustomerProductSetupRequests_ProductId] ON [CustomerProductSetupRequests] ([ProductId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102152307_AddCustomerProductSetup'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261102152307_AddCustomerProductSetup', N'10.0.8');
END;

COMMIT;
GO

