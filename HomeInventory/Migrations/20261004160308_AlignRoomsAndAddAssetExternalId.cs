using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeInventory.Migrations
{
    /// <summary>
    /// First migration scaffolded with a model snapshot. Earlier migrations were hand-written without designer files,
    /// so this one only applies the real schema changes: the missing Rooms.FloorId foreign key (applied by SQLite
    /// as a Rooms table rebuild driven by the designer's target model) and Assets.ExternalId for import de-duplication.
    /// </summary>
    public partial class AlignRoomsAndAddAssetExternalId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_Rooms_Floors_FloorId",
                table: "Rooms",
                column: "FloorId",
                principalTable: "Floors",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "Assets",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Assets_ExternalId",
                table: "Assets",
                column: "ExternalId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Assets_ExternalId",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "Assets");

            migrationBuilder.DropForeignKey(
                name: "FK_Rooms_Floors_FloorId",
                table: "Rooms");
        }
    }
}
