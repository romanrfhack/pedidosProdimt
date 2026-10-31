using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prodimt.Pedidos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerCaptureAliases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CaptureAlias",
                table: "CustomerFrequentProducts",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerFrequentProducts_CustomerId_CaptureAlias",
                table: "CustomerFrequentProducts",
                columns: new[] { "CustomerId", "CaptureAlias" },
                unique: true,
                filter: "[CaptureAlias] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CustomerFrequentProducts_CustomerId_CaptureAlias",
                table: "CustomerFrequentProducts");

            migrationBuilder.DropColumn(
                name: "CaptureAlias",
                table: "CustomerFrequentProducts");
        }
    }
}
