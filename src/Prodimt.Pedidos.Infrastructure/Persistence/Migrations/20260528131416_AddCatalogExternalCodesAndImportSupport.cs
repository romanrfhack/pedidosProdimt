using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prodimt.Pedidos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogExternalCodesAndImportSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalCode",
                table: "Products",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalCode",
                table: "Machines",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalCode",
                table: "Customers",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_ExternalCode",
                table: "Products",
                column: "ExternalCode",
                unique: true,
                filter: "[ExternalCode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Machines_ExternalCode",
                table: "Machines",
                column: "ExternalCode",
                unique: true,
                filter: "[ExternalCode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_ExternalCode",
                table: "Customers",
                column: "ExternalCode",
                unique: true,
                filter: "[ExternalCode] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_ExternalCode",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Machines_ExternalCode",
                table: "Machines");

            migrationBuilder.DropIndex(
                name: "IX_Customers_ExternalCode",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "ExternalCode",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ExternalCode",
                table: "Machines");

            migrationBuilder.DropColumn(
                name: "ExternalCode",
                table: "Customers");
        }
    }
}
