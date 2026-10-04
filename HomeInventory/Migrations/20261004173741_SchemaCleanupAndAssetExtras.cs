using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeInventory.Migrations
{
    /// <inheritdoc />
    public partial class SchemaCleanupAndAssetExtras : Migration
    {
        // A random version-4 GUID in the uppercase text format EF Core uses for Guid columns on SQLite.
        const string NewGuidSql = "upper(hex(randomblob(4))) || '-' || upper(hex(randomblob(2))) || '-4' || substr(upper(hex(randomblob(2))), 2) || '-' || substr('89AB', 1 + (abs(random()) % 4), 1) || substr(upper(hex(randomblob(2))), 2) || '-' || upper(hex(randomblob(6)))";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every room now needs a floor: park any floorless (or orphaned) rooms on an "Unassigned rooms" floor in their property.
            migrationBuilder.Sql($"""
                INSERT INTO "Floors" ("Id", "PropertyId", "Name")
                SELECT {NewGuidSql}, "PropertyId", 'Unassigned rooms'
                FROM (SELECT DISTINCT "PropertyId" FROM "Rooms" WHERE "FloorId" IS NULL OR "FloorId" NOT IN (SELECT "Id" FROM "Floors"));
                """);
            migrationBuilder.Sql("""
                UPDATE "Rooms"
                SET "FloorId" = (SELECT f."Id" FROM "Floors" f WHERE f."PropertyId" = "Rooms"."PropertyId" AND f."Name" = 'Unassigned rooms' ORDER BY f."Id" LIMIT 1)
                WHERE "FloorId" IS NULL OR "FloorId" NOT IN (SELECT "Id" FROM "Floors");
                """);

            // The free-text fixture maintenance fields are replaced by maintenance tasks; carry existing values over before dropping them.
            migrationBuilder.Sql($"""
                INSERT INTO "MaintenanceTasks" ("Id", "PropertyId", "FixtureId", "Title", "LastCompletedOn", "Notes")
                SELECT {NewGuidSql}, r."PropertyId", f."Id", substr('Service: ' || f."Name", 1, 200), f."LastMaintenanceDate", f."MaintenanceSchedule"
                FROM "Fixtures" f JOIN "Rooms" r ON r."Id" = f."RoomId"
                WHERE (f."MaintenanceSchedule" IS NOT NULL AND trim(f."MaintenanceSchedule") <> '') OR f."LastMaintenanceDate" IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "LastMaintenanceDate",
                table: "Fixtures");

            migrationBuilder.DropColumn(
                name: "MaintenanceSchedule",
                table: "Fixtures");

            migrationBuilder.AlterColumn<Guid>(
                name: "FloorId",
                table: "Rooms",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Barcode",
                table: "Assets",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManualUrl",
                table: "Assets",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Assets_Barcode",
                table: "Assets",
                column: "Barcode");

            // Asset history started after these assets were created; give each one its "Added" event.
            migrationBuilder.Sql($"""
                INSERT INTO "AssetEvents" ("Id", "AssetId", "OccurredOn", "Kind", "Description", "Cost")
                SELECT {NewGuidSql}, a."Id", COALESCE(a."PurchaseDate", date('now')), 'Added',
                       CASE WHEN a."PurchaseDate" IS NULL THEN 'Added to the inventory' ELSE 'Bought' END, a."PurchasePrice"
                FROM "Assets" a
                WHERE NOT EXISTS (SELECT 1 FROM "AssetEvents" e WHERE e."AssetId" = a."Id" AND e."Kind" = 'Added');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Assets_Barcode",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "Barcode",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "ManualUrl",
                table: "Assets");

            migrationBuilder.AlterColumn<Guid>(
                name: "FloorId",
                table: "Rooms",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AddColumn<DateOnly>(
                name: "LastMaintenanceDate",
                table: "Fixtures",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MaintenanceSchedule",
                table: "Fixtures",
                type: "TEXT",
                maxLength: 400,
                nullable: true);
        }
    }
}
