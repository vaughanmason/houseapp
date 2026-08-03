using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HomeInventory.Migrations;

[DbContext(typeof(InventoryDbContext))]
[Migration("20260803130000_InitialCreate")]
public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "Properties", columns: table => new
        {
            Id = table.Column<Guid>(type: "TEXT", nullable: false),
            Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
            Address = table.Column<string>(type: "TEXT", nullable: true),
            FloorArea = table.Column<decimal>(type: "TEXT", nullable: true),
            Notes = table.Column<string>(type: "TEXT", nullable: true)
        }, constraints: table => table.PrimaryKey("PK_Properties", x => x.Id));
        migrationBuilder.CreateTable(name: "Rooms", columns: table => new
        {
            Id = table.Column<Guid>(type: "TEXT", nullable: false), PropertyId = table.Column<Guid>(type: "TEXT", nullable: false), Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false), Type = table.Column<string>(type: "TEXT", nullable: true), Length = table.Column<decimal>(type: "TEXT", nullable: true), Width = table.Column<decimal>(type: "TEXT", nullable: true), Height = table.Column<decimal>(type: "TEXT", nullable: true), Notes = table.Column<string>(type: "TEXT", nullable: true)
        }, constraints: table => { table.PrimaryKey("PK_Rooms", x => x.Id); table.ForeignKey("FK_Rooms_Properties_PropertyId", x => x.PropertyId, "Properties", "Id", onDelete: ReferentialAction.Cascade); });
        migrationBuilder.CreateTable(name: "StorageLocations", columns: table => new
        {
            Id = table.Column<Guid>(type: "TEXT", nullable: false), PropertyId = table.Column<Guid>(type: "TEXT", nullable: false), ParentId = table.Column<Guid>(type: "TEXT", nullable: true), Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false), Type = table.Column<string>(type: "TEXT", nullable: true)
        }, constraints: table => { table.PrimaryKey("PK_StorageLocations", x => x.Id); table.ForeignKey("FK_StorageLocations_Properties_PropertyId", x => x.PropertyId, "Properties", "Id", onDelete: ReferentialAction.Cascade); table.ForeignKey("FK_StorageLocations_StorageLocations_ParentId", x => x.ParentId, "StorageLocations", "Id", onDelete: ReferentialAction.Restrict); });
        migrationBuilder.CreateTable(name: "Assets", columns: table => new
        {
            Id = table.Column<Guid>(type: "TEXT", nullable: false), PropertyId = table.Column<Guid>(type: "TEXT", nullable: false), RoomId = table.Column<Guid>(type: "TEXT", nullable: true), StorageLocationId = table.Column<Guid>(type: "TEXT", nullable: true), Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false), Category = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false), Description = table.Column<string>(type: "TEXT", nullable: true), Brand = table.Column<string>(type: "TEXT", nullable: true), Model = table.Column<string>(type: "TEXT", nullable: true), SerialNumber = table.Column<string>(type: "TEXT", nullable: true), PurchaseDate = table.Column<DateOnly>(type: "TEXT", nullable: true), PurchasePrice = table.Column<decimal>(type: "TEXT", nullable: true), CurrentValue = table.Column<decimal>(type: "TEXT", nullable: true), Condition = table.Column<string>(type: "TEXT", nullable: true), Notes = table.Column<string>(type: "TEXT", nullable: true), IsArchived = table.Column<bool>(type: "INTEGER", nullable: false)
        }, constraints: table => { table.PrimaryKey("PK_Assets", x => x.Id); table.ForeignKey("FK_Assets_Properties_PropertyId", x => x.PropertyId, "Properties", "Id", onDelete: ReferentialAction.Cascade); table.ForeignKey("FK_Assets_Rooms_RoomId", x => x.RoomId, "Rooms", "Id", onDelete: ReferentialAction.Restrict); table.ForeignKey("FK_Assets_StorageLocations_StorageLocationId", x => x.StorageLocationId, "StorageLocations", "Id", onDelete: ReferentialAction.Restrict); });
        migrationBuilder.CreateIndex(name: "IX_Rooms_PropertyId", table: "Rooms", column: "PropertyId");
        migrationBuilder.CreateIndex(name: "IX_StorageLocations_PropertyId", table: "StorageLocations", column: "PropertyId");
        migrationBuilder.CreateIndex(name: "IX_StorageLocations_ParentId", table: "StorageLocations", column: "ParentId");
        migrationBuilder.CreateIndex(name: "IX_Assets_PropertyId", table: "Assets", column: "PropertyId");
        migrationBuilder.CreateIndex(name: "IX_Assets_RoomId", table: "Assets", column: "RoomId");
        migrationBuilder.CreateIndex(name: "IX_Assets_StorageLocationId", table: "Assets", column: "StorageLocationId");
    }
    protected override void Down(MigrationBuilder migrationBuilder) { migrationBuilder.DropTable("Assets"); migrationBuilder.DropTable("Rooms"); migrationBuilder.DropTable("StorageLocations"); migrationBuilder.DropTable("Properties"); }
}
