using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameWarden.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false, collation: "NOCASE"),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    SecurityStamp = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    IsOwner = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    PasswordChangedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_users_UserName",
                table: "users",
                column: "UserName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
