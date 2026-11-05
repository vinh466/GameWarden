using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameWarden.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class OperationsKernel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApprovalId",
                table: "agent_tool_calls",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ErrorCode",
                table: "agent_tool_calls",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstanceId",
                table: "agent_tool_calls",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PolicyRule",
                table: "agent_tool_calls",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TraceId",
                table: "agent_tool_calls",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DurationMs",
                table: "agent_runs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PromptId",
                table: "agent_runs",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TraceId",
                table: "agent_runs",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TurnId",
                table: "agent_runs",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExecutedAt",
                table: "agent_approvals",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExecutionState",
                table: "agent_approvals",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "notStarted");

            migrationBuilder.AddColumn<string>(
                name: "Impact",
                table: "agent_approvals",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstanceId",
                table: "agent_approvals",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PolicyRule",
                table: "agent_approvals",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Scope",
                table: "agent_approvals",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "agent_approvals",
                type: "TEXT",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceId",
                table: "agent_approvals",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TraceId",
                table: "agent_approvals",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Verification",
                table: "agent_approvals",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VerifiedAt",
                table: "agent_approvals",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "agent_events",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    At = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TraceId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Type = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Data = table.Column<string>(type: "TEXT", nullable: true),
                    InstanceId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    RunId = table.Column<long>(type: "INTEGER", nullable: true),
                    IncidentId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "agent_incident_events",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IncidentId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    At = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Data = table.Column<string>(type: "TEXT", nullable: true),
                    TraceId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_incident_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "agent_incidents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    InstanceId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Trigger = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    TriggerId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ChannelId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ThreadId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    MessageId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    TraceId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    OpenedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AcknowledgedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_incidents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "agent_policy_decisions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    At = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TraceId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    TurnId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Capability = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    InstanceId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Rule = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Decision = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    InputHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_policy_decisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "agent_runbook_runs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RunbookId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RunbookVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    IncidentId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    InstanceId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    State = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    CurrentStep = table.Column<int>(type: "INTEGER", nullable: false),
                    Checkpoint = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: true),
                    Output = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: true),
                    Error = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    TraceId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_runbook_runs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agent_events_At",
                table: "agent_events",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_agent_events_TraceId_Id",
                table: "agent_events",
                columns: new[] { "TraceId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_agent_incident_events_IncidentId_Id",
                table: "agent_incident_events",
                columns: new[] { "IncidentId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_agent_incidents_InstanceId_State",
                table: "agent_incidents",
                columns: new[] { "InstanceId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_agent_incidents_OpenedAt",
                table: "agent_incidents",
                column: "OpenedAt");

            migrationBuilder.CreateIndex(
                name: "IX_agent_policy_decisions_At",
                table: "agent_policy_decisions",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_agent_runbook_runs_IncidentId",
                table: "agent_runbook_runs",
                column: "IncidentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_events");

            migrationBuilder.DropTable(
                name: "agent_incident_events");

            migrationBuilder.DropTable(
                name: "agent_incidents");

            migrationBuilder.DropTable(
                name: "agent_policy_decisions");

            migrationBuilder.DropTable(
                name: "agent_runbook_runs");

            migrationBuilder.DropColumn(
                name: "ApprovalId",
                table: "agent_tool_calls");

            migrationBuilder.DropColumn(
                name: "ErrorCode",
                table: "agent_tool_calls");

            migrationBuilder.DropColumn(
                name: "InstanceId",
                table: "agent_tool_calls");

            migrationBuilder.DropColumn(
                name: "PolicyRule",
                table: "agent_tool_calls");

            migrationBuilder.DropColumn(
                name: "TraceId",
                table: "agent_tool_calls");

            migrationBuilder.DropColumn(
                name: "DurationMs",
                table: "agent_runs");

            migrationBuilder.DropColumn(
                name: "PromptId",
                table: "agent_runs");

            migrationBuilder.DropColumn(
                name: "TraceId",
                table: "agent_runs");

            migrationBuilder.DropColumn(
                name: "TurnId",
                table: "agent_runs");

            migrationBuilder.DropColumn(
                name: "ExecutedAt",
                table: "agent_approvals");

            migrationBuilder.DropColumn(
                name: "ExecutionState",
                table: "agent_approvals");

            migrationBuilder.DropColumn(
                name: "Impact",
                table: "agent_approvals");

            migrationBuilder.DropColumn(
                name: "InstanceId",
                table: "agent_approvals");

            migrationBuilder.DropColumn(
                name: "PolicyRule",
                table: "agent_approvals");

            migrationBuilder.DropColumn(
                name: "Scope",
                table: "agent_approvals");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "agent_approvals");

            migrationBuilder.DropColumn(
                name: "SourceId",
                table: "agent_approvals");

            migrationBuilder.DropColumn(
                name: "TraceId",
                table: "agent_approvals");

            migrationBuilder.DropColumn(
                name: "Verification",
                table: "agent_approvals");

            migrationBuilder.DropColumn(
                name: "VerifiedAt",
                table: "agent_approvals");
        }
    }
}
