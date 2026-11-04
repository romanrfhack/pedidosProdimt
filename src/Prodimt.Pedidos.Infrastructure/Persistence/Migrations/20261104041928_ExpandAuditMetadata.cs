using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prodimt.Pedidos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExpandAuditMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "MetadataJson",
                table: "AuditLogs",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(2000)",
                oldMaxLength: 2000,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Downgrade must never truncate historical evidence. The application
            // can roll back while this backward-compatible wider column remains.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [AuditLogs] WHERE DATALENGTH([MetadataJson]) > 4000)
                    THROW 51024, 'Audit metadata exceeds the previous column capacity; retain the expanded schema.', 1;
                """);
            migrationBuilder.AlterColumn<string>(
                name: "MetadataJson",
                table: "AuditLogs",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);
        }
    }
}
