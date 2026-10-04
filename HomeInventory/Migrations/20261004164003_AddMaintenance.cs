using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeInventory.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MaintenanceTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    PropertyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FixtureId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IntervalValue = table.Column<int>(type: "INTEGER", nullable: true),
                    IntervalUnit = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    DueOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    LastCompletedOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Supplier = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                    EstimatedCost = table.Column<decimal>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaintenanceTasks_Fixtures_FixtureId",
                        column: x => x.FixtureId,
                        principalTable: "Fixtures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MaintenanceTasks_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "Properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    TaskId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompletedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Cost = table.Column<decimal>(type: "TEXT", nullable: true),
                    Supplier = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaintenanceRecords_MaintenanceTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "MaintenanceTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRecords_ExternalId",
                table: "MaintenanceRecords",
                column: "ExternalId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRecords_TaskId",
                table: "MaintenanceRecords",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceTasks_DueOn",
                table: "MaintenanceTasks",
                column: "DueOn");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceTasks_ExternalId",
                table: "MaintenanceTasks",
                column: "ExternalId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceTasks_FixtureId",
                table: "MaintenanceTasks",
                column: "FixtureId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceTasks_PropertyId",
                table: "MaintenanceTasks",
                column: "PropertyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaintenanceRecords");

            migrationBuilder.DropTable(
                name: "MaintenanceTasks");
        }
    }
}
