using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeInventory.Migrations
{
    /// <inheritdoc />
    public partial class AddUtilityFieldsToFixtures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccountNumber",
                table: "Fixtures",
                type: "TEXT",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "Fixtures",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "Fixture");

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "Fixtures",
                type: "TEXT",
                maxLength: 160,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccountNumber",
                table: "Fixtures");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "Fixtures");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "Fixtures");
        }
    }
}
