using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameWarden.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddModsLastStart : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "LastStartModsOn",
                table: "instance_mods",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastStartModsOn",
                table: "instance_mods");
        }
    }
}
