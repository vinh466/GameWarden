using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameWarden.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "events",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    At = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Level = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    InstanceId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    JobId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    User = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Message = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_events", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_events_InstanceId",
                table: "events",
                column: "InstanceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "events");
        }
    }
}
