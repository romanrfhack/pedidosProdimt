IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260527190837_InitialCreate'
)
BEGIN
    CREATE TABLE [Customers] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [PhoneNumber] nvarchar(40) NOT NULL,
        [IsActive] bit NOT NULL,
        [PreferredDeliveryTime] time NULL,
        [PreferredDeliveryWindowStart] time NULL,
        [PreferredDeliveryWindowEnd] time NULL,
        [DeliveryNotes] nvarchar(500) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_Customers] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260527190837_InitialCreate'
)
BEGIN
    CREATE TABLE [Machines] (
        [Id] uniqueidentifier NOT NULL,
        [Number] int NOT NULL,
        [Name] nvarchar(120) NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_Machines] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260527190837_InitialCreate'
)
BEGIN
    CREATE TABLE [Products] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(120) NOT NULL,
        [Description] nvarchar(500) NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_Products] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260527190837_InitialCreate'
)
BEGIN
    CREATE TABLE [SalesChannels] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(80) NOT NULL,
        [Type] nvarchar(40) NOT NULL,
        [IsInternal] bit NOT NULL,
        CONSTRAINT [PK_SalesChannels] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260527190837_InitialCreate'
)
BEGIN
    CREATE TABLE [CustomerMachineAssignments] (
        [CustomerId] uniqueidentifier NOT NULL,
        [MachineId] uniqueidentifier NOT NULL,
        [IsDefault] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [Notes] nvarchar(500) NULL,
        CONSTRAINT [PK_CustomerMachineAssignments] PRIMARY KEY ([CustomerId], [MachineId]),
        CONSTRAINT [FK_CustomerMachineAssignments_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_CustomerMachineAssignments_Machines_MachineId] FOREIGN KEY ([MachineId]) REFERENCES [Machines] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260527190837_InitialCreate'
)
BEGIN
    CREATE TABLE [CustomerFrequentProducts] (
        [CustomerId] uniqueidentifier NOT NULL,
        [ProductId] uniqueidentifier NOT NULL,
        [DefaultQuantity] decimal(18,2) NULL,
        [SortOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_CustomerFrequentProducts] PRIMARY KEY ([CustomerId], [ProductId]),
        CONSTRAINT [FK_CustomerFrequentProducts_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_CustomerFrequentProducts_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260527190837_InitialCreate'
)
BEGIN
    CREATE TABLE [Orders] (
        [Id] uniqueidentifier NOT NULL,
        [CustomerId] uniqueidentifier NULL,
        [SalesChannelId] uniqueidentifier NOT NULL,
        [OrderDate] date NOT NULL,
        [SubmittedAt] datetimeoffset NOT NULL,
        [Status] nvarchar(40) NOT NULL,
        [SequenceNumber] int NOT NULL,
        [IsLate] bit NOT NULL,
        [RequiresAdminReview] bit NOT NULL,
        [AdminReviewReason] nvarchar(60) NULL,
        [AdminDecision] nvarchar(60) NULL,
        [RequestedDeliveryTime] time NULL,
        [RequestedDeliveryWindowStart] time NULL,
        [RequestedDeliveryWindowEnd] time NULL,
        [DeliveryNotes] nvarchar(500) NULL,
        [InternalNotes] nvarchar(1000) NULL,
        CONSTRAINT [PK_Orders] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Orders_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Orders_SalesChannels_SalesChannelId] FOREIGN KEY ([SalesChannelId]) REFERENCES [SalesChannels] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260527190837_InitialCreate'
)
BEGIN
    CREATE TABLE [OrderLines] (
        [Id] uniqueidentifier NOT NULL,
        [OrderId] uniqueidentifier NOT NULL,
        [ProductId] uniqueidentifier NOT NULL,
        [Quantity] decimal(18,2) NOT NULL,
        [AssignedMachineId] uniqueidentifier NULL,
        [Notes] nvarchar(500) NULL,
        CONSTRAINT [PK_OrderLines] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrderLines_Machines_AssignedMachineId] FOREIGN KEY ([AssignedMachineId]) REFERENCES [Machines] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_OrderLines_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_OrderLines_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260527190837_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_CustomerFrequentProducts_ProductId] ON [CustomerFrequentProducts] ([ProductId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260527190837_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_CustomerMachineAssignments_MachineId] ON [CustomerMachineAssignments] ([MachineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260527190837_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Machines_Number] ON [Machines] ([Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260527190837_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrderLines_AssignedMachineId] ON [OrderLines] ([AssignedMachineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260527190837_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrderLines_OrderId] ON [OrderLines] ([OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260527190837_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrderLines_ProductId] ON [OrderLines] ([ProductId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260527190837_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Orders_CustomerId_OrderDate_SequenceNumber] ON [Orders] ([CustomerId], [OrderDate], [SequenceNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260527190837_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Orders_SalesChannelId] ON [Orders] ([SalesChannelId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260527190837_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SalesChannels_Type] ON [SalesChannels] ([Type]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260527190837_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260527190837_InitialCreate', N'10.0.8');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260528021837_AddOrderAuditLogs'
)
BEGIN
    CREATE TABLE [OrderAuditLogs] (
        [Id] uniqueidentifier NOT NULL,
        [OrderId] uniqueidentifier NOT NULL,
        [CustomerId] uniqueidentifier NULL,
        [EventType] nvarchar(80) NOT NULL,
        [OccurredAt] datetimeoffset NOT NULL,
        [ActorType] nvarchar(40) NOT NULL,
        [ActorId] nvarchar(100) NULL,
        [ActorDisplayName] nvarchar(200) NULL,
        [OrderStatus] nvarchar(40) NULL,
        [AdminReviewReason] nvarchar(60) NULL,
        [AdminDecision] nvarchar(60) NULL,
        [Summary] nvarchar(500) NOT NULL,
        [MetadataJson] nvarchar(2000) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_OrderAuditLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrderAuditLogs_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260528021837_AddOrderAuditLogs'
)
BEGIN
    CREATE INDEX [IX_OrderAuditLogs_OrderId_OccurredAt] ON [OrderAuditLogs] ([OrderId], [OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260528021837_AddOrderAuditLogs'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260528021837_AddOrderAuditLogs', N'10.0.8');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260528025208_AddPilotAuthentication'
)
BEGIN
    CREATE TABLE [AdminUsers] (
        [Id] uniqueidentifier NOT NULL,
        [UserName] nvarchar(120) NOT NULL,
        [PasswordHash] nvarchar(500) NOT NULL,
        [DisplayName] nvarchar(200) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_AdminUsers] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260528025208_AddPilotAuthentication'
)
BEGIN
    CREATE TABLE [CustomerAccessTokens] (
        [Id] uniqueidentifier NOT NULL,
        [CustomerId] uniqueidentifier NOT NULL,
        [TokenHash] nvarchar(128) NOT NULL,
        [DisplayName] nvarchar(200) NOT NULL,
        [IsActive] bit NOT NULL,
        [ExpiresAt] datetimeoffset NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [LastUsedAt] datetimeoffset NULL,
        CONSTRAINT [PK_CustomerAccessTokens] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CustomerAccessTokens_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260528025208_AddPilotAuthentication'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AdminUsers_UserName] ON [AdminUsers] ([UserName]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260528025208_AddPilotAuthentication'
)
BEGIN
    CREATE INDEX [IX_CustomerAccessTokens_CustomerId] ON [CustomerAccessTokens] ([CustomerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260528025208_AddPilotAuthentication'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CustomerAccessTokens_TokenHash] ON [CustomerAccessTokens] ([TokenHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260528025208_AddPilotAuthentication'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260528025208_AddPilotAuthentication', N'10.0.8');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260528035015_AddCatalogManagementSupport'
)
BEGIN
    CREATE TABLE [AuditLogs] (
        [Id] uniqueidentifier NOT NULL,
        [EntityType] nvarchar(120) NOT NULL,
        [EntityId] nvarchar(80) NOT NULL,
        [EventType] nvarchar(120) NOT NULL,
        [OccurredAt] datetimeoffset NOT NULL,
        [ActorType] int NOT NULL,
        [ActorId] nvarchar(120) NULL,
        [ActorDisplayName] nvarchar(200) NULL,
        [Summary] nvarchar(500) NOT NULL,
        [MetadataJson] nvarchar(2000) NULL,
        CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260528035015_AddCatalogManagementSupport'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_EntityType_EntityId_OccurredAt] ON [AuditLogs] ([EntityType], [EntityId], [OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260528035015_AddCatalogManagementSupport'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_EventType] ON [AuditLogs] ([EventType]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260528035015_AddCatalogManagementSupport'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260528035015_AddCatalogManagementSupport', N'10.0.8');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260528131416_AddCatalogExternalCodesAndImportSupport'
)
BEGIN
    ALTER TABLE [Products] ADD [ExternalCode] nvarchar(80) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260528131416_AddCatalogExternalCodesAndImportSupport'
)
BEGIN
    ALTER TABLE [Machines] ADD [ExternalCode] nvarchar(80) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260528131416_AddCatalogExternalCodesAndImportSupport'
)
BEGIN
    ALTER TABLE [Customers] ADD [ExternalCode] nvarchar(80) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260528131416_AddCatalogExternalCodesAndImportSupport'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Products_ExternalCode] ON [Products] ([ExternalCode]) WHERE [ExternalCode] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260528131416_AddCatalogExternalCodesAndImportSupport'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Machines_ExternalCode] ON [Machines] ([ExternalCode]) WHERE [ExternalCode] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260528131416_AddCatalogExternalCodesAndImportSupport'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Customers_ExternalCode] ON [Customers] ([ExternalCode]) WHERE [ExternalCode] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260528131416_AddCatalogExternalCodesAndImportSupport'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260528131416_AddCatalogExternalCodesAndImportSupport', N'10.0.8');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024164610_AddPilotCatalogAndOrderSnapshots'
)
BEGIN
    DROP INDEX [IX_Orders_CustomerId_OrderDate_SequenceNumber] ON [Orders];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024164610_AddPilotCatalogAndOrderSnapshots'
)
BEGIN
    ALTER TABLE [Products] ADD [UnitCode] nvarchar(16) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024164610_AddPilotCatalogAndOrderSnapshots'
)
BEGIN
    ALTER TABLE [Orders] ADD [CustomerNameSnapshot] nvarchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024164610_AddPilotCatalogAndOrderSnapshots'
)
BEGIN
    ALTER TABLE [Orders] ADD [HasStoreKey] bit NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024164610_AddPilotCatalogAndOrderSnapshots'
)
BEGIN
    ALTER TABLE [Orders] ADD [InternalDeliveryInstructions] nvarchar(1000) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024164610_AddPilotCatalogAndOrderSnapshots'
)
BEGIN
    ALTER TABLE [Orders] ADD [OrderingProfileVersion] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024164610_AddPilotCatalogAndOrderSnapshots'
)
BEGIN
    ALTER TABLE [Orders] ADD [PaymentTermCode] nvarchar(32) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024164610_AddPilotCatalogAndOrderSnapshots'
)
BEGIN
    ALTER TABLE [Orders] ADD [RouteCode] nvarchar(80) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024164610_AddPilotCatalogAndOrderSnapshots'
)
BEGIN
    ALTER TABLE [Orders] ADD [RouteStopOrder] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024164610_AddPilotCatalogAndOrderSnapshots'
)
BEGIN
    ALTER TABLE [Orders] ADD [Version] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024164610_AddPilotCatalogAndOrderSnapshots'
)
BEGIN
    ALTER TABLE [OrderLines] ADD [ProductNameSnapshot] nvarchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024164610_AddPilotCatalogAndOrderSnapshots'
)
BEGIN
    ALTER TABLE [OrderLines] ADD [UnitCode] nvarchar(16) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024164610_AddPilotCatalogAndOrderSnapshots'
)
BEGIN
    ALTER TABLE [Customers] ADD [HasStoreKey] bit NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024164610_AddPilotCatalogAndOrderSnapshots'
)
BEGIN
    ALTER TABLE [Customers] ADD [InternalDeliveryInstructions] nvarchar(1000) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024164610_AddPilotCatalogAndOrderSnapshots'
)
BEGIN
    ALTER TABLE [Customers] ADD [IsOrderProfileApproved] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024164610_AddPilotCatalogAndOrderSnapshots'
)
BEGIN
    ALTER TABLE [Customers] ADD [OrderingProfileVersion] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024164610_AddPilotCatalogAndOrderSnapshots'
)
BEGIN
    ALTER TABLE [Customers] ADD [PaymentTermCode] nvarchar(32) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024164610_AddPilotCatalogAndOrderSnapshots'
)
BEGIN
    ALTER TABLE [Customers] ADD [RouteCode] nvarchar(80) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024164610_AddPilotCatalogAndOrderSnapshots'
)
BEGIN
    ALTER TABLE [Customers] ADD [RouteStopOrder] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024164610_AddPilotCatalogAndOrderSnapshots'
)
BEGIN
    ALTER TABLE [CustomerFrequentProducts] ADD [InterpretationPosition] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024164610_AddPilotCatalogAndOrderSnapshots'
)
BEGIN
    ALTER TABLE [CustomerFrequentProducts] ADD [PreferredMachineId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024164610_AddPilotCatalogAndOrderSnapshots'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Orders_CustomerId_OrderDate_SequenceNumber] ON [Orders] ([CustomerId], [OrderDate], [SequenceNumber]) WHERE [CustomerId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024164610_AddPilotCatalogAndOrderSnapshots'
)
BEGIN
    CREATE INDEX [IX_Customers_RouteCode_RouteStopOrder] ON [Customers] ([RouteCode], [RouteStopOrder]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024164610_AddPilotCatalogAndOrderSnapshots'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_CustomerFrequentProducts_CustomerId_InterpretationPosition] ON [CustomerFrequentProducts] ([CustomerId], [InterpretationPosition]) WHERE [InterpretationPosition] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024164610_AddPilotCatalogAndOrderSnapshots'
)
BEGIN
    CREATE INDEX [IX_CustomerFrequentProducts_PreferredMachineId] ON [CustomerFrequentProducts] ([PreferredMachineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024164610_AddPilotCatalogAndOrderSnapshots'
)
BEGIN
    ALTER TABLE [CustomerFrequentProducts] ADD CONSTRAINT [FK_CustomerFrequentProducts_Machines_PreferredMachineId] FOREIGN KEY ([PreferredMachineId]) REFERENCES [Machines] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024164610_AddPilotCatalogAndOrderSnapshots'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261024164610_AddPilotCatalogAndOrderSnapshots', N'10.0.8');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024181206_AddDailyOrderOperations'
)
BEGIN
    ALTER TABLE [Orders] ADD [ExternalOrderKey] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024181206_AddDailyOrderOperations'
)
BEGIN
    ALTER TABLE [Orders] ADD [Source] nvarchar(80) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024181206_AddDailyOrderOperations'
)
BEGIN
    ALTER TABLE [Orders] ADD [SourcePayloadHash] nvarchar(64) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024181206_AddDailyOrderOperations'
)
BEGIN
    CREATE TABLE [OrderOperations] (
        [OperationKey] nvarchar(240) NOT NULL,
        [PayloadHash] nvarchar(64) NOT NULL,
        [ResultJson] nvarchar(max) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_OrderOperations] PRIMARY KEY ([OperationKey])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024181206_AddDailyOrderOperations'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Orders_Source_ExternalOrderKey] ON [Orders] ([Source], [ExternalOrderKey]) WHERE [Source] IS NOT NULL AND [ExternalOrderKey] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024181206_AddDailyOrderOperations'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261024181206_AddDailyOrderOperations', N'10.0.8');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024181934_ExpandOrderAuditSnapshots'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrderAuditLogs]') AND [c].[name] = N'MetadataJson');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [OrderAuditLogs] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [OrderAuditLogs] ALTER COLUMN [MetadataJson] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024181934_ExpandOrderAuditSnapshots'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261024181934_ExpandOrderAuditSnapshots', N'10.0.8');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024202253_AddOrderChangeNotices'
)
BEGIN
    CREATE TABLE [OrderChangeNotices] (
        [Id] uniqueidentifier NOT NULL,
        [OrderId] uniqueidentifier NOT NULL,
        [OrderVersion] int NOT NULL,
        [Version] int NOT NULL,
        [ServiceDate] date NOT NULL,
        [CustomerId] uniqueidentifier NULL,
        [CustomerName] nvarchar(200) NOT NULL,
        [RouteCode] nvarchar(80) NULL,
        [MachineId] uniqueidentifier NULL,
        [MachineNumber] int NULL,
        [MachineName] nvarchar(120) NULL,
        [Kind] nvarchar(20) NOT NULL,
        [Reason] nvarchar(1000) NOT NULL,
        [ActorId] nvarchar(200) NULL,
        [ActorDisplayName] nvarchar(200) NULL,
        [OccurredAt] datetimeoffset NOT NULL,
        [LinesJson] nvarchar(max) NOT NULL,
        [AcknowledgedAt] datetimeoffset NULL,
        [AcknowledgedById] nvarchar(200) NULL,
        [AcknowledgedBy] nvarchar(200) NULL,
        CONSTRAINT [PK_OrderChangeNotices] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrderChangeNotices_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024202253_AddOrderChangeNotices'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OrderChangeNotices_OrderId_OrderVersion_MachineId] ON [OrderChangeNotices] ([OrderId], [OrderVersion], [MachineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024202253_AddOrderChangeNotices'
)
BEGIN
    CREATE INDEX [IX_OrderChangeNotices_ServiceDate_MachineId_AcknowledgedAt] ON [OrderChangeNotices] ([ServiceDate], [MachineId], [AcknowledgedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024202253_AddOrderChangeNotices'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261024202253_AddOrderChangeNotices', N'10.0.8');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261028142346_AddCustomerRestDays'
)
BEGIN
    ALTER TABLE [Customers] ADD [RestDaysMask] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261028142346_AddCustomerRestDays'
)
BEGIN
    EXEC(N'ALTER TABLE [Customers] ADD CONSTRAINT [CK_Customers_RestDaysMask] CHECK ([RestDaysMask] BETWEEN 0 AND 127)');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261028142346_AddCustomerRestDays'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261028142346_AddCustomerRestDays', N'10.0.8');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261028202031_AddCustomerCaptureAliases'
)
BEGIN
    ALTER TABLE [CustomerFrequentProducts] ADD [CaptureAlias] nvarchar(16) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261028202031_AddCustomerCaptureAliases'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_CustomerFrequentProducts_CustomerId_CaptureAlias] ON [CustomerFrequentProducts] ([CustomerId], [CaptureAlias]) WHERE [CaptureAlias] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261028202031_AddCustomerCaptureAliases'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261028202031_AddCustomerCaptureAliases', N'10.0.8');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101154618_AddSharedProductsAndCustomerPrices'
)
BEGIN
    ALTER TABLE [Products] ADD [MergedIntoProductId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101154618_AddSharedProductsAndCustomerPrices'
)
BEGIN
    ALTER TABLE [OrderLines] ADD [CurrencyCodeSnapshot] nvarchar(3) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101154618_AddSharedProductsAndCustomerPrices'
)
BEGIN
    ALTER TABLE [OrderLines] ADD [PriceEffectiveFrom] date NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101154618_AddSharedProductsAndCustomerPrices'
)
BEGIN
    ALTER TABLE [OrderLines] ADD [PriceRevisionId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101154618_AddSharedProductsAndCustomerPrices'
)
BEGIN
    ALTER TABLE [OrderLines] ADD [UnitPriceSnapshot] decimal(18,4) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101154618_AddSharedProductsAndCustomerPrices'
)
BEGIN
    CREATE TABLE [CustomerProductPriceBooks] (
        [Id] uniqueidentifier NOT NULL,
        [CustomerId] uniqueidentifier NOT NULL,
        [ProductId] uniqueidentifier NOT NULL,
        [Version] int NOT NULL,
        CONSTRAINT [PK_CustomerProductPriceBooks] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CustomerProductPriceBooks_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_CustomerProductPriceBooks_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101154618_AddSharedProductsAndCustomerPrices'
)
BEGIN
    CREATE TABLE [ProductConsolidationOperations] (
        [Id] uniqueidentifier NOT NULL,
        [PayloadHash] nchar(64) NOT NULL,
        [CompletedAt] datetimeoffset NOT NULL,
        [ResultJson] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_ProductConsolidationOperations] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101154618_AddSharedProductsAndCustomerPrices'
)
BEGIN
    CREATE TABLE [CustomerProductPriceRevisions] (
        [Id] uniqueidentifier NOT NULL,
        [PriceBookId] uniqueidentifier NOT NULL,
        [Version] int NOT NULL,
        [UnitPrice] decimal(18,4) NULL,
        [CurrencyCode] nvarchar(3) NOT NULL,
        [UnitCode] nvarchar(16) NOT NULL,
        [ProductNameSnapshot] nvarchar(200) NOT NULL,
        [EffectiveFrom] date NOT NULL,
        [RecordedAt] datetimeoffset NOT NULL,
        [ActorId] nvarchar(200) NOT NULL,
        [ActorDisplayName] nvarchar(200) NULL,
        [Reason] nvarchar(500) NOT NULL,
        [OperationId] uniqueidentifier NOT NULL,
        [PayloadHash] nchar(64) NOT NULL,
        CONSTRAINT [PK_CustomerProductPriceRevisions] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_PriceRevision_Amount] CHECK ([UnitPrice] IS NULL OR ([UnitPrice] >= 0 AND [UnitPrice] <= 999999999.9999)),
        CONSTRAINT [FK_CustomerProductPriceRevisions_CustomerProductPriceBooks_PriceBookId] FOREIGN KEY ([PriceBookId]) REFERENCES [CustomerProductPriceBooks] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101154618_AddSharedProductsAndCustomerPrices'
)
BEGIN
    CREATE INDEX [IX_Products_MergedIntoProductId] ON [Products] ([MergedIntoProductId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101154618_AddSharedProductsAndCustomerPrices'
)
BEGIN
    EXEC(N'ALTER TABLE [Products] ADD CONSTRAINT [CK_Products_Consolidated] CHECK ([MergedIntoProductId] IS NULL OR ([MergedIntoProductId] <> [Id] AND [IsActive] = 0))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101154618_AddSharedProductsAndCustomerPrices'
)
BEGIN
    CREATE INDEX [IX_OrderLines_PriceRevisionId] ON [OrderLines] ([PriceRevisionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101154618_AddSharedProductsAndCustomerPrices'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CustomerProductPriceBooks_CustomerId_ProductId] ON [CustomerProductPriceBooks] ([CustomerId], [ProductId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101154618_AddSharedProductsAndCustomerPrices'
)
BEGIN
    CREATE INDEX [IX_CustomerProductPriceBooks_ProductId] ON [CustomerProductPriceBooks] ([ProductId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101154618_AddSharedProductsAndCustomerPrices'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CustomerProductPriceRevisions_OperationId] ON [CustomerProductPriceRevisions] ([OperationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101154618_AddSharedProductsAndCustomerPrices'
)
BEGIN
    CREATE INDEX [IX_CustomerProductPriceRevisions_PriceBookId_EffectiveFrom_Version] ON [CustomerProductPriceRevisions] ([PriceBookId], [EffectiveFrom] DESC, [Version] DESC);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101154618_AddSharedProductsAndCustomerPrices'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CustomerProductPriceRevisions_PriceBookId_Version] ON [CustomerProductPriceRevisions] ([PriceBookId], [Version]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101154618_AddSharedProductsAndCustomerPrices'
)
BEGIN
    ALTER TABLE [OrderLines] ADD CONSTRAINT [FK_OrderLines_CustomerProductPriceRevisions_PriceRevisionId] FOREIGN KEY ([PriceRevisionId]) REFERENCES [CustomerProductPriceRevisions] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101154618_AddSharedProductsAndCustomerPrices'
)
BEGIN
    ALTER TABLE [Products] ADD CONSTRAINT [FK_Products_Products_MergedIntoProductId] FOREIGN KEY ([MergedIntoProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101154618_AddSharedProductsAndCustomerPrices'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261101154618_AddSharedProductsAndCustomerPrices', N'10.0.8');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101190000_AddRecoverableCustomerAccessTokens'
)
BEGIN
    ALTER TABLE [CustomerAccessTokens] ADD [IsReminderLink] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101190000_AddRecoverableCustomerAccessTokens'
)
BEGIN
    ALTER TABLE [CustomerAccessTokens] ADD [MetadataVersion] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101190000_AddRecoverableCustomerAccessTokens'
)
BEGIN
    ALTER TABLE [CustomerAccessTokens] ADD [ProtectedToken] nvarchar(1024) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101190000_AddRecoverableCustomerAccessTokens'
)
BEGIN
    DECLARE @revoked TABLE (Id uniqueidentifier NOT NULL);
    DECLARE @revokedAt datetimeoffset = TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00');
    UPDATE dbo.CustomerAccessTokens
     SET IsActive=0, MetadataVersion=MetadataVersion+1
     OUTPUT inserted.Id INTO @revoked(Id)
     WHERE IsActive=1 AND ProtectedToken IS NULL;
    DECLARE @expected int = (SELECT COUNT(*) FROM @revoked);
    INSERT INTO dbo.AuditLogs
     (Id,EntityType,EntityId,EventType,OccurredAt,ActorType,ActorId,ActorDisplayName,Summary,MetadataJson)
     SELECT NEWID(),N'CustomerAccessToken',CONVERT(nvarchar(36),Id),
      N'CustomerAccessTokenRevokedForRecoveryUpgrade',@revokedAt,2,
      N'i12-token-upgrade',N'Mantenimiento PRODIMT',
      N'Acceso anterior revocado para habilitar gestión segura de tokens.',
      N'{"migration":"20261101190000_AddRecoverableCustomerAccessTokens","method":"revoke-legacy-accesses"}'
     FROM @revoked;
    IF @@ROWCOUNT<>@expected THROW 51031,'legacy_token_revocation_audit_mismatch',1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101190000_AddRecoverableCustomerAccessTokens'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261101190000_AddRecoverableCustomerAccessTokens', N'10.0.8');
END;

COMMIT;
GO

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

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    ALTER TABLE [AdminUsers] ADD [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    ALTER TABLE [AdminUsers] ADD [SessionRevision] uniqueidentifier NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    CREATE TABLE [SecurityPermissions] (
        [Id] uniqueidentifier NOT NULL,
        [Key] nvarchar(160) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Module] nvarchar(120) NOT NULL,
        [Description] nvarchar(500) NOT NULL,
        [IsActive] bit NOT NULL,
        [IsSystem] bit NOT NULL,
        CONSTRAINT [PK_SecurityPermissions] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    CREATE TABLE [SecurityRoles] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(120) NOT NULL,
        [Description] nvarchar(500) NOT NULL,
        [IsActive] bit NOT NULL,
        [IsSystem] bit NOT NULL,
        CONSTRAINT [PK_SecurityRoles] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    CREATE TABLE [SecurityStates] (
        [Id] int NOT NULL,
        [Revision] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_SecurityStates] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_SecurityState_Singleton] CHECK ([Id] = 1)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    CREATE TABLE [SecurityUserPermissions] (
        [UserId] uniqueidentifier NOT NULL,
        [PermissionId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_SecurityUserPermissions] PRIMARY KEY ([UserId], [PermissionId]),
        CONSTRAINT [FK_SecurityUserPermissions_AdminUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AdminUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SecurityUserPermissions_SecurityPermissions_PermissionId] FOREIGN KEY ([PermissionId]) REFERENCES [SecurityPermissions] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    CREATE TABLE [SecurityRolePermissions] (
        [RoleId] uniqueidentifier NOT NULL,
        [PermissionId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_SecurityRolePermissions] PRIMARY KEY ([RoleId], [PermissionId]),
        CONSTRAINT [FK_SecurityRolePermissions_SecurityPermissions_PermissionId] FOREIGN KEY ([PermissionId]) REFERENCES [SecurityPermissions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SecurityRolePermissions_SecurityRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [SecurityRoles] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    CREATE TABLE [SecurityUserRoles] (
        [UserId] uniqueidentifier NOT NULL,
        [RoleId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_SecurityUserRoles] PRIMARY KEY ([UserId], [RoleId]),
        CONSTRAINT [FK_SecurityUserRoles_AdminUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AdminUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SecurityUserRoles_SecurityRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [SecurityRoles] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SecurityPermissions_Key] ON [SecurityPermissions] ([Key]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    CREATE INDEX [IX_SecurityRolePermissions_PermissionId] ON [SecurityRolePermissions] ([PermissionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SecurityRoles_Name] ON [SecurityRoles] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    CREATE INDEX [IX_SecurityUserPermissions_PermissionId] ON [SecurityUserPermissions] ([PermissionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    CREATE INDEX [IX_SecurityUserRoles_RoleId] ON [SecurityUserRoles] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Revision') AND [object_id] = OBJECT_ID(N'[SecurityStates]'))
        SET IDENTITY_INSERT [SecurityStates] ON;
    EXEC(N'INSERT INTO [SecurityStates] ([Id], [Revision])
    VALUES (1, ''18180000-0000-0000-0000-000000000002'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Revision') AND [object_id] = OBJECT_ID(N'[SecurityStates]'))
        SET IDENTITY_INSERT [SecurityStates] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Name', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityRoles]'))
        SET IDENTITY_INSERT [SecurityRoles] ON;
    EXEC(N'INSERT INTO [SecurityRoles] ([Id], [Name], [Description], [IsActive], [IsSystem])
    VALUES (''18180000-0000-0000-0000-000000000001'', N''Administrador'', N''Administración del sistema'', CAST(1 AS bit), CAST(1 AS bit))');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Name', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityRoles]'))
        SET IDENTITY_INSERT [SecurityRoles] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'Module', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityPermissions]'))
        SET IDENTITY_INSERT [SecurityPermissions] ON;
    EXEC(N'INSERT INTO [SecurityPermissions] ([Id], [Key], [Name], [Module], [Description], [IsActive], [IsSystem])
    VALUES (''18180000-0000-0000-0001-000000000001'', N''orders.read'', N''Consultar pedidos'', N''Pedidos'', N''Consultar pedidos'', CAST(1 AS bit), CAST(1 AS bit))');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'Module', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityPermissions]'))
        SET IDENTITY_INSERT [SecurityPermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] ON;
    EXEC(N'INSERT INTO [SecurityRolePermissions] ([RoleId], [PermissionId])
    VALUES (''18180000-0000-0000-0000-000000000001'', ''18180000-0000-0000-0001-000000000001'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'Module', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityPermissions]'))
        SET IDENTITY_INSERT [SecurityPermissions] ON;
    EXEC(N'INSERT INTO [SecurityPermissions] ([Id], [Key], [Name], [Module], [Description], [IsActive], [IsSystem])
    VALUES (''18180000-0000-0000-0001-000000000002'', N''orders.capture'', N''Capturar y corregir pedidos'', N''Pedidos'', N''Capturar y corregir pedidos'', CAST(1 AS bit), CAST(1 AS bit))');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'Module', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityPermissions]'))
        SET IDENTITY_INSERT [SecurityPermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] ON;
    EXEC(N'INSERT INTO [SecurityRolePermissions] ([RoleId], [PermissionId])
    VALUES (''18180000-0000-0000-0000-000000000001'', ''18180000-0000-0000-0001-000000000002'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'Module', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityPermissions]'))
        SET IDENTITY_INSERT [SecurityPermissions] ON;
    EXEC(N'INSERT INTO [SecurityPermissions] ([Id], [Key], [Name], [Module], [Description], [IsActive], [IsSystem])
    VALUES (''18180000-0000-0000-0001-000000000003'', N''orders.review'', N''Revisar pedidos'', N''Pedidos'', N''Revisar pedidos'', CAST(1 AS bit), CAST(1 AS bit))');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'Module', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityPermissions]'))
        SET IDENTITY_INSERT [SecurityPermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] ON;
    EXEC(N'INSERT INTO [SecurityRolePermissions] ([RoleId], [PermissionId])
    VALUES (''18180000-0000-0000-0000-000000000001'', ''18180000-0000-0000-0001-000000000003'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'Module', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityPermissions]'))
        SET IDENTITY_INSERT [SecurityPermissions] ON;
    EXEC(N'INSERT INTO [SecurityPermissions] ([Id], [Key], [Name], [Module], [Description], [IsActive], [IsSystem])
    VALUES (''18180000-0000-0000-0001-000000000004'', N''customers.read'', N''Consultar clientes'', N''Clientes'', N''Consultar clientes'', CAST(1 AS bit), CAST(1 AS bit))');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'Module', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityPermissions]'))
        SET IDENTITY_INSERT [SecurityPermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] ON;
    EXEC(N'INSERT INTO [SecurityRolePermissions] ([RoleId], [PermissionId])
    VALUES (''18180000-0000-0000-0000-000000000001'', ''18180000-0000-0000-0001-000000000004'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'Module', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityPermissions]'))
        SET IDENTITY_INSERT [SecurityPermissions] ON;
    EXEC(N'INSERT INTO [SecurityPermissions] ([Id], [Key], [Name], [Module], [Description], [IsActive], [IsSystem])
    VALUES (''18180000-0000-0000-0001-000000000005'', N''customers.manage'', N''Administrar clientes y perfiles'', N''Clientes'', N''Administrar clientes y perfiles'', CAST(1 AS bit), CAST(1 AS bit))');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'Module', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityPermissions]'))
        SET IDENTITY_INSERT [SecurityPermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] ON;
    EXEC(N'INSERT INTO [SecurityRolePermissions] ([RoleId], [PermissionId])
    VALUES (''18180000-0000-0000-0000-000000000001'', ''18180000-0000-0000-0001-000000000005'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'Module', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityPermissions]'))
        SET IDENTITY_INSERT [SecurityPermissions] ON;
    EXEC(N'INSERT INTO [SecurityPermissions] ([Id], [Key], [Name], [Module], [Description], [IsActive], [IsSystem])
    VALUES (''18180000-0000-0000-0001-000000000006'', N''products.read'', N''Consultar productos'', N''Productos'', N''Consultar productos'', CAST(1 AS bit), CAST(1 AS bit))');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'Module', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityPermissions]'))
        SET IDENTITY_INSERT [SecurityPermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] ON;
    EXEC(N'INSERT INTO [SecurityRolePermissions] ([RoleId], [PermissionId])
    VALUES (''18180000-0000-0000-0000-000000000001'', ''18180000-0000-0000-0001-000000000006'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'Module', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityPermissions]'))
        SET IDENTITY_INSERT [SecurityPermissions] ON;
    EXEC(N'INSERT INTO [SecurityPermissions] ([Id], [Key], [Name], [Module], [Description], [IsActive], [IsSystem])
    VALUES (''18180000-0000-0000-0001-000000000007'', N''products.manage'', N''Administrar productos'', N''Productos'', N''Administrar productos'', CAST(1 AS bit), CAST(1 AS bit))');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'Module', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityPermissions]'))
        SET IDENTITY_INSERT [SecurityPermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] ON;
    EXEC(N'INSERT INTO [SecurityRolePermissions] ([RoleId], [PermissionId])
    VALUES (''18180000-0000-0000-0000-000000000001'', ''18180000-0000-0000-0001-000000000007'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'Module', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityPermissions]'))
        SET IDENTITY_INSERT [SecurityPermissions] ON;
    EXEC(N'INSERT INTO [SecurityPermissions] ([Id], [Key], [Name], [Module], [Description], [IsActive], [IsSystem])
    VALUES (''18180000-0000-0000-0001-000000000008'', N''machines.read'', N''Consultar máquinas'', N''Máquinas'', N''Consultar máquinas'', CAST(1 AS bit), CAST(1 AS bit))');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'Module', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityPermissions]'))
        SET IDENTITY_INSERT [SecurityPermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] ON;
    EXEC(N'INSERT INTO [SecurityRolePermissions] ([RoleId], [PermissionId])
    VALUES (''18180000-0000-0000-0000-000000000001'', ''18180000-0000-0000-0001-000000000008'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'Module', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityPermissions]'))
        SET IDENTITY_INSERT [SecurityPermissions] ON;
    EXEC(N'INSERT INTO [SecurityPermissions] ([Id], [Key], [Name], [Module], [Description], [IsActive], [IsSystem])
    VALUES (''18180000-0000-0000-0001-000000000009'', N''machines.manage'', N''Administrar máquinas'', N''Máquinas'', N''Administrar máquinas'', CAST(1 AS bit), CAST(1 AS bit))');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'Module', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityPermissions]'))
        SET IDENTITY_INSERT [SecurityPermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] ON;
    EXEC(N'INSERT INTO [SecurityRolePermissions] ([RoleId], [PermissionId])
    VALUES (''18180000-0000-0000-0000-000000000001'', ''18180000-0000-0000-0001-000000000009'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'Module', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityPermissions]'))
        SET IDENTITY_INSERT [SecurityPermissions] ON;
    EXEC(N'INSERT INTO [SecurityPermissions] ([Id], [Key], [Name], [Module], [Description], [IsActive], [IsSystem])
    VALUES (''18180000-0000-0000-0001-000000000010'', N''pricing.manage'', N''Administrar precios'', N''Precios'', N''Administrar precios'', CAST(1 AS bit), CAST(1 AS bit))');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'Module', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityPermissions]'))
        SET IDENTITY_INSERT [SecurityPermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] ON;
    EXEC(N'INSERT INTO [SecurityRolePermissions] ([RoleId], [PermissionId])
    VALUES (''18180000-0000-0000-0000-000000000001'', ''18180000-0000-0000-0001-000000000010'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'Module', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityPermissions]'))
        SET IDENTITY_INSERT [SecurityPermissions] ON;
    EXEC(N'INSERT INTO [SecurityPermissions] ([Id], [Key], [Name], [Module], [Description], [IsActive], [IsSystem])
    VALUES (''18180000-0000-0000-0001-000000000011'', N''tokens.manage'', N''Administrar accesos de clientes'', N''Accesos'', N''Administrar accesos de clientes'', CAST(1 AS bit), CAST(1 AS bit))');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'Module', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityPermissions]'))
        SET IDENTITY_INSERT [SecurityPermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] ON;
    EXEC(N'INSERT INTO [SecurityRolePermissions] ([RoleId], [PermissionId])
    VALUES (''18180000-0000-0000-0000-000000000001'', ''18180000-0000-0000-0001-000000000011'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'Module', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityPermissions]'))
        SET IDENTITY_INSERT [SecurityPermissions] ON;
    EXEC(N'INSERT INTO [SecurityPermissions] ([Id], [Key], [Name], [Module], [Description], [IsActive], [IsSystem])
    VALUES (''18180000-0000-0000-0001-000000000012'', N''imports.manage'', N''Importar catálogos y pedidos'', N''Importación'', N''Importar catálogos y pedidos'', CAST(1 AS bit), CAST(1 AS bit))');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'Module', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityPermissions]'))
        SET IDENTITY_INSERT [SecurityPermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] ON;
    EXEC(N'INSERT INTO [SecurityRolePermissions] ([RoleId], [PermissionId])
    VALUES (''18180000-0000-0000-0000-000000000001'', ''18180000-0000-0000-0001-000000000012'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'Module', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityPermissions]'))
        SET IDENTITY_INSERT [SecurityPermissions] ON;
    EXEC(N'INSERT INTO [SecurityPermissions] ([Id], [Key], [Name], [Module], [Description], [IsActive], [IsSystem])
    VALUES (''18180000-0000-0000-0001-000000000013'', N''operations.read'', N''Consultar operación y avisos'', N''Operación'', N''Consultar operación y avisos'', CAST(1 AS bit), CAST(1 AS bit))');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'Module', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityPermissions]'))
        SET IDENTITY_INSERT [SecurityPermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] ON;
    EXEC(N'INSERT INTO [SecurityRolePermissions] ([RoleId], [PermissionId])
    VALUES (''18180000-0000-0000-0000-000000000001'', ''18180000-0000-0000-0001-000000000013'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'Module', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityPermissions]'))
        SET IDENTITY_INSERT [SecurityPermissions] ON;
    EXEC(N'INSERT INTO [SecurityPermissions] ([Id], [Key], [Name], [Module], [Description], [IsActive], [IsSystem])
    VALUES (''18180000-0000-0000-0001-000000000014'', N''operations.acknowledge'', N''Reconocer avisos'', N''Operación'', N''Reconocer avisos'', CAST(1 AS bit), CAST(1 AS bit))');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'Module', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityPermissions]'))
        SET IDENTITY_INSERT [SecurityPermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] ON;
    EXEC(N'INSERT INTO [SecurityRolePermissions] ([RoleId], [PermissionId])
    VALUES (''18180000-0000-0000-0000-000000000001'', ''18180000-0000-0000-0001-000000000014'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'Module', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityPermissions]'))
        SET IDENTITY_INSERT [SecurityPermissions] ON;
    EXEC(N'INSERT INTO [SecurityPermissions] ([Id], [Key], [Name], [Module], [Description], [IsActive], [IsSystem])
    VALUES (''18180000-0000-0000-0001-000000000015'', N''security.manage'', N''Administrar usuarios, roles y permisos'', N''Seguridad'', N''Administrar usuarios, roles y permisos'', CAST(1 AS bit), CAST(1 AS bit))');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'Module', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityPermissions]'))
        SET IDENTITY_INSERT [SecurityPermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] ON;
    EXEC(N'INSERT INTO [SecurityRolePermissions] ([RoleId], [PermissionId])
    VALUES (''18180000-0000-0000-0000-000000000001'', ''18180000-0000-0000-0001-000000000015'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    INSERT INTO [SecurityUserRoles] ([UserId], [RoleId]) SELECT [Id], '18180000-0000-0000-0000-000000000001' FROM [AdminUsers];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102235000_AddSecurityAdministration'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261102235000_AddSecurityAdministration', N'10.0.8');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103010000_AddProductionAllocation'
)
BEGIN
    ALTER TABLE [Products] ADD [RequiresMachine] bit NOT NULL DEFAULT CAST(1 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103010000_AddProductionAllocation'
)
BEGIN
    ALTER TABLE [OrderLines] ADD [RequiresMachine] bit NOT NULL DEFAULT CAST(1 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103010000_AddProductionAllocation'
)
BEGIN
    ALTER TABLE [CustomerFrequentProducts] ADD [RequiresMachine] bit NOT NULL DEFAULT CAST(1 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103010000_AddProductionAllocation'
)
BEGIN
    CREATE TABLE [ProductionBatches] (
        [Id] uniqueidentifier NOT NULL,
        [ServiceDate] date NOT NULL,
        [MachineId] uniqueidentifier NOT NULL,
        [ProductId] uniqueidentifier NOT NULL,
        [ProductNameSnapshot] nvarchar(200) NOT NULL,
        [UnitCode] nvarchar(16) NOT NULL,
        [Quantity] decimal(18,2) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(120) NULL,
        [Notes] nvarchar(500) NULL,
        CONSTRAINT [PK_ProductionBatches] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ProductionBatch_Quantity] CHECK ([Quantity] > 0),
        CONSTRAINT [CK_ProductionBatch_Unit] CHECK ([UnitCode] = 'kg'),
        CONSTRAINT [FK_ProductionBatches_Machines_MachineId] FOREIGN KEY ([MachineId]) REFERENCES [Machines] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ProductionBatches_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103010000_AddProductionAllocation'
)
BEGIN
    CREATE TABLE [ProductionAllocations] (
        [Id] uniqueidentifier NOT NULL,
        [BatchId] uniqueidentifier NOT NULL,
        [OrderId] uniqueidentifier NOT NULL,
        [OrderLineId] uniqueidentifier NOT NULL,
        [CustomerNameSnapshot] nvarchar(200) NOT NULL,
        [Quantity] decimal(18,2) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(120) NULL,
        [ReversedAt] datetimeoffset NULL,
        [ReversedBy] nvarchar(120) NULL,
        [ReverseReason] nvarchar(500) NULL,
        CONSTRAINT [PK_ProductionAllocations] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ProductionAllocation_Quantity] CHECK ([Quantity] > 0),
        CONSTRAINT [CK_ProductionAllocation_Reversal] CHECK (([ReversedAt] IS NULL AND [ReversedBy] IS NULL AND [ReverseReason] IS NULL) OR ([ReversedAt] IS NOT NULL AND [ReverseReason] IS NOT NULL)),
        CONSTRAINT [FK_ProductionAllocations_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ProductionAllocations_ProductionBatches_BatchId] FOREIGN KEY ([BatchId]) REFERENCES [ProductionBatches] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103010000_AddProductionAllocation'
)
BEGIN
    CREATE INDEX [IX_ProductionAllocations_BatchId_ReversedAt] ON [ProductionAllocations] ([BatchId], [ReversedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103010000_AddProductionAllocation'
)
BEGIN
    CREATE INDEX [IX_ProductionAllocations_OrderId_OrderLineId_ReversedAt] ON [ProductionAllocations] ([OrderId], [OrderLineId], [ReversedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103010000_AddProductionAllocation'
)
BEGIN
    CREATE INDEX [IX_ProductionBatches_MachineId] ON [ProductionBatches] ([MachineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103010000_AddProductionAllocation'
)
BEGIN
    CREATE INDEX [IX_ProductionBatches_ProductId] ON [ProductionBatches] ([ProductId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103010000_AddProductionAllocation'
)
BEGIN
    CREATE INDEX [IX_ProductionBatches_ServiceDate_MachineId_ProductId] ON [ProductionBatches] ([ServiceDate], [MachineId], [ProductId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103010000_AddProductionAllocation'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'Module', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityPermissions]'))
        SET IDENTITY_INSERT [SecurityPermissions] ON;
    EXEC(N'INSERT INTO [SecurityPermissions] ([Id], [Key], [Name], [Module], [Description], [IsActive], [IsSystem])
    VALUES (''18180000-0000-0000-0001-000000000016'', N''operations.assign'', N''Asignar máquinas a pedidos'', N''Operación'', N''Asignar máquinas a pedidos'', CAST(1 AS bit), CAST(1 AS bit))');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'Module', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityPermissions]'))
        SET IDENTITY_INSERT [SecurityPermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103010000_AddProductionAllocation'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] ON;
    EXEC(N'INSERT INTO [SecurityRolePermissions] ([RoleId], [PermissionId])
    VALUES (''18180000-0000-0000-0000-000000000001'', ''18180000-0000-0000-0001-000000000016'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103010000_AddProductionAllocation'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'Module', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityPermissions]'))
        SET IDENTITY_INSERT [SecurityPermissions] ON;
    EXEC(N'INSERT INTO [SecurityPermissions] ([Id], [Key], [Name], [Module], [Description], [IsActive], [IsSystem])
    VALUES (''18180000-0000-0000-0001-000000000017'', N''operations.produce'', N''Registrar kilos producidos'', N''Operación'', N''Registrar kilos producidos'', CAST(1 AS bit), CAST(1 AS bit))');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'Module', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityPermissions]'))
        SET IDENTITY_INSERT [SecurityPermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103010000_AddProductionAllocation'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] ON;
    EXEC(N'INSERT INTO [SecurityRolePermissions] ([RoleId], [PermissionId])
    VALUES (''18180000-0000-0000-0000-000000000001'', ''18180000-0000-0000-0001-000000000017'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103010000_AddProductionAllocation'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'Module', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityPermissions]'))
        SET IDENTITY_INSERT [SecurityPermissions] ON;
    EXEC(N'INSERT INTO [SecurityPermissions] ([Id], [Key], [Name], [Module], [Description], [IsActive], [IsSystem])
    VALUES (''18180000-0000-0000-0001-000000000018'', N''operations.allocate'', N''Asignar producción a pedidos'', N''Operación'', N''Asignar producción a pedidos'', CAST(1 AS bit), CAST(1 AS bit))');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'Module', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityPermissions]'))
        SET IDENTITY_INSERT [SecurityPermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103010000_AddProductionAllocation'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] ON;
    EXEC(N'INSERT INTO [SecurityRolePermissions] ([RoleId], [PermissionId])
    VALUES (''18180000-0000-0000-0000-000000000001'', ''18180000-0000-0000-0001-000000000018'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103010000_AddProductionAllocation'
)
BEGIN
    UPDATE [SecurityStates] SET [Revision] = NEWID() WHERE [Id] = 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103010000_AddProductionAllocation'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261103010000_AddProductionAllocation', N'10.0.8');
END;

COMMIT;
GO

