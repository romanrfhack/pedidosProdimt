using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prodimt.Pedidos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPilotCatalogAndOrderSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_CustomerId_OrderDate_SequenceNumber",
                table: "Orders");

            migrationBuilder.AddColumn<string>(
                name: "UnitCode",
                table: "Products",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerNameSnapshot",
                table: "Orders",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasStoreKey",
                table: "Orders",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InternalDeliveryInstructions",
                table: "Orders",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OrderingProfileVersion",
                table: "Orders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentTermCode",
                table: "Orders",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RouteCode",
                table: "Orders",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RouteStopOrder",
                table: "Orders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Orders",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "ProductNameSnapshot",
                table: "OrderLines",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UnitCode",
                table: "OrderLines",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasStoreKey",
                table: "Customers",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InternalDeliveryInstructions",
                table: "Customers",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsOrderProfileApproved",
                table: "Customers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "OrderingProfileVersion",
                table: "Customers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PaymentTermCode",
                table: "Customers",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RouteCode",
                table: "Customers",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RouteStopOrder",
                table: "Customers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InterpretationPosition",
                table: "CustomerFrequentProducts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PreferredMachineId",
                table: "CustomerFrequentProducts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CustomerId_OrderDate_SequenceNumber",
                table: "Orders",
                columns: new[] { "CustomerId", "OrderDate", "SequenceNumber" },
                unique: true,
                filter: "[CustomerId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_RouteCode_RouteStopOrder",
                table: "Customers",
                columns: new[] { "RouteCode", "RouteStopOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerFrequentProducts_CustomerId_InterpretationPosition",
                table: "CustomerFrequentProducts",
                columns: new[] { "CustomerId", "InterpretationPosition" },
                unique: true,
                filter: "[InterpretationPosition] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerFrequentProducts_PreferredMachineId",
                table: "CustomerFrequentProducts",
                column: "PreferredMachineId");

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerFrequentProducts_Machines_PreferredMachineId",
                table: "CustomerFrequentProducts",
                column: "PreferredMachineId",
                principalTable: "Machines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CustomerFrequentProducts_Machines_PreferredMachineId",
                table: "CustomerFrequentProducts");

            migrationBuilder.DropIndex(
                name: "IX_Orders_CustomerId_OrderDate_SequenceNumber",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Customers_RouteCode_RouteStopOrder",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_CustomerFrequentProducts_CustomerId_InterpretationPosition",
                table: "CustomerFrequentProducts");

            migrationBuilder.DropIndex(
                name: "IX_CustomerFrequentProducts_PreferredMachineId",
                table: "CustomerFrequentProducts");

            migrationBuilder.DropColumn(
                name: "UnitCode",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "CustomerNameSnapshot",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "HasStoreKey",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "InternalDeliveryInstructions",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "OrderingProfileVersion",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentTermCode",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "RouteCode",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "RouteStopOrder",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ProductNameSnapshot",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "UnitCode",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "HasStoreKey",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "InternalDeliveryInstructions",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "IsOrderProfileApproved",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "OrderingProfileVersion",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "PaymentTermCode",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "RouteCode",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "RouteStopOrder",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "InterpretationPosition",
                table: "CustomerFrequentProducts");

            migrationBuilder.DropColumn(
                name: "PreferredMachineId",
                table: "CustomerFrequentProducts");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CustomerId_OrderDate_SequenceNumber",
                table: "Orders",
                columns: new[] { "CustomerId", "OrderDate", "SequenceNumber" });
        }
    }
}
