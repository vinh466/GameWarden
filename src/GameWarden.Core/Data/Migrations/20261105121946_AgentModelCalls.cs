using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameWarden.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AgentModelCalls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_model_calls",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    At = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TraceId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    RunId = table.Column<long>(type: "INTEGER", nullable: true),
                    TurnId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Purpose = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    LatencyMs = table.Column<int>(type: "INTEGER", nullable: false),
                    PromptTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    CompletionTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    ToolCalls = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    PromptPreview = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: true),
                    ResponsePreview = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: true),
                    IncidentId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_model_calls", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agent_model_calls_At",
                table: "agent_model_calls",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_agent_model_calls_TraceId",
                table: "agent_model_calls",
                column: "TraceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_model_calls");
        }
    }
}
