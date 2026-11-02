using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prodimt.Pedidos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerDeliveryLocations",
                columns: table => new
                {
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MapsUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerDeliveryLocations", x => x.CustomerId);
                    table.ForeignKey(
                        name: "FK_CustomerDeliveryLocations_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryRoutes",
                columns: table => new
                {
                    Code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    DefaultDriverUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryRoutes", x => x.Code);
                    table.ForeignKey(
                        name: "FK_DeliveryRoutes_AdminUsers_DefaultDriverUserId",
                        column: x => x.DefaultDriverUserId,
                        principalTable: "AdminUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryTrips",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    RouteCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    RouteName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    TripNumber = table.Column<int>(type: "int", nullable: false),
                    DriverUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DriverName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryTrips", x => x.Id);
                    table.CheckConstraint("CK_DeliveryTrips_Number", "[TripNumber] >= 1 AND [TripNumber] <= 999");
                    table.CheckConstraint("CK_DeliveryTrips_Status", "[Status] IN ('Open', 'Closed')");
                    table.ForeignKey(
                        name: "FK_DeliveryTrips_AdminUsers_DriverUserId",
                        column: x => x.DriverUserId,
                        principalTable: "AdminUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryTrips_DeliveryRoutes_RouteCode",
                        column: x => x.RouteCode,
                        principalTable: "DeliveryRoutes",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TripId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SequenceNumber = table.Column<int>(type: "int", nullable: false),
                    RouteStopOrder = table.Column<int>(type: "int", nullable: true),
                    ProductName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    UnitCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RequestedQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    AssignedQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    ReleasedQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    CountedQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    LoadedQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    DeliveredQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    ReturnedQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    TransferredOutQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    TransferredInQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    HasUnresolvedIncident = table.Column<bool>(type: "bit", nullable: false),
                    AcknowledgedOrderVersion = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    MapsUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RequestedDeliveryTime = table.Column<TimeOnly>(type: "time", nullable: true),
                    RequestedDeliveryWindowStart = table.Column<TimeOnly>(type: "time", nullable: true),
                    RequestedDeliveryWindowEnd = table.Column<TimeOnly>(type: "time", nullable: true),
                    HasStoreKey = table.Column<bool>(type: "bit", nullable: true),
                    PaymentTermCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    DeliveryNotes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    InternalDeliveryInstructions = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryAssignments", x => x.Id);
                    table.CheckConstraint("CK_DeliveryAssignments_Commitment", "[AssignedQuantity] - [ReleasedQuantity] >= COALESCE([LoadedQuantity], 0) + [TransferredInQuantity] - [ReturnedQuantity] - [TransferredOutQuantity]");
                    table.CheckConstraint("CK_DeliveryAssignments_Custody", "COALESCE([LoadedQuantity], 0) + [TransferredInQuantity] - [DeliveredQuantity] - [ReturnedQuantity] - [TransferredOutQuantity] >= 0");
                    table.CheckConstraint("CK_DeliveryAssignments_Quantities", "[AssignedQuantity] > 0 AND [ReleasedQuantity] >= 0 AND [ReleasedQuantity] <= [AssignedQuantity] AND ([CountedQuantity] IS NULL OR [CountedQuantity] >= 0) AND ([LoadedQuantity] IS NULL OR [LoadedQuantity] >= 0) AND [DeliveredQuantity] >= 0 AND [ReturnedQuantity] >= 0 AND [TransferredOutQuantity] >= 0 AND [TransferredInQuantity] >= 0");
                    table.ForeignKey(
                        name: "FK_DeliveryAssignments_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryAssignments_DeliveryTrips_TripId",
                        column: x => x.TripId,
                        principalTable: "DeliveryTrips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryAssignments_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TripId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Type = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    PreviousQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ActorId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ActorName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliveryEvents_DeliveryAssignments_AssignmentId",
                        column: x => x.AssignmentId,
                        principalTable: "DeliveryAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryEvents_DeliveryTrips_TripId",
                        column: x => x.TripId,
                        principalTable: "DeliveryTrips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryAssignments_CustomerId",
                table: "DeliveryAssignments",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryAssignments_OrderId_OrderLineId",
                table: "DeliveryAssignments",
                columns: new[] { "OrderId", "OrderLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryAssignments_TripId",
                table: "DeliveryAssignments",
                column: "TripId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryEvents_AssignmentId",
                table: "DeliveryEvents",
                column: "AssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryEvents_TripId_OccurredAt",
                table: "DeliveryEvents",
                columns: new[] { "TripId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryRoutes_DefaultDriverUserId",
                table: "DeliveryRoutes",
                column: "DefaultDriverUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryTrips_Date_RouteCode_TripNumber",
                table: "DeliveryTrips",
                columns: new[] { "Date", "RouteCode", "TripNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryTrips_DriverUserId_Date",
                table: "DeliveryTrips",
                columns: new[] { "DriverUserId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryTrips_RouteCode",
                table: "DeliveryTrips",
                column: "RouteCode");

            // Append-only permission identities: published I18/I19 IDs never change.
            var administrator = new Guid("18180000-0000-0000-0000-000000000001");
            var driverRole = new Guid("22220000-0000-0000-0000-000000000001");
            migrationBuilder.InsertData("SecurityRoles",
                new[] { "Id", "Name", "Description", "IsActive", "IsSystem" },
                new object[] { driverRole, "Repartidor - Camioneta", "Consulta y operación de sus vueltas asignadas", true, true });
            var permissions = new[]
            {
                (Id: new Guid("18180000-0000-0000-0001-000000000019"), Key: "delivery.read.own", Name: "Consultar mi reparto"),
                (Id: new Guid("18180000-0000-0000-0001-000000000020"), Key: "delivery.operate.own", Name: "Registrar mi conteo, carga y entregas"),
                (Id: new Guid("18180000-0000-0000-0001-000000000021"), Key: "delivery.manage", Name: "Administrar rutas y reparto")
            };
            foreach (var permission in permissions)
            {
                migrationBuilder.InsertData("SecurityPermissions",
                    new[] { "Id", "Key", "Name", "Module", "Description", "IsActive", "IsSystem" },
                    new object[] { permission.Id, permission.Key, permission.Name, "Reparto", permission.Name, true, true });
                migrationBuilder.InsertData("SecurityRolePermissions", new[] { "RoleId", "PermissionId" }, new object[] { administrator, permission.Id });
                if (permission.Key != "delivery.manage")
                    migrationBuilder.InsertData("SecurityRolePermissions", new[] { "RoleId", "PermissionId" }, new object[] { driverRole, permission.Id });
            }
            migrationBuilder.Sql("UPDATE [SecurityStates] SET [Revision] = NEWID() WHERE [Id] = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM [SecurityUserRoles] WHERE [RoleId] = '22220000-0000-0000-0000-000000000001';");
            migrationBuilder.Sql("DELETE FROM [SecurityUserPermissions] WHERE [PermissionId] IN ('18180000-0000-0000-0001-000000000019','18180000-0000-0000-0001-000000000020','18180000-0000-0000-0001-000000000021');");
            migrationBuilder.Sql("DELETE FROM [SecurityRolePermissions] WHERE [PermissionId] IN ('18180000-0000-0000-0001-000000000019','18180000-0000-0000-0001-000000000020','18180000-0000-0000-0001-000000000021') OR [RoleId] = '22220000-0000-0000-0000-000000000001';");
            migrationBuilder.Sql("DELETE FROM [SecurityPermissions] WHERE [Id] IN ('18180000-0000-0000-0001-000000000019','18180000-0000-0000-0001-000000000020','18180000-0000-0000-0001-000000000021');");
            migrationBuilder.Sql("DELETE FROM [SecurityRoles] WHERE [Id] = '22220000-0000-0000-0000-000000000001';");
            migrationBuilder.Sql("UPDATE [SecurityStates] SET [Revision] = NEWID() WHERE [Id] = 1;");
            migrationBuilder.DropTable(
                name: "CustomerDeliveryLocations");

            migrationBuilder.DropTable(
                name: "DeliveryEvents");

            migrationBuilder.DropTable(
                name: "DeliveryAssignments");

            migrationBuilder.DropTable(
                name: "DeliveryTrips");

            migrationBuilder.DropTable(
                name: "DeliveryRoutes");
        }
    }
}
