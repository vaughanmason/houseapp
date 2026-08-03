using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HomeInventory.Migrations;

[DbContext(typeof(InventoryDbContext))]
[Migration("20260803163500_AddPropertyRoomDetails")]
public partial class AddPropertyRoomDetails : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateOnly>(name: "PurchaseDate", table: "Properties", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "PurchasePrice", table: "Properties", type: "TEXT", nullable: true);

        migrationBuilder.AddColumn<decimal>(name: "Area", table: "Rooms", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "CeilingHeight", table: "Rooms", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<string>(name: "CeilingFinish", table: "Rooms", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<string>(name: "FixturesNotes", table: "Rooms", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<string>(name: "Flooring", table: "Rooms", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<string>(name: "PaintDetails", table: "Rooms", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<string>(name: "UtilitiesNotes", table: "Rooms", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "Volume", table: "Rooms", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<string>(name: "WallFinish", table: "Rooms", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<int>(name: "WindowsCount", table: "Rooms", type: "INTEGER", nullable: true);
        migrationBuilder.AddColumn<int>(name: "DoorsCount", table: "Rooms", type: "INTEGER", nullable: true);

        migrationBuilder.CreateTable(
            name: "PropertyPhotos",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                PropertyId = table.Column<Guid>(type: "TEXT", nullable: false),
                StorageKey = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                Caption = table.Column<string>(type: "TEXT", maxLength: 240, nullable: true),
                SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PropertyPhotos", x => x.Id);
                table.ForeignKey(
                    name: "FK_PropertyPhotos_Properties_PropertyId",
                    column: x => x.PropertyId,
                    principalTable: "Properties",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "RoomPhotos",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                RoomId = table.Column<Guid>(type: "TEXT", nullable: false),
                StorageKey = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                Caption = table.Column<string>(type: "TEXT", maxLength: 240, nullable: true),
                SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RoomPhotos", x => x.Id);
                table.ForeignKey(
                    name: "FK_RoomPhotos_Rooms_RoomId",
                    column: x => x.RoomId,
                    principalTable: "Rooms",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PropertyPhotos_PropertyId",
            table: "PropertyPhotos",
            column: "PropertyId");

        migrationBuilder.CreateIndex(
            name: "IX_RoomPhotos_RoomId",
            table: "RoomPhotos",
            column: "RoomId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "PropertyPhotos");
        migrationBuilder.DropTable(name: "RoomPhotos");

        migrationBuilder.DropColumn(name: "PurchaseDate", table: "Properties");
        migrationBuilder.DropColumn(name: "PurchasePrice", table: "Properties");

        migrationBuilder.DropColumn(name: "Area", table: "Rooms");
        migrationBuilder.DropColumn(name: "CeilingHeight", table: "Rooms");
        migrationBuilder.DropColumn(name: "CeilingFinish", table: "Rooms");
        migrationBuilder.DropColumn(name: "DoorsCount", table: "Rooms");
        migrationBuilder.DropColumn(name: "FixturesNotes", table: "Rooms");
        migrationBuilder.DropColumn(name: "Flooring", table: "Rooms");
        migrationBuilder.DropColumn(name: "PaintDetails", table: "Rooms");
        migrationBuilder.DropColumn(name: "UtilitiesNotes", table: "Rooms");
        migrationBuilder.DropColumn(name: "Volume", table: "Rooms");
        migrationBuilder.DropColumn(name: "WallFinish", table: "Rooms");
        migrationBuilder.DropColumn(name: "WindowsCount", table: "Rooms");
    }
}
