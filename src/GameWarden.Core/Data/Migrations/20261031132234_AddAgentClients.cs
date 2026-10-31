using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameWarden.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentClients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_audit",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    At = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ClientId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ClientName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    TokenId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Action = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Target = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    StatusCode = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceIp = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    RequestId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: false),
                    Details = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_audit", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "agent_clients",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false, collation: "NOCASE"),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    TrustedFull = table.Column<bool>(type: "INTEGER", nullable: false),
                    PermissionsJson = table.Column<string>(type: "TEXT", nullable: false),
                    AllowedNetworksJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastSeenIp = table.Column<string>(type: "TEXT", nullable: true),
                    LastSeenVersion = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_clients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "agent_settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    DiagnosticsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    DiagnosticsExpiresAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    DiagnosticsReason = table.Column<string>(type: "TEXT", nullable: true),
                    AdminEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AdminExpiresAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    AdminReason = table.Column<string>(type: "TEXT", nullable: true),
                    RequireAdminMode = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowedNetworksJson = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "agent_tokens",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ClientId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SecretHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Prefix = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastUsedIp = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agent_tokens_agent_clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "agent_clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agent_audit_At",
                table: "agent_audit",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_agent_audit_ClientId",
                table: "agent_audit",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_agent_clients_Name",
                table: "agent_clients",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agent_tokens_ClientId",
                table: "agent_tokens",
                column: "ClientId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_audit");

            migrationBuilder.DropTable(
                name: "agent_settings");

            migrationBuilder.DropTable(
                name: "agent_tokens");

            migrationBuilder.DropTable(
                name: "agent_clients");
        }
    }
}
