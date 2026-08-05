using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HomeInventory.Migrations;

[DbContext(typeof(InventoryDbContext))]
[Migration("20260805120000_AddFloorsAndSurfaces")]
public partial class AddFloorsAndSurfaces : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Floors",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                PropertyId = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                Notes = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Floors", x => x.Id);
                table.ForeignKey(
                    name: "FK_Floors_Properties_PropertyId",
                    column: x => x.PropertyId,
                    principalTable: "Properties",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.AddColumn<Guid>(
            name: "FloorId",
            table: "Rooms",
            type: "TEXT",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "Surfaces",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                RoomId = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                SurfaceType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                PaintBrand = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                ColorName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                ColorCode = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                Finish = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                Coats = table.Column<int>(type: "INTEGER", nullable: true),
                PaintedDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                Painter = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                QuantityPurchased = table.Column<decimal>(type: "TEXT", nullable: true),
                Manufacturer = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                ProductName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                Material = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                Supplier = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                Warranty = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                Invoice = table.Column<string>(type: "TEXT", maxLength: 240, nullable: true),
                InstallationDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                Notes = table.Column<string>(type: "TEXT", maxLength: 800, nullable: true),
                SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Surfaces", x => x.Id);
                table.ForeignKey(
                    name: "FK_Surfaces_Rooms_RoomId",
                    column: x => x.RoomId,
                    principalTable: "Rooms",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Floors_PropertyId",
            table: "Floors",
            column: "PropertyId");

        migrationBuilder.CreateIndex(
            name: "IX_Rooms_FloorId",
            table: "Rooms",
            column: "FloorId");

        migrationBuilder.CreateIndex(
            name: "IX_Surfaces_RoomId",
            table: "Surfaces",
            column: "RoomId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "Surfaces");
        migrationBuilder.DropColumn(name: "FloorId", table: "Rooms");
        migrationBuilder.DropTable(name: "Floors");
    }
}
