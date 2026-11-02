using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prodimt.Pedidos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSecurityAdministration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "AdminUsers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "SessionRevision",
                table: "AdminUsers",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: Guid.Empty);

            migrationBuilder.CreateTable(
                name: "SecurityPermissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Module = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityPermissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SecurityRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SecurityStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityStates", x => x.Id);
                    table.CheckConstraint("CK_SecurityState_Singleton", "[Id] = 1");
                });

            migrationBuilder.CreateTable(
                name: "SecurityUserPermissions",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityUserPermissions", x => new { x.UserId, x.PermissionId });
                    table.ForeignKey(
                        name: "FK_SecurityUserPermissions_AdminUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AdminUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SecurityUserPermissions_SecurityPermissions_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "SecurityPermissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SecurityRolePermissions",
                columns: table => new
                {
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityRolePermissions", x => new { x.RoleId, x.PermissionId });
                    table.ForeignKey(
                        name: "FK_SecurityRolePermissions_SecurityPermissions_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "SecurityPermissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SecurityRolePermissions_SecurityRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "SecurityRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SecurityUserRoles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_SecurityUserRoles_AdminUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AdminUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SecurityUserRoles_SecurityRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "SecurityRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SecurityPermissions_Key",
                table: "SecurityPermissions",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SecurityRolePermissions_PermissionId",
                table: "SecurityRolePermissions",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityRoles_Name",
                table: "SecurityRoles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SecurityUserPermissions_PermissionId",
                table: "SecurityUserPermissions",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityUserRoles_RoleId",
                table: "SecurityUserRoles",
                column: "RoleId");
            // Frozen I18 bootstrap: future startup/seed runs must never reset edited grants.
            migrationBuilder.InsertData("SecurityStates", new[] { "Id", "Revision" },
                new object[] { 1, new Guid("18180000-0000-0000-0000-000000000002") });
            migrationBuilder.InsertData("SecurityRoles", new[] { "Id", "Name", "Description", "IsActive", "IsSystem" },
                new object[] { new Guid("18180000-0000-0000-0000-000000000001"), "Administrador", "Administración del sistema", true, true });
            migrationBuilder.InsertData("SecurityPermissions", new[] { "Id", "Key", "Name", "Module", "Description", "IsActive", "IsSystem" },
                new object[] { new Guid("18180000-0000-0000-0001-000000000001"), "orders.read", "Consultar pedidos", "Pedidos", "Consultar pedidos", true, true });
            migrationBuilder.InsertData("SecurityRolePermissions", new[] { "RoleId", "PermissionId" },
                new object[] { new Guid("18180000-0000-0000-0000-000000000001"), new Guid("18180000-0000-0000-0001-000000000001") });
            migrationBuilder.InsertData("SecurityPermissions", new[] { "Id", "Key", "Name", "Module", "Description", "IsActive", "IsSystem" },
                new object[] { new Guid("18180000-0000-0000-0001-000000000002"), "orders.capture", "Capturar y corregir pedidos", "Pedidos", "Capturar y corregir pedidos", true, true });
            migrationBuilder.InsertData("SecurityRolePermissions", new[] { "RoleId", "PermissionId" },
                new object[] { new Guid("18180000-0000-0000-0000-000000000001"), new Guid("18180000-0000-0000-0001-000000000002") });
            migrationBuilder.InsertData("SecurityPermissions", new[] { "Id", "Key", "Name", "Module", "Description", "IsActive", "IsSystem" },
                new object[] { new Guid("18180000-0000-0000-0001-000000000003"), "orders.review", "Revisar pedidos", "Pedidos", "Revisar pedidos", true, true });
            migrationBuilder.InsertData("SecurityRolePermissions", new[] { "RoleId", "PermissionId" },
                new object[] { new Guid("18180000-0000-0000-0000-000000000001"), new Guid("18180000-0000-0000-0001-000000000003") });
            migrationBuilder.InsertData("SecurityPermissions", new[] { "Id", "Key", "Name", "Module", "Description", "IsActive", "IsSystem" },
                new object[] { new Guid("18180000-0000-0000-0001-000000000004"), "customers.read", "Consultar clientes", "Clientes", "Consultar clientes", true, true });
            migrationBuilder.InsertData("SecurityRolePermissions", new[] { "RoleId", "PermissionId" },
                new object[] { new Guid("18180000-0000-0000-0000-000000000001"), new Guid("18180000-0000-0000-0001-000000000004") });
            migrationBuilder.InsertData("SecurityPermissions", new[] { "Id", "Key", "Name", "Module", "Description", "IsActive", "IsSystem" },
                new object[] { new Guid("18180000-0000-0000-0001-000000000005"), "customers.manage", "Administrar clientes y perfiles", "Clientes", "Administrar clientes y perfiles", true, true });
            migrationBuilder.InsertData("SecurityRolePermissions", new[] { "RoleId", "PermissionId" },
                new object[] { new Guid("18180000-0000-0000-0000-000000000001"), new Guid("18180000-0000-0000-0001-000000000005") });
            migrationBuilder.InsertData("SecurityPermissions", new[] { "Id", "Key", "Name", "Module", "Description", "IsActive", "IsSystem" },
                new object[] { new Guid("18180000-0000-0000-0001-000000000006"), "products.read", "Consultar productos", "Productos", "Consultar productos", true, true });
            migrationBuilder.InsertData("SecurityRolePermissions", new[] { "RoleId", "PermissionId" },
                new object[] { new Guid("18180000-0000-0000-0000-000000000001"), new Guid("18180000-0000-0000-0001-000000000006") });
            migrationBuilder.InsertData("SecurityPermissions", new[] { "Id", "Key", "Name", "Module", "Description", "IsActive", "IsSystem" },
                new object[] { new Guid("18180000-0000-0000-0001-000000000007"), "products.manage", "Administrar productos", "Productos", "Administrar productos", true, true });
            migrationBuilder.InsertData("SecurityRolePermissions", new[] { "RoleId", "PermissionId" },
                new object[] { new Guid("18180000-0000-0000-0000-000000000001"), new Guid("18180000-0000-0000-0001-000000000007") });
            migrationBuilder.InsertData("SecurityPermissions", new[] { "Id", "Key", "Name", "Module", "Description", "IsActive", "IsSystem" },
                new object[] { new Guid("18180000-0000-0000-0001-000000000008"), "machines.read", "Consultar máquinas", "Máquinas", "Consultar máquinas", true, true });
            migrationBuilder.InsertData("SecurityRolePermissions", new[] { "RoleId", "PermissionId" },
                new object[] { new Guid("18180000-0000-0000-0000-000000000001"), new Guid("18180000-0000-0000-0001-000000000008") });
            migrationBuilder.InsertData("SecurityPermissions", new[] { "Id", "Key", "Name", "Module", "Description", "IsActive", "IsSystem" },
                new object[] { new Guid("18180000-0000-0000-0001-000000000009"), "machines.manage", "Administrar máquinas", "Máquinas", "Administrar máquinas", true, true });
            migrationBuilder.InsertData("SecurityRolePermissions", new[] { "RoleId", "PermissionId" },
                new object[] { new Guid("18180000-0000-0000-0000-000000000001"), new Guid("18180000-0000-0000-0001-000000000009") });
            migrationBuilder.InsertData("SecurityPermissions", new[] { "Id", "Key", "Name", "Module", "Description", "IsActive", "IsSystem" },
                new object[] { new Guid("18180000-0000-0000-0001-000000000010"), "pricing.manage", "Administrar precios", "Precios", "Administrar precios", true, true });
            migrationBuilder.InsertData("SecurityRolePermissions", new[] { "RoleId", "PermissionId" },
                new object[] { new Guid("18180000-0000-0000-0000-000000000001"), new Guid("18180000-0000-0000-0001-000000000010") });
            migrationBuilder.InsertData("SecurityPermissions", new[] { "Id", "Key", "Name", "Module", "Description", "IsActive", "IsSystem" },
                new object[] { new Guid("18180000-0000-0000-0001-000000000011"), "tokens.manage", "Administrar accesos de clientes", "Accesos", "Administrar accesos de clientes", true, true });
            migrationBuilder.InsertData("SecurityRolePermissions", new[] { "RoleId", "PermissionId" },
                new object[] { new Guid("18180000-0000-0000-0000-000000000001"), new Guid("18180000-0000-0000-0001-000000000011") });
            migrationBuilder.InsertData("SecurityPermissions", new[] { "Id", "Key", "Name", "Module", "Description", "IsActive", "IsSystem" },
                new object[] { new Guid("18180000-0000-0000-0001-000000000012"), "imports.manage", "Importar catálogos y pedidos", "Importación", "Importar catálogos y pedidos", true, true });
            migrationBuilder.InsertData("SecurityRolePermissions", new[] { "RoleId", "PermissionId" },
                new object[] { new Guid("18180000-0000-0000-0000-000000000001"), new Guid("18180000-0000-0000-0001-000000000012") });
            migrationBuilder.InsertData("SecurityPermissions", new[] { "Id", "Key", "Name", "Module", "Description", "IsActive", "IsSystem" },
                new object[] { new Guid("18180000-0000-0000-0001-000000000013"), "operations.read", "Consultar operación y avisos", "Operación", "Consultar operación y avisos", true, true });
            migrationBuilder.InsertData("SecurityRolePermissions", new[] { "RoleId", "PermissionId" },
                new object[] { new Guid("18180000-0000-0000-0000-000000000001"), new Guid("18180000-0000-0000-0001-000000000013") });
            migrationBuilder.InsertData("SecurityPermissions", new[] { "Id", "Key", "Name", "Module", "Description", "IsActive", "IsSystem" },
                new object[] { new Guid("18180000-0000-0000-0001-000000000014"), "operations.acknowledge", "Reconocer avisos", "Operación", "Reconocer avisos", true, true });
            migrationBuilder.InsertData("SecurityRolePermissions", new[] { "RoleId", "PermissionId" },
                new object[] { new Guid("18180000-0000-0000-0000-000000000001"), new Guid("18180000-0000-0000-0001-000000000014") });
            migrationBuilder.InsertData("SecurityPermissions", new[] { "Id", "Key", "Name", "Module", "Description", "IsActive", "IsSystem" },
                new object[] { new Guid("18180000-0000-0000-0001-000000000015"), "security.manage", "Administrar usuarios, roles y permisos", "Seguridad", "Administrar usuarios, roles y permisos", true, true });
            migrationBuilder.InsertData("SecurityRolePermissions", new[] { "RoleId", "PermissionId" },
                new object[] { new Guid("18180000-0000-0000-0000-000000000001"), new Guid("18180000-0000-0000-0001-000000000015") });
            migrationBuilder.Sql("INSERT INTO [SecurityUserRoles] ([UserId], [RoleId]) SELECT [Id], '18180000-0000-0000-0000-000000000001' FROM [AdminUsers];");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SecurityRolePermissions");

            migrationBuilder.DropTable(
                name: "SecurityStates");

            migrationBuilder.DropTable(
                name: "SecurityUserPermissions");

            migrationBuilder.DropTable(
                name: "SecurityUserRoles");

            migrationBuilder.DropTable(
                name: "SecurityPermissions");

            migrationBuilder.DropTable(
                name: "SecurityRoles");

            migrationBuilder.DropColumn(
                name: "SessionRevision",
                table: "AdminUsers");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "AdminUsers");
        }
    }
}
