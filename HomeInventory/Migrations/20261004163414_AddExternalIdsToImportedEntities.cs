using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeInventory.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalIdsToImportedEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "Surfaces",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "StorageLocations",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "Rooms",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "RoomPhotos",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "PropertyPhotos",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "Properties",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "Paints",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "Floors",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "Fixtures",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "FixturePhotos",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "AssetPhotos",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Surfaces_ExternalId",
                table: "Surfaces",
                column: "ExternalId");

            migrationBuilder.CreateIndex(
                name: "IX_StorageLocations_ExternalId",
                table: "StorageLocations",
                column: "ExternalId");

            migrationBuilder.CreateIndex(
                name: "IX_Rooms_ExternalId",
                table: "Rooms",
                column: "ExternalId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomPhotos_ExternalId",
                table: "RoomPhotos",
                column: "ExternalId");

            migrationBuilder.CreateIndex(
                name: "IX_PropertyPhotos_ExternalId",
                table: "PropertyPhotos",
                column: "ExternalId");

            migrationBuilder.CreateIndex(
                name: "IX_Properties_ExternalId",
                table: "Properties",
                column: "ExternalId");

            migrationBuilder.CreateIndex(
                name: "IX_Paints_ExternalId",
                table: "Paints",
                column: "ExternalId");

            migrationBuilder.CreateIndex(
                name: "IX_Floors_ExternalId",
                table: "Floors",
                column: "ExternalId");

            migrationBuilder.CreateIndex(
                name: "IX_Fixtures_ExternalId",
                table: "Fixtures",
                column: "ExternalId");

            migrationBuilder.CreateIndex(
                name: "IX_FixturePhotos_ExternalId",
                table: "FixturePhotos",
                column: "ExternalId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetPhotos_ExternalId",
                table: "AssetPhotos",
                column: "ExternalId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Surfaces_ExternalId",
                table: "Surfaces");

            migrationBuilder.DropIndex(
                name: "IX_StorageLocations_ExternalId",
                table: "StorageLocations");

            migrationBuilder.DropIndex(
                name: "IX_Rooms_ExternalId",
                table: "Rooms");

            migrationBuilder.DropIndex(
                name: "IX_RoomPhotos_ExternalId",
                table: "RoomPhotos");

            migrationBuilder.DropIndex(
                name: "IX_PropertyPhotos_ExternalId",
                table: "PropertyPhotos");

            migrationBuilder.DropIndex(
                name: "IX_Properties_ExternalId",
                table: "Properties");

            migrationBuilder.DropIndex(
                name: "IX_Paints_ExternalId",
                table: "Paints");

            migrationBuilder.DropIndex(
                name: "IX_Floors_ExternalId",
                table: "Floors");

            migrationBuilder.DropIndex(
                name: "IX_Fixtures_ExternalId",
                table: "Fixtures");

            migrationBuilder.DropIndex(
                name: "IX_FixturePhotos_ExternalId",
                table: "FixturePhotos");

            migrationBuilder.DropIndex(
                name: "IX_AssetPhotos_ExternalId",
                table: "AssetPhotos");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "Surfaces");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "StorageLocations");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "RoomPhotos");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "PropertyPhotos");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "Paints");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "Floors");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "Fixtures");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "FixturePhotos");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "AssetPhotos");
        }
    }
}
