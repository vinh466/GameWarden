using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameWarden.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "instances",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AdapterId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AdapterVersion = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    DesiredState = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    PersistedState = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    RunnerPid = table.Column<int>(type: "INTEGER", nullable: true),
                    RunnerStartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    PortsJson = table.Column<string>(type: "TEXT", nullable: false),
                    SettingsJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instances", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "instances");
        }
    }
}
