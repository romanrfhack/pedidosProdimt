using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prodimt.Pedidos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecoverableCustomerAccessTokens : Migration
    {
        // All rows that predate this migration have ProtectedToken=NULL. The same
        // predicate is safe for a backed-up retry after an application rollback
        // created additional legacy hash-only accesses on the retained schema.
        public const string RevokeLegacySql = """
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
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsReminderLink",
                table: "CustomerAccessTokens",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MetadataVersion",
                table: "CustomerAccessTokens",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "ProtectedToken",
                table: "CustomerAccessTokens",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: true);

            // EF runs this migration, the schema additions and the history write
            // in one transaction. Audit failure must also roll back revocation.
            migrationBuilder.Sql(RevokeLegacySql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A schema downgrade never reactivates accesses the user revoked.
            migrationBuilder.DropColumn(
                name: "IsReminderLink",
                table: "CustomerAccessTokens");

            migrationBuilder.DropColumn(
                name: "MetadataVersion",
                table: "CustomerAccessTokens");

            migrationBuilder.DropColumn(
                name: "ProtectedToken",
                table: "CustomerAccessTokens");
        }
    }
}
