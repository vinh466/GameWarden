using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameWarden.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddObservability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notification_deliveries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventId = table.Column<long>(type: "INTEGER", nullable: false),
                    DestinationId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Mode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    MessageId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SentAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_deliveries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "notification_events",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    At = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Topic = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    FieldsJson = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: true),
                    InstanceId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    JobId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Url = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    DedupeKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "system_log",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    At = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Level = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Component = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    EventId = table.Column<int>(type: "INTEGER", nullable: true),
                    Message = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Exception = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: true),
                    InstanceId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    JobId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    RequestId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    AgentClientId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Data = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_log", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_notification_deliveries_EventId_DestinationId",
                table: "notification_deliveries",
                columns: new[] { "EventId", "DestinationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notification_deliveries_State_NextAttemptAt",
                table: "notification_deliveries",
                columns: new[] { "State", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_notification_events_At",
                table: "notification_events",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_notification_events_DedupeKey",
                table: "notification_events",
                column: "DedupeKey");

            migrationBuilder.CreateIndex(
                name: "IX_system_log_At",
                table: "system_log",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_system_log_Component_At",
                table: "system_log",
                columns: new[] { "Component", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_system_log_InstanceId_At",
                table: "system_log",
                columns: new[] { "InstanceId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_system_log_JobId_At",
                table: "system_log",
                columns: new[] { "JobId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_system_log_Level_At",
                table: "system_log",
                columns: new[] { "Level", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_system_log_RequestId",
                table: "system_log",
                column: "RequestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_deliveries");

            migrationBuilder.DropTable(
                name: "notification_events");

            migrationBuilder.DropTable(
                name: "system_log");
        }
    }
}
