using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prodimt.Pedidos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryTripDepartureAndDayDrivers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DepartedAt",
                table: "DeliveryTrips",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DeliveryRouteDayDrivers",
                columns: table => new
                {
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    RouteCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    DriverUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryRouteDayDrivers", x => new { x.Date, x.RouteCode });
                    table.ForeignKey(
                        name: "FK_DeliveryRouteDayDrivers_AdminUsers_DriverUserId",
                        column: x => x.DriverUserId,
                        principalTable: "AdminUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryRouteDayDrivers_DeliveryRoutes_RouteCode",
                        column: x => x.RouteCode,
                        principalTable: "DeliveryRoutes",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryRouteDayDrivers_DriverUserId",
                table: "DeliveryRouteDayDrivers",
                column: "DriverUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryRouteDayDrivers_RouteCode",
                table: "DeliveryRouteDayDrivers",
                column: "RouteCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeliveryRouteDayDrivers");

            migrationBuilder.DropColumn(
                name: "DepartedAt",
                table: "DeliveryTrips");
        }
    }
}
