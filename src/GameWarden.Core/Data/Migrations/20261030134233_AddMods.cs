using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameWarden.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "instance_mods",
                columns: table => new
                {
                    InstanceId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ActiveSetId = table.Column<long>(type: "INTEGER", nullable: true),
                    PreviousSetId = table.Column<long>(type: "INTEGER", nullable: true),
                    RunningSetId = table.Column<long>(type: "INTEGER", nullable: true),
                    ShareCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ShareCodeCreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instance_mods", x => x.InstanceId);
                    table.ForeignKey(
                        name: "FK_instance_mods_instances_InstanceId",
                        column: x => x.InstanceId,
                        principalTable: "instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mod_sets",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InstanceId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Origin = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    JobId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mod_sets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mod_sets_instances_InstanceId",
                        column: x => x.InstanceId,
                        principalTable: "instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "package_versions",
                columns: table => new
                {
                    Package = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_package_versions", x => new { x.Package, x.Version });
                });

            migrationBuilder.CreateTable(
                name: "mod_entries",
                columns: table => new
                {
                    SetId = table.Column<long>(type: "INTEGER", nullable: false),
                    Package = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Origin = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mod_entries", x => new { x.SetId, x.Package });
                    table.ForeignKey(
                        name: "FK_mod_entries_mod_sets_SetId",
                        column: x => x.SetId,
                        principalTable: "mod_sets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mod_sets_InstanceId",
                table: "mod_sets",
                column: "InstanceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "instance_mods");

            migrationBuilder.DropTable(
                name: "mod_entries");

            migrationBuilder.DropTable(
                name: "package_versions");

            migrationBuilder.DropTable(
                name: "mod_sets");
        }
    }
}
