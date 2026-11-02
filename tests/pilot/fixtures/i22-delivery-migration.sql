BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103020000_AddDeliveryOperations'
)
BEGIN
    CREATE TABLE [CustomerDeliveryLocations] (
        [CustomerId] uniqueidentifier NOT NULL,
        [MapsUrl] nvarchar(2048) NULL,
        [Address] nvarchar(500) NULL,
        [Version] int NOT NULL,
        CONSTRAINT [PK_CustomerDeliveryLocations] PRIMARY KEY ([CustomerId]),
        CONSTRAINT [FK_CustomerDeliveryLocations_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103020000_AddDeliveryOperations'
)
BEGIN
    CREATE TABLE [DeliveryRoutes] (
        [Code] nvarchar(80) NOT NULL,
        [Name] nvarchar(160) NOT NULL,
        [DefaultDriverUserId] uniqueidentifier NULL,
        [IsActive] bit NOT NULL,
        [Version] int NOT NULL,
        CONSTRAINT [PK_DeliveryRoutes] PRIMARY KEY ([Code]),
        CONSTRAINT [FK_DeliveryRoutes_AdminUsers_DefaultDriverUserId] FOREIGN KEY ([DefaultDriverUserId]) REFERENCES [AdminUsers] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103020000_AddDeliveryOperations'
)
BEGIN
    CREATE TABLE [DeliveryTrips] (
        [Id] uniqueidentifier NOT NULL,
        [Date] date NOT NULL,
        [RouteCode] nvarchar(80) NOT NULL,
        [RouteName] nvarchar(160) NOT NULL,
        [TripNumber] int NOT NULL,
        [DriverUserId] uniqueidentifier NOT NULL,
        [DriverName] nvarchar(160) NOT NULL,
        [Status] nvarchar(16) NOT NULL,
        [Version] int NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [ClosedAt] datetimeoffset NULL,
        CONSTRAINT [PK_DeliveryTrips] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_DeliveryTrips_Number] CHECK ([TripNumber] >= 1 AND [TripNumber] <= 999),
        CONSTRAINT [CK_DeliveryTrips_Status] CHECK ([Status] IN ('Open', 'Closed')),
        CONSTRAINT [FK_DeliveryTrips_AdminUsers_DriverUserId] FOREIGN KEY ([DriverUserId]) REFERENCES [AdminUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_DeliveryTrips_DeliveryRoutes_RouteCode] FOREIGN KEY ([RouteCode]) REFERENCES [DeliveryRoutes] ([Code]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103020000_AddDeliveryOperations'
)
BEGIN
    CREATE TABLE [DeliveryAssignments] (
        [Id] uniqueidentifier NOT NULL,
        [TripId] uniqueidentifier NOT NULL,
        [OrderId] uniqueidentifier NOT NULL,
        [OrderLineId] uniqueidentifier NOT NULL,
        [ProductId] uniqueidentifier NOT NULL,
        [CustomerId] uniqueidentifier NOT NULL,
        [CustomerName] nvarchar(200) NOT NULL,
        [SequenceNumber] int NOT NULL,
        [RouteStopOrder] int NULL,
        [ProductName] nvarchar(160) NOT NULL,
        [UnitCode] nvarchar(20) NOT NULL,
        [RequestedQuantity] decimal(18,3) NOT NULL,
        [AssignedQuantity] decimal(18,3) NOT NULL,
        [ReleasedQuantity] decimal(18,3) NOT NULL,
        [CountedQuantity] decimal(18,3) NULL,
        [LoadedQuantity] decimal(18,3) NULL,
        [DeliveredQuantity] decimal(18,3) NOT NULL,
        [ReturnedQuantity] decimal(18,3) NOT NULL,
        [TransferredOutQuantity] decimal(18,3) NOT NULL,
        [TransferredInQuantity] decimal(18,3) NOT NULL,
        [HasUnresolvedIncident] bit NOT NULL,
        [AcknowledgedOrderVersion] int NOT NULL,
        [Version] int NOT NULL,
        [MapsUrl] nvarchar(2048) NULL,
        [Address] nvarchar(500) NULL,
        [RequestedDeliveryTime] time NULL,
        [RequestedDeliveryWindowStart] time NULL,
        [RequestedDeliveryWindowEnd] time NULL,
        [HasStoreKey] bit NULL,
        [PaymentTermCode] nvarchar(40) NULL,
        [DeliveryNotes] nvarchar(2000) NULL,
        [InternalDeliveryInstructions] nvarchar(2000) NULL,
        CONSTRAINT [PK_DeliveryAssignments] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_DeliveryAssignments_Commitment] CHECK ([AssignedQuantity] - [ReleasedQuantity] >= COALESCE([LoadedQuantity], 0) + [TransferredInQuantity] - [ReturnedQuantity] - [TransferredOutQuantity]),
        CONSTRAINT [CK_DeliveryAssignments_Custody] CHECK (COALESCE([LoadedQuantity], 0) + [TransferredInQuantity] - [DeliveredQuantity] - [ReturnedQuantity] - [TransferredOutQuantity] >= 0),
        CONSTRAINT [CK_DeliveryAssignments_Quantities] CHECK ([AssignedQuantity] > 0 AND [ReleasedQuantity] >= 0 AND [ReleasedQuantity] <= [AssignedQuantity] AND ([CountedQuantity] IS NULL OR [CountedQuantity] >= 0) AND ([LoadedQuantity] IS NULL OR [LoadedQuantity] >= 0) AND [DeliveredQuantity] >= 0 AND [ReturnedQuantity] >= 0 AND [TransferredOutQuantity] >= 0 AND [TransferredInQuantity] >= 0),
        CONSTRAINT [FK_DeliveryAssignments_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_DeliveryAssignments_DeliveryTrips_TripId] FOREIGN KEY ([TripId]) REFERENCES [DeliveryTrips] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_DeliveryAssignments_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103020000_AddDeliveryOperations'
)
BEGIN
    CREATE TABLE [DeliveryEvents] (
        [Id] uniqueidentifier NOT NULL,
        [TripId] uniqueidentifier NOT NULL,
        [AssignmentId] uniqueidentifier NULL,
        [Type] nvarchar(80) NOT NULL,
        [Quantity] decimal(18,3) NULL,
        [PreviousQuantity] decimal(18,3) NULL,
        [Reason] nvarchar(500) NULL,
        [Notes] nvarchar(2000) NULL,
        [ActorId] nvarchar(100) NULL,
        [ActorName] nvarchar(160) NULL,
        [OccurredAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_DeliveryEvents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DeliveryEvents_DeliveryAssignments_AssignmentId] FOREIGN KEY ([AssignmentId]) REFERENCES [DeliveryAssignments] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_DeliveryEvents_DeliveryTrips_TripId] FOREIGN KEY ([TripId]) REFERENCES [DeliveryTrips] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103020000_AddDeliveryOperations'
)
BEGIN
    CREATE INDEX [IX_DeliveryAssignments_CustomerId] ON [DeliveryAssignments] ([CustomerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103020000_AddDeliveryOperations'
)
BEGIN
    CREATE INDEX [IX_DeliveryAssignments_OrderId_OrderLineId] ON [DeliveryAssignments] ([OrderId], [OrderLineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103020000_AddDeliveryOperations'
)
BEGIN
    CREATE INDEX [IX_DeliveryAssignments_TripId] ON [DeliveryAssignments] ([TripId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103020000_AddDeliveryOperations'
)
BEGIN
    CREATE INDEX [IX_DeliveryEvents_AssignmentId] ON [DeliveryEvents] ([AssignmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103020000_AddDeliveryOperations'
)
BEGIN
    CREATE INDEX [IX_DeliveryEvents_TripId_OccurredAt] ON [DeliveryEvents] ([TripId], [OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103020000_AddDeliveryOperations'
)
BEGIN
    CREATE INDEX [IX_DeliveryRoutes_DefaultDriverUserId] ON [DeliveryRoutes] ([DefaultDriverUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103020000_AddDeliveryOperations'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DeliveryTrips_Date_RouteCode_TripNumber] ON [DeliveryTrips] ([Date], [RouteCode], [TripNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103020000_AddDeliveryOperations'
)
BEGIN
    CREATE INDEX [IX_DeliveryTrips_DriverUserId_Date] ON [DeliveryTrips] ([DriverUserId], [Date]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103020000_AddDeliveryOperations'
)
BEGIN
    CREATE INDEX [IX_DeliveryTrips_RouteCode] ON [DeliveryTrips] ([RouteCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103020000_AddDeliveryOperations'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Name', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityRoles]'))
        SET IDENTITY_INSERT [SecurityRoles] ON;
    EXEC(N'INSERT INTO [SecurityRoles] ([Id], [Name], [Description], [IsActive], [IsSystem])
    VALUES (''22220000-0000-0000-0000-000000000001'', N''Repartidor - Camioneta'', N''Consulta y operación de sus vueltas asignadas'', CAST(1 AS bit), CAST(1 AS bit))');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Name', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityRoles]'))
        SET IDENTITY_INSERT [SecurityRoles] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103020000_AddDeliveryOperations'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'Module', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityPermissions]'))
        SET IDENTITY_INSERT [SecurityPermissions] ON;
    EXEC(N'INSERT INTO [SecurityPermissions] ([Id], [Key], [Name], [Module], [Description], [IsActive], [IsSystem])
    VALUES (''18180000-0000-0000-0001-000000000019'', N''delivery.read.own'', N''Consultar mi reparto'', N''Reparto'', N''Consultar mi reparto'', CAST(1 AS bit), CAST(1 AS bit))');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'Module', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityPermissions]'))
        SET IDENTITY_INSERT [SecurityPermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103020000_AddDeliveryOperations'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] ON;
    EXEC(N'INSERT INTO [SecurityRolePermissions] ([RoleId], [PermissionId])
    VALUES (''18180000-0000-0000-0000-000000000001'', ''18180000-0000-0000-0001-000000000019'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103020000_AddDeliveryOperations'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] ON;
    EXEC(N'INSERT INTO [SecurityRolePermissions] ([RoleId], [PermissionId])
    VALUES (''22220000-0000-0000-0000-000000000001'', ''18180000-0000-0000-0001-000000000019'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103020000_AddDeliveryOperations'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'Module', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityPermissions]'))
        SET IDENTITY_INSERT [SecurityPermissions] ON;
    EXEC(N'INSERT INTO [SecurityPermissions] ([Id], [Key], [Name], [Module], [Description], [IsActive], [IsSystem])
    VALUES (''18180000-0000-0000-0001-000000000020'', N''delivery.operate.own'', N''Registrar mi conteo, carga y entregas'', N''Reparto'', N''Registrar mi conteo, carga y entregas'', CAST(1 AS bit), CAST(1 AS bit))');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'Module', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityPermissions]'))
        SET IDENTITY_INSERT [SecurityPermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103020000_AddDeliveryOperations'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] ON;
    EXEC(N'INSERT INTO [SecurityRolePermissions] ([RoleId], [PermissionId])
    VALUES (''18180000-0000-0000-0000-000000000001'', ''18180000-0000-0000-0001-000000000020'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103020000_AddDeliveryOperations'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] ON;
    EXEC(N'INSERT INTO [SecurityRolePermissions] ([RoleId], [PermissionId])
    VALUES (''22220000-0000-0000-0000-000000000001'', ''18180000-0000-0000-0001-000000000020'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103020000_AddDeliveryOperations'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'Module', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityPermissions]'))
        SET IDENTITY_INSERT [SecurityPermissions] ON;
    EXEC(N'INSERT INTO [SecurityPermissions] ([Id], [Key], [Name], [Module], [Description], [IsActive], [IsSystem])
    VALUES (''18180000-0000-0000-0001-000000000021'', N''delivery.manage'', N''Administrar rutas y reparto'', N''Reparto'', N''Administrar rutas y reparto'', CAST(1 AS bit), CAST(1 AS bit))');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Key', N'Name', N'Module', N'Description', N'IsActive', N'IsSystem') AND [object_id] = OBJECT_ID(N'[SecurityPermissions]'))
        SET IDENTITY_INSERT [SecurityPermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103020000_AddDeliveryOperations'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] ON;
    EXEC(N'INSERT INTO [SecurityRolePermissions] ([RoleId], [PermissionId])
    VALUES (''18180000-0000-0000-0000-000000000001'', ''18180000-0000-0000-0001-000000000021'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'PermissionId') AND [object_id] = OBJECT_ID(N'[SecurityRolePermissions]'))
        SET IDENTITY_INSERT [SecurityRolePermissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103020000_AddDeliveryOperations'
)
BEGIN
    UPDATE [SecurityStates] SET [Revision] = NEWID() WHERE [Id] = 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103020000_AddDeliveryOperations'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261103020000_AddDeliveryOperations', N'10.0.8');
END;

COMMIT;
GO

