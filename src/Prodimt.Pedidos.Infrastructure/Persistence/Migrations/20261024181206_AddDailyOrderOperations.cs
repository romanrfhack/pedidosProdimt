using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prodimt.Pedidos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyOrderOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalOrderKey",
                table: "Orders",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Orders",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourcePayloadHash",
                table: "Orders",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OrderOperations",
                columns: table => new
                {
                    OperationKey = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: false),
                    PayloadHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderOperations", x => x.OperationKey);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Source_ExternalOrderKey",
                table: "Orders",
                columns: new[] { "Source", "ExternalOrderKey" },
                unique: true,
                filter: "[Source] IS NOT NULL AND [ExternalOrderKey] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderOperations");

            migrationBuilder.DropIndex(
                name: "IX_Orders_Source_ExternalOrderKey",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ExternalOrderKey",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "SourcePayloadHash",
                table: "Orders");
        }
    }
}
