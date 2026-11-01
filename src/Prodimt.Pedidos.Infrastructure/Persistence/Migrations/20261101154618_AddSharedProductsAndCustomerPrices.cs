using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prodimt.Pedidos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedProductsAndCustomerPrices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MergedIntoProductId",
                table: "Products",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCodeSnapshot",
                table: "OrderLines",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PriceEffectiveFrom",
                table: "OrderLines",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PriceRevisionId",
                table: "OrderLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitPriceSnapshot",
                table: "OrderLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CustomerProductPriceBooks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerProductPriceBooks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerProductPriceBooks_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerProductPriceBooks_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductConsolidationOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayloadHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductConsolidationOperations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomerProductPriceRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PriceBookId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    CurrencyCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    UnitCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ProductNameSnapshot = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ActorDisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayloadHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerProductPriceRevisions", x => x.Id);
                    table.CheckConstraint("CK_PriceRevision_Amount", "[UnitPrice] IS NULL OR ([UnitPrice] >= 0 AND [UnitPrice] <= 999999999.9999)");
                    table.ForeignKey(
                        name: "FK_CustomerProductPriceRevisions_CustomerProductPriceBooks_PriceBookId",
                        column: x => x.PriceBookId,
                        principalTable: "CustomerProductPriceBooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Products_MergedIntoProductId",
                table: "Products",
                column: "MergedIntoProductId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Products_Consolidated",
                table: "Products",
                sql: "[MergedIntoProductId] IS NULL OR ([MergedIntoProductId] <> [Id] AND [IsActive] = 0)");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLines_PriceRevisionId",
                table: "OrderLines",
                column: "PriceRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerProductPriceBooks_CustomerId_ProductId",
                table: "CustomerProductPriceBooks",
                columns: new[] { "CustomerId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerProductPriceBooks_ProductId",
                table: "CustomerProductPriceBooks",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerProductPriceRevisions_OperationId",
                table: "CustomerProductPriceRevisions",
                column: "OperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerProductPriceRevisions_PriceBookId_EffectiveFrom_Version",
                table: "CustomerProductPriceRevisions",
                columns: new[] { "PriceBookId", "EffectiveFrom", "Version" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerProductPriceRevisions_PriceBookId_Version",
                table: "CustomerProductPriceRevisions",
                columns: new[] { "PriceBookId", "Version" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderLines_CustomerProductPriceRevisions_PriceRevisionId",
                table: "OrderLines",
                column: "PriceRevisionId",
                principalTable: "CustomerProductPriceRevisions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Products_MergedIntoProductId",
                table: "Products",
                column: "MergedIntoProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderLines_CustomerProductPriceRevisions_PriceRevisionId",
                table: "OrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_Products_Products_MergedIntoProductId",
                table: "Products");

            migrationBuilder.DropTable(
                name: "CustomerProductPriceRevisions");

            migrationBuilder.DropTable(
                name: "ProductConsolidationOperations");

            migrationBuilder.DropTable(
                name: "CustomerProductPriceBooks");

            migrationBuilder.DropIndex(
                name: "IX_Products_MergedIntoProductId",
                table: "Products");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_Consolidated",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_OrderLines_PriceRevisionId",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "MergedIntoProductId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "CurrencyCodeSnapshot",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "PriceEffectiveFrom",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "PriceRevisionId",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "UnitPriceSnapshot",
                table: "OrderLines");
        }
    }
}
