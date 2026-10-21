using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Belleza.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Delivery3AppointmentsAndBookingLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaximumBookingDays",
                table: "Businesses",
                type: "int",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.AddColumn<int>(
                name: "MinimumAdvanceMinutes",
                table: "Businesses",
                type: "int",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.CreateTable(
                name: "Appointments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    CustomerPhone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CustomerEmail = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    ServiceNameSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PriceSnapshot = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false),
                    DurationMinutesSnapshot = table.Column<int>(type: "int", nullable: false),
                    BufferMinutesSnapshot = table.Column<int>(type: "int", nullable: false),
                    StartUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ManageTokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TokenExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IdempotencyKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Appointments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Appointments_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Appointments_Services_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "Services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppointmentChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChangeType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Actor = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    OldStartUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    NewStartUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppointmentChanges_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentChanges_AppointmentId",
                table: "AppointmentChanges",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentChanges_BusinessId_AppointmentId_CreatedAtUtc",
                table: "AppointmentChanges",
                columns: new[] { "BusinessId", "AppointmentId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_BusinessId_IdempotencyKeyHash",
                table: "Appointments",
                columns: new[] { "BusinessId", "IdempotencyKeyHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_BusinessId_ManageTokenHash",
                table: "Appointments",
                columns: new[] { "BusinessId", "ManageTokenHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_BusinessId_StartUtc",
                table: "Appointments",
                columns: new[] { "BusinessId", "StartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ServiceId",
                table: "Appointments",
                column: "ServiceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppointmentChanges");

            migrationBuilder.DropTable(
                name: "Appointments");

            migrationBuilder.DropColumn(
                name: "MaximumBookingDays",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "MinimumAdvanceMinutes",
                table: "Businesses");
        }
    }
}
