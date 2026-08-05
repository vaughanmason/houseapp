using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HomeInventory.Migrations;

[DbContext(typeof(InventoryDbContext))]
[Migration("20260805150000_AddPropertyCurrency")]
public partial class AddPropertyCurrency : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Currency",
            table: "Properties",
            type: "TEXT",
            maxLength: 3,
            nullable: false,
            defaultValue: "USD");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Currency", table: "Properties");
    }
}
