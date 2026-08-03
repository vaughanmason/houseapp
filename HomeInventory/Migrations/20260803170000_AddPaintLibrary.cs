using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HomeInventory.Migrations;

[DbContext(typeof(InventoryDbContext))]
[Migration("20260803170000_AddPaintLibrary")]
public partial class AddPaintLibrary : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Paints",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Brand = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                ColorName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                ColorCode = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                Finish = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                Notes = table.Column<string>(type: "TEXT", maxLength: 800, nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_Paints", x => x.Id));

        migrationBuilder.CreateTable(
            name: "RoomPaints",
            columns: table => new
            {
                RoomId = table.Column<Guid>(type: "TEXT", nullable: false),
                PaintId = table.Column<Guid>(type: "TEXT", nullable: false),
                SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                Surface = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RoomPaints", x => new { x.RoomId, x.PaintId });
                table.ForeignKey(
                    name: "FK_RoomPaints_Paints_PaintId",
                    column: x => x.PaintId,
                    principalTable: "Paints",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_RoomPaints_Rooms_RoomId",
                    column: x => x.RoomId,
                    principalTable: "Rooms",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_RoomPaints_PaintId",
            table: "RoomPaints",
            column: "PaintId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "RoomPaints");
        migrationBuilder.DropTable(name: "Paints");
    }
}
