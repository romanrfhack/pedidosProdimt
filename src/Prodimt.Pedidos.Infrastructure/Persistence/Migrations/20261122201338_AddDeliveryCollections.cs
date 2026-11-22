using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prodimt.Pedidos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryCollections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CollectionCashReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TripId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DriverUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DriverName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RouteCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    TripNumber = table.Column<int>(type: "int", nullable: false),
                    ExpectedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ReceivedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReceiverUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceiverName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionCashReceipts", x => x.Id);
                    table.CheckConstraint("CK_CollectionCashReceipt_Amount", "[ExpectedAmount] > 0 AND [ReceivedAmount] >= 0");
                    table.ForeignKey(
                        name: "FK_CollectionCashReceipts_AdminUsers_ReceiverUserId",
                        column: x => x.ReceiverUserId,
                        principalTable: "AdminUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CollectionCashReceipts_DeliveryTrips_TripId",
                        column: x => x.TripId,
                        principalTable: "DeliveryTrips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CollectionCharges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceKey = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    DeliveryEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssignmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TripId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UnitCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ServiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RouteCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    TripNumber = table.Column<int>(type: "int", nullable: false),
                    DriverUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DriverName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PaymentTermCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    RequestedQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    IsCredit = table.Column<bool>(type: "bit", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    ActorName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionCharges", x => x.Id);
                    table.CheckConstraint("CK_CollectionCharge_Amount", "([Amount] IS NULL OR [Amount] >= 0) AND ([UnitPrice] IS NULL OR [UnitPrice] >= 0)");
                    table.CheckConstraint("CK_CollectionCharge_Quantity", "[Quantity] > 0 AND [RequestedQuantity] >= 0");
                    table.ForeignKey(
                        name: "FK_CollectionCharges_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CollectionCharges_DeliveryAssignments_AssignmentId",
                        column: x => x.AssignmentId,
                        principalTable: "DeliveryAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CollectionCharges_DeliveryEvents_DeliveryEventId",
                        column: x => x.DeliveryEventId,
                        principalTable: "DeliveryEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CollectionCharges_DeliveryTrips_TripId",
                        column: x => x.TripId,
                        principalTable: "DeliveryTrips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CollectionCharges_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CollectionPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TripId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DriverUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DriverName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ServiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RouteCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    TripNumber = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AllocationPolicy = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    AllocationsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreditAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    ActorName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionPayments", x => x.Id);
                    table.CheckConstraint("CK_CollectionPayment_Amount", "[Amount] > 0 AND ([CreditAmount] IS NULL OR ([CreditAmount] >= 0 AND [CreditAmount] <= [Amount]))");
                    table.ForeignKey(
                        name: "FK_CollectionPayments_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CollectionPayments_DeliveryAssignments_AssignmentId",
                        column: x => x.AssignmentId,
                        principalTable: "DeliveryAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CollectionPayments_DeliveryTrips_TripId",
                        column: x => x.TripId,
                        principalTable: "DeliveryTrips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CollectionChargeValuations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChargeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    ActorName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionChargeValuations", x => x.Id);
                    table.CheckConstraint("CK_CollectionValuation_Amount", "[Amount] >= 0 AND [UnitPrice] >= 0");
                    table.ForeignKey(
                        name: "FK_CollectionChargeValuations_CollectionCharges_ChargeId",
                        column: x => x.ChargeId,
                        principalTable: "CollectionCharges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CollectionCashReceiptLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceiptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionCashReceiptLines", x => x.Id);
                    table.CheckConstraint("CK_CollectionCashReceiptLine_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_CollectionCashReceiptLines_CollectionCashReceipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "CollectionCashReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CollectionCashReceiptLines_CollectionPayments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "CollectionPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CollectionCashReceiptLines_PaymentId",
                table: "CollectionCashReceiptLines",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionCashReceiptLines_ReceiptId_PaymentId",
                table: "CollectionCashReceiptLines",
                columns: new[] { "ReceiptId", "PaymentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CollectionCashReceipts_ReceiverUserId_CreatedAt",
                table: "CollectionCashReceipts",
                columns: new[] { "ReceiverUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CollectionCashReceipts_TripId",
                table: "CollectionCashReceipts",
                column: "TripId");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionCharges_AssignmentId",
                table: "CollectionCharges",
                column: "AssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionCharges_CustomerId_ServiceDate",
                table: "CollectionCharges",
                columns: new[] { "CustomerId", "ServiceDate" });

            migrationBuilder.CreateIndex(
                name: "IX_CollectionCharges_DeliveryEventId",
                table: "CollectionCharges",
                column: "DeliveryEventId");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionCharges_OrderId",
                table: "CollectionCharges",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionCharges_OrderLineId_Sequence",
                table: "CollectionCharges",
                columns: new[] { "OrderLineId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CollectionCharges_SourceKey",
                table: "CollectionCharges",
                column: "SourceKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CollectionCharges_TripId",
                table: "CollectionCharges",
                column: "TripId");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionChargeValuations_ChargeId",
                table: "CollectionChargeValuations",
                column: "ChargeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CollectionPayments_AssignmentId",
                table: "CollectionPayments",
                column: "AssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionPayments_CustomerId_ServiceDate",
                table: "CollectionPayments",
                columns: new[] { "CustomerId", "ServiceDate" });

            migrationBuilder.CreateIndex(
                name: "IX_CollectionPayments_TripId",
                table: "CollectionPayments",
                column: "TripId");

            ConfigurePermissions(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM dbo.CollectionCharges) OR EXISTS (SELECT 1 FROM dbo.CollectionPayments)
                   OR EXISTS (SELECT 1 FROM dbo.CollectionCashReceipts)
                    THROW 51022, N'La cobranza contiene movimientos; recuperar hacia delante sin eliminar su historia.', 1;
                DELETE FROM dbo.SecurityUserPermissions WHERE PermissionId IN
                    ('18180000-0000-0000-0001-000000000024','18180000-0000-0000-0001-000000000025','18180000-0000-0000-0001-000000000026');
                DELETE FROM dbo.SecurityRolePermissions WHERE PermissionId IN
                    ('18180000-0000-0000-0001-000000000024','18180000-0000-0000-0001-000000000025','18180000-0000-0000-0001-000000000026');
                DELETE FROM dbo.SecurityPermissions WHERE Id IN
                    ('18180000-0000-0000-0001-000000000024','18180000-0000-0000-0001-000000000025','18180000-0000-0000-0001-000000000026');
                UPDATE dbo.SecurityStates SET Revision=NEWID() WHERE Id=1;
                """);
            migrationBuilder.DropTable(
                name: "CollectionCashReceiptLines");

            migrationBuilder.DropTable(
                name: "CollectionChargeValuations");

            migrationBuilder.DropTable(
                name: "CollectionCashReceipts");

            migrationBuilder.DropTable(
                name: "CollectionPayments");

            migrationBuilder.DropTable(
                name: "CollectionCharges");
        }

        private static void ConfigurePermissions(MigrationBuilder migrationBuilder)
        {
            var admin = new Guid("18180000-0000-0000-0000-000000000001");
            var driver = new Guid("22220000-0000-0000-0000-000000000001");
            var permissions = new[]
            {
                (Number: 24, Key: "operations.collections.work", Name: "Registrar y consultar la cobranza de mi reparto"),
                (Number: 25, Key: "operations.collections.receive", Name: "Recibir la cobranza de las rutas en Mostrador"),
                (Number: 26, Key: "operations.collections.read", Name: "Consultar adeudos y liquidaciones de cobranza")
            };
            foreach (var permission in permissions)
            {
                var id = Guid.Parse($"18180000-0000-0000-0001-{permission.Number:000000000000}");
                migrationBuilder.InsertData("SecurityPermissions",
                    new[] { "Id", "Key", "Name", "Module", "Description", "IsActive", "IsSystem" },
                    new object[] { id, permission.Key, permission.Name, "Cobranza", permission.Name, true, true });
                migrationBuilder.InsertData("SecurityRolePermissions", new[] { "RoleId", "PermissionId" }, new object[] { admin, id });
                if (permission.Number == 24)
                    migrationBuilder.InsertData("SecurityRolePermissions", new[] { "RoleId", "PermissionId" }, new object[] { driver, id });
            }
            // Identify only the role expressly requested by the user. Never infer cash
            // authority from order-capture permissions or grant it to every name variant.
            migrationBuilder.Sql("""
                DECLARE @cashier uniqueidentifier;
                DECLARE @matches int;
                SELECT @matches=COUNT(*) FROM dbo.SecurityRoles
                WHERE IsActive=1 AND REPLACE(REPLACE(LTRIM(RTRIM(Name)),N'–',N'-'),N'—',N'-')
                    COLLATE Latin1_General_100_CI_AI=N'Administración - Mostrador';
                IF @matches=1
                    SELECT @cashier=Id FROM dbo.SecurityRoles
                    WHERE IsActive=1 AND REPLACE(REPLACE(LTRIM(RTRIM(Name)),N'–',N'-'),N'—',N'-')
                        COLLATE Latin1_General_100_CI_AI=N'Administración - Mostrador';
                ELSE IF @matches>1
                    SELECT @cashier=Id FROM dbo.SecurityRoles
                    WHERE IsActive=1 AND Name COLLATE Latin1_General_100_BIN2=N'Administración - Mostrador';
                IF @cashier IS NULL
                BEGIN
                    SET @cashier='25250000-0000-0000-0000-000000000001';
                    IF EXISTS (SELECT 1 FROM dbo.SecurityRoles WHERE Id=@cashier)
                        THROW 51023, N'El identificador reservado de Mostrador ya pertenece a otro rol. Resolver su identidad antes de conceder permisos de caja.', 1;
                    ELSE
                    BEGIN
                        DECLARE @name nvarchar(120)=CASE WHEN @matches=0 THEN N'Administración - Mostrador' ELSE N'Administración - Mostrador (caja)' END;
                        DECLARE @suffix int=0;
                        WHILE EXISTS (SELECT 1 FROM dbo.SecurityRoles WHERE Name=@name)
                        BEGIN
                            SET @suffix=@suffix+1;
                            SET @name=N'Administración - Mostrador (caja '+CONVERT(nvarchar(10),@suffix)+N')';
                        END;
                        INSERT INTO dbo.SecurityRoles (Id,Name,Description,IsActive,IsSystem)
                        VALUES (@cashier,@name,N'Recepción y consulta de cobranza entregada por repartidores',1,1);
                        INSERT INTO dbo.SecurityRolePermissions (RoleId,PermissionId)
                            SELECT @cashier,Id FROM dbo.SecurityPermissions WHERE [Key] IN ('orders.read','orders.capture');
                    END;
                END;
                INSERT INTO dbo.SecurityRolePermissions (RoleId,PermissionId)
                    SELECT @cashier,Id FROM dbo.SecurityPermissions WHERE [Key] IN
                        ('operations.collections.read','operations.collections.receive');
                UPDATE dbo.SecurityStates SET Revision=NEWID() WHERE Id=1;
                IF DATABASE_PRINCIPAL_ID(N'prodimt_pedidos_app') IS NOT NULL
                BEGIN
                    GRANT SELECT,INSERT ON dbo.CollectionCharges TO prodimt_pedidos_app;
                    GRANT SELECT,INSERT ON dbo.CollectionChargeValuations TO prodimt_pedidos_app;
                    GRANT SELECT,INSERT ON dbo.CollectionPayments TO prodimt_pedidos_app;
                    GRANT SELECT,INSERT ON dbo.CollectionCashReceipts TO prodimt_pedidos_app;
                    GRANT SELECT,INSERT ON dbo.CollectionCashReceiptLines TO prodimt_pedidos_app;
                    DENY UPDATE,DELETE ON dbo.CollectionCharges TO prodimt_pedidos_app;
                    DENY UPDATE,DELETE ON dbo.CollectionChargeValuations TO prodimt_pedidos_app;
                    DENY UPDATE,DELETE ON dbo.CollectionPayments TO prodimt_pedidos_app;
                    DENY UPDATE,DELETE ON dbo.CollectionCashReceipts TO prodimt_pedidos_app;
                    DENY UPDATE,DELETE ON dbo.CollectionCashReceiptLines TO prodimt_pedidos_app;
                END;
                """);
        }
    }
}
