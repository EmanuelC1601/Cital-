using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Citalu.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Delivery4PersistentNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReminderLeadHours",
                table: "Businesses",
                type: "int",
                nullable: false,
                defaultValue: 24);

            migrationBuilder.CreateTable(
                name: "EmailOutbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Recipient = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProtectedBody = table.Column<string>(type: "nvarchar(max)", maxLength: 12000, nullable: false),
                    DeduplicationKey = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AvailableAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AppointmentVersionUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    LockedUntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastFailure = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SentAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailOutbox", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailOutbox_BusinessId_AppointmentId_Status",
                table: "EmailOutbox",
                columns: new[] { "BusinessId", "AppointmentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailOutbox_DeduplicationKey",
                table: "EmailOutbox",
                column: "DeduplicationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailOutbox_Status_AvailableAtUtc_LockedUntilUtc",
                table: "EmailOutbox",
                columns: new[] { "Status", "AvailableAtUtc", "LockedUntilUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailOutbox");

            migrationBuilder.DropColumn(
                name: "ReminderLeadHours",
                table: "Businesses");
        }
    }
}
