using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HomeInventory.Migrations;

[DbContext(typeof(InventoryDbContext))]
[Migration("20260805140000_AddFixturesAndAssetPhotos")]
public partial class AddFixturesAndAssetPhotos : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Fixtures",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                RoomId = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Type = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                Manufacturer = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                Model = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                SerialNumber = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                PurchaseDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                PurchasePrice = table.Column<decimal>(type: "TEXT", nullable: true),
                CurrentValue = table.Column<decimal>(type: "TEXT", nullable: true),
                Warranty = table.Column<string>(type: "TEXT", maxLength: 240, nullable: true),
                ManualUrl = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                InstallerName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                InstallationDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                MaintenanceSchedule = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                LastMaintenanceDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                Condition = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Fixtures", x => x.Id);
                table.ForeignKey(
                    name: "FK_Fixtures_Rooms_RoomId",
                    column: x => x.RoomId,
                    principalTable: "Rooms",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "FixturePhotos",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                FixtureId = table.Column<Guid>(type: "TEXT", nullable: false),
                StorageKey = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                Caption = table.Column<string>(type: "TEXT", maxLength: 240, nullable: true),
                SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FixturePhotos", x => x.Id);
                table.ForeignKey(
                    name: "FK_FixturePhotos_Fixtures_FixtureId",
                    column: x => x.FixtureId,
                    principalTable: "Fixtures",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AssetPhotos",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                AssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                StorageKey = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                Caption = table.Column<string>(type: "TEXT", maxLength: 240, nullable: true),
                SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AssetPhotos", x => x.Id);
                table.ForeignKey(
                    name: "FK_AssetPhotos_Assets_AssetId",
                    column: x => x.AssetId,
                    principalTable: "Assets",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Fixtures_RoomId",
            table: "Fixtures",
            column: "RoomId");

        migrationBuilder.CreateIndex(
            name: "IX_FixturePhotos_FixtureId",
            table: "FixturePhotos",
            column: "FixtureId");

        migrationBuilder.CreateIndex(
            name: "IX_AssetPhotos_AssetId",
            table: "AssetPhotos",
            column: "AssetId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "FixturePhotos");
        migrationBuilder.DropTable(name: "AssetPhotos");
        migrationBuilder.DropTable(name: "Fixtures");
    }
}
