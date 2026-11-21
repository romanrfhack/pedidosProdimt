using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prodimt.Pedidos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMachineWorkspace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ReleasedQuantity",
                table: "ProductionAllocations",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "MachineOperators",
                columns: table => new
                {
                    MachineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MachineOperators", x => new { x.MachineId, x.UserId });
                    table.ForeignKey(
                        name: "FK_MachineOperators_AdminUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AdminUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MachineOperators_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MachineProductionNotices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MachineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MachineProductionNotices", x => x.Id);
                    table.CheckConstraint("CK_MachineProductionNotice_Quantity", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_MachineProductionNotices_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MachineProductionNotices_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MachineWorkers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MachineWorkers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductionLotHandoffs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionLotHandoffs", x => x.Id);
                    table.CheckConstraint("CK_ProductionLotHandoff_Quantity", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_ProductionLotHandoffs_ProductionBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "ProductionBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MachineTeamChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MachineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    WorkerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PreviousWorkerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OtherMachineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OtherPosition = table.Column<int>(type: "int", nullable: true),
                    TransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ActorName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MachineTeamChanges", x => x.Id);
                    table.CheckConstraint("CK_MachineTeamChanges_OtherPosition", "[OtherPosition] IS NULL OR [OtherPosition] BETWEEN 1 AND 4");
                    table.CheckConstraint("CK_MachineTeamChanges_Position", "[Position] BETWEEN 1 AND 4");
                    table.ForeignKey(
                        name: "FK_MachineTeamChanges_MachineWorkers_PreviousWorkerId",
                        column: x => x.PreviousWorkerId,
                        principalTable: "MachineWorkers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MachineTeamChanges_MachineWorkers_WorkerId",
                        column: x => x.WorkerId,
                        principalTable: "MachineWorkers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MachineTeamChanges_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MachineTeamChanges_Machines_OtherMachineId",
                        column: x => x.OtherMachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MachineTeamTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MachineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    OperatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FirstCollectorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SecondCollectorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PackerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Revision = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MachineTeamTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MachineTeamTemplates_MachineWorkers_FirstCollectorId",
                        column: x => x.FirstCollectorId,
                        principalTable: "MachineWorkers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MachineTeamTemplates_MachineWorkers_OperatorId",
                        column: x => x.OperatorId,
                        principalTable: "MachineWorkers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MachineTeamTemplates_MachineWorkers_PackerId",
                        column: x => x.PackerId,
                        principalTable: "MachineWorkers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MachineTeamTemplates_MachineWorkers_SecondCollectorId",
                        column: x => x.SecondCollectorId,
                        principalTable: "MachineWorkers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MachineTeamTemplates_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductionAllocation_Release",
                table: "ProductionAllocations",
                sql: "CAST([ReleasedQuantity] AS decimal(18,2)) >= 0 AND CAST([ReleasedQuantity] AS decimal(18,2)) <= CAST([Quantity] AS decimal(18,2)) AND ([ReversedAt] IS NULL OR CAST([ReleasedQuantity] AS decimal(18,2)) = 0)");

            migrationBuilder.CreateIndex(
                name: "IX_MachineOperators_UserId",
                table: "MachineOperators",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_MachineProductionNotices_MachineId_ServiceDate",
                table: "MachineProductionNotices",
                columns: new[] { "MachineId", "ServiceDate" });

            migrationBuilder.CreateIndex(
                name: "IX_MachineProductionNotices_OrderId",
                table: "MachineProductionNotices",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_MachineTeamChanges_Date_MachineId_Position_Revision",
                table: "MachineTeamChanges",
                columns: new[] { "Date", "MachineId", "Position", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MachineTeamChanges_MachineId",
                table: "MachineTeamChanges",
                column: "MachineId");

            migrationBuilder.CreateIndex(
                name: "IX_MachineTeamChanges_OtherMachineId",
                table: "MachineTeamChanges",
                column: "OtherMachineId");

            migrationBuilder.CreateIndex(
                name: "IX_MachineTeamChanges_PreviousWorkerId",
                table: "MachineTeamChanges",
                column: "PreviousWorkerId");

            migrationBuilder.CreateIndex(
                name: "IX_MachineTeamChanges_WorkerId",
                table: "MachineTeamChanges",
                column: "WorkerId");

            migrationBuilder.CreateIndex(
                name: "IX_MachineTeamTemplates_FirstCollectorId",
                table: "MachineTeamTemplates",
                column: "FirstCollectorId");

            migrationBuilder.CreateIndex(
                name: "IX_MachineTeamTemplates_MachineId_EffectiveDate",
                table: "MachineTeamTemplates",
                columns: new[] { "MachineId", "EffectiveDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MachineTeamTemplates_OperatorId",
                table: "MachineTeamTemplates",
                column: "OperatorId");

            migrationBuilder.CreateIndex(
                name: "IX_MachineTeamTemplates_PackerId",
                table: "MachineTeamTemplates",
                column: "PackerId");

            migrationBuilder.CreateIndex(
                name: "IX_MachineTeamTemplates_SecondCollectorId",
                table: "MachineTeamTemplates",
                column: "SecondCollectorId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionLotHandoffs_BatchId",
                table: "ProductionLotHandoffs",
                column: "BatchId");

            // Preserve existing user-defined roles even if they use the same display name.
            migrationBuilder.Sql("""
                DECLARE @roleName nvarchar(120) = N'Responsable de Máquina';
                DECLARE @suffix int = 0;
                WHILE EXISTS (SELECT 1 FROM [SecurityRoles] WHERE [Name] = @roleName)
                BEGIN
                    SET @suffix = @suffix + 1;
                    SET @roleName = N'Responsable de Máquina (sistema ' + CONVERT(nvarchar(10), @suffix) + N')';
                END;
                INSERT INTO [SecurityRoles] ([Id], [Name], [Description], [IsActive], [IsSystem])
                VALUES ('24240000-0000-0000-0000-000000000001', @roleName,
                    N'Captura producción y organiza el equipo de las máquinas asignadas.', 1, 1);
                """);

            var administrator = new Guid("18180000-0000-0000-0000-000000000001");
            var machineOperator = new Guid("24240000-0000-0000-0000-000000000001");
            var permissions = new[]
            {
                (Id: new Guid("18180000-0000-0000-0001-000000000022"), Key: "operations.machine.work", Name: "Operar las máquinas asignadas"),
                (Id: new Guid("18180000-0000-0000-0001-000000000023"), Key: "operations.machine.manage", Name: "Administrar responsables y personal de máquinas")
            };
            foreach (var permission in permissions)
            {
                migrationBuilder.InsertData("SecurityPermissions",
                    new[] { "Id", "Key", "Name", "Module", "Description", "IsActive", "IsSystem" },
                    new object[] { permission.Id, permission.Key, permission.Name, "Operación", permission.Name, true, true });
                migrationBuilder.InsertData("SecurityRolePermissions", new[] { "RoleId", "PermissionId" },
                    new object[] { administrator, permission.Id });
                if (permission.Key == "operations.machine.work")
                    migrationBuilder.InsertData("SecurityRolePermissions", new[] { "RoleId", "PermissionId" },
                        new object[] { machineOperator, permission.Id });
            }
            migrationBuilder.Sql("UPDATE [SecurityStates] SET [Revision] = NEWID() WHERE [Id] = 1;");
            migrationBuilder.Sql("""
                IF DATABASE_PRINCIPAL_ID(N'prodimt_pedidos_app') IS NOT NULL
                BEGIN
                    GRANT SELECT, INSERT, DELETE ON dbo.MachineOperators TO [prodimt_pedidos_app];
                    DENY UPDATE ON dbo.MachineOperators TO [prodimt_pedidos_app];
                    GRANT SELECT, INSERT, UPDATE ON dbo.MachineTeamTemplates TO [prodimt_pedidos_app];
                    DENY DELETE ON dbo.MachineTeamTemplates TO [prodimt_pedidos_app];
                    GRANT SELECT, INSERT ON dbo.MachineTeamChanges TO [prodimt_pedidos_app];
                    DENY UPDATE, DELETE ON dbo.MachineTeamChanges TO [prodimt_pedidos_app];
                    GRANT SELECT, INSERT ON dbo.MachineWorkers TO [prodimt_pedidos_app];
                    DENY UPDATE, DELETE ON dbo.MachineWorkers TO [prodimt_pedidos_app];
                    GRANT SELECT, INSERT ON dbo.MachineProductionNotices TO [prodimt_pedidos_app];
                    DENY UPDATE, DELETE ON dbo.MachineProductionNotices TO [prodimt_pedidos_app];
                    GRANT SELECT, INSERT ON dbo.ProductionLotHandoffs TO [prodimt_pedidos_app];
                    DENY UPDATE, DELETE ON dbo.ProductionLotHandoffs TO [prodimt_pedidos_app];
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM [SecurityUserRoles] WHERE [RoleId] = '24240000-0000-0000-0000-000000000001';
                DELETE FROM [SecurityUserPermissions] WHERE [PermissionId] IN ('18180000-0000-0000-0001-000000000022','18180000-0000-0000-0001-000000000023');
                DELETE FROM [SecurityRolePermissions] WHERE [PermissionId] IN ('18180000-0000-0000-0001-000000000022','18180000-0000-0000-0001-000000000023')
                    OR [RoleId] = '24240000-0000-0000-0000-000000000001';
                DELETE FROM [SecurityPermissions] WHERE [Id] IN ('18180000-0000-0000-0001-000000000022','18180000-0000-0000-0001-000000000023');
                DELETE FROM [SecurityRoles] WHERE [Id] = '24240000-0000-0000-0000-000000000001';
                UPDATE [SecurityStates] SET [Revision] = NEWID() WHERE [Id] = 1;
                """);

            migrationBuilder.DropTable(
                name: "MachineOperators");

            migrationBuilder.DropTable(
                name: "MachineProductionNotices");

            migrationBuilder.DropTable(
                name: "MachineTeamChanges");

            migrationBuilder.DropTable(
                name: "MachineTeamTemplates");

            migrationBuilder.DropTable(
                name: "ProductionLotHandoffs");

            migrationBuilder.DropTable(
                name: "MachineWorkers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductionAllocation_Release",
                table: "ProductionAllocations");

            migrationBuilder.DropColumn(
                name: "ReleasedQuantity",
                table: "ProductionAllocations");
        }
    }
}
