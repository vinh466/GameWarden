using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameWarden.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notification_status",
                columns: table => new
                {
                    DestinationId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    MessageId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Content = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_status", x => x.DestinationId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_status");
        }
    }
}
