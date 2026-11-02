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

