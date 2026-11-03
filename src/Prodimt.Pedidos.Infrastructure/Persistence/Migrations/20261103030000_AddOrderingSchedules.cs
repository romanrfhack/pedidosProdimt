using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prodimt.Pedidos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderingSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeOnly>(
                name: "AutomaticConfirmationCutoff",
                table: "Customers",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OrderingScheduleVersion",
                table: "Customers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint("CK_Customers_AutomaticConfirmationCutoff", "Customers",
                "[AutomaticConfirmationCutoff] IS NULL OR [AutomaticConfirmationCutoff] < '18:00:00'");

            migrationBuilder.CreateTable(
                name: "RouteOrderSchedules",
                columns: table => new
                {
                    RouteCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    AutomaticConfirmationCutoff = table.Column<TimeOnly>(type: "time", nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RouteOrderSchedules", x => x.RouteCode);
                    table.CheckConstraint("CK_RouteOrderSchedules_Cutoff", "[AutomaticConfirmationCutoff] IS NULL OR [AutomaticConfirmationCutoff] < '18:00:00'");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint("CK_Customers_AutomaticConfirmationCutoff", "Customers");

            migrationBuilder.DropTable(
                name: "RouteOrderSchedules");

            migrationBuilder.DropColumn(
                name: "AutomaticConfirmationCutoff",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "OrderingScheduleVersion",
                table: "Customers");
        }
    }
}
