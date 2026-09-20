using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenLethe.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChapter10RpgSaveInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Chapter10RpgSaveInfo",
                table: "accounts",
                type: "jsonb",
                nullable: false,
                // EF's generated default for a string column is "", which is not valid
                // jsonb - existing rows have to land on the same "{}" placeholder every
                // other game-data column starts from.
                defaultValue: "{}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Chapter10RpgSaveInfo",
                table: "accounts");
        }
    }
}
