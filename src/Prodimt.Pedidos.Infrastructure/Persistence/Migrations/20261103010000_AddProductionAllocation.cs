using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prodimt.Pedidos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionAllocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequiresMachine",
                table: "Products",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresMachine",
                table: "OrderLines",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresMachine",
                table: "CustomerFrequentProducts",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "ProductionBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    MachineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductNameSnapshot = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UnitCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionBatches", x => x.Id);
                    table.CheckConstraint("CK_ProductionBatch_Quantity", "[Quantity] > 0");
                    table.CheckConstraint("CK_ProductionBatch_Unit", "[UnitCode] = 'kg'");
                    table.ForeignKey(
                        name: "FK_ProductionBatches_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionBatches_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductionAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerNameSnapshot = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    ReversedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReversedBy = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    ReverseReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionAllocations", x => x.Id);
                    table.CheckConstraint("CK_ProductionAllocation_Quantity", "[Quantity] > 0");
                    table.CheckConstraint("CK_ProductionAllocation_Reversal", "([ReversedAt] IS NULL AND [ReversedBy] IS NULL AND [ReverseReason] IS NULL) OR ([ReversedAt] IS NOT NULL AND [ReverseReason] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_ProductionAllocations_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionAllocations_ProductionBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "ProductionBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionAllocations_BatchId_ReversedAt",
                table: "ProductionAllocations",
                columns: new[] { "BatchId", "ReversedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionAllocations_OrderId_OrderLineId_ReversedAt",
                table: "ProductionAllocations",
                columns: new[] { "OrderId", "OrderLineId", "ReversedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionBatches_MachineId",
                table: "ProductionBatches",
                column: "MachineId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionBatches_ProductId",
                table: "ProductionBatches",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionBatches_ServiceDate_MachineId_ProductId",
                table: "ProductionBatches",
                columns: new[] { "ServiceDate", "MachineId", "ProductId" });

            // Frozen I19 grants: only the protected system role receives new actions.
            // Existing custom roles and direct grants remain unchanged on upgrade/retry.
            var administrator = new Guid("18180000-0000-0000-0000-000000000001");
            var permissions = new[]
            {
                (Id: new Guid("18180000-0000-0000-0001-000000000016"), Key: "operations.assign", Name: "Asignar máquinas a pedidos"),
                (Id: new Guid("18180000-0000-0000-0001-000000000017"), Key: "operations.produce", Name: "Registrar kilos producidos"),
                (Id: new Guid("18180000-0000-0000-0001-000000000018"), Key: "operations.allocate", Name: "Asignar producción a pedidos")
            };
            foreach (var permission in permissions)
            {
                migrationBuilder.InsertData("SecurityPermissions",
                    new[] { "Id", "Key", "Name", "Module", "Description", "IsActive", "IsSystem" },
                    new object[] { permission.Id, permission.Key, permission.Name, "Operación", permission.Name, true, true });
                migrationBuilder.InsertData("SecurityRolePermissions", new[] { "RoleId", "PermissionId" },
                    new object[] { administrator, permission.Id });
            }
            migrationBuilder.Sql("UPDATE [SecurityStates] SET [Revision] = NEWID() WHERE [Id] = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM [SecurityUserPermissions] WHERE [PermissionId] IN ('18180000-0000-0000-0001-000000000016','18180000-0000-0000-0001-000000000017','18180000-0000-0000-0001-000000000018');");
            migrationBuilder.Sql("DELETE FROM [SecurityRolePermissions] WHERE [PermissionId] IN ('18180000-0000-0000-0001-000000000016','18180000-0000-0000-0001-000000000017','18180000-0000-0000-0001-000000000018');");
            migrationBuilder.Sql("DELETE FROM [SecurityPermissions] WHERE [Id] IN ('18180000-0000-0000-0001-000000000016','18180000-0000-0000-0001-000000000017','18180000-0000-0000-0001-000000000018');");
            migrationBuilder.Sql("UPDATE [SecurityStates] SET [Revision] = NEWID() WHERE [Id] = 1;");
            migrationBuilder.DropTable(
                name: "ProductionAllocations");

            migrationBuilder.DropTable(
                name: "ProductionBatches");

            migrationBuilder.DropColumn(
                name: "RequiresMachine",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "RequiresMachine",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "RequiresMachine",
                table: "CustomerFrequentProducts");
        }
    }
}
