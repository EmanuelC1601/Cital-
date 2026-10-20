using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Belleza.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Delivery2ServicesAvailabilityAndPublicProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccentColor",
                table: "Businesses",
                type: "nvarchar(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "#f3e9ef");

            migrationBuilder.AddColumn<string>(
                name: "CoverPath",
                table: "Businesses",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Businesses",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Location",
                table: "Businesses",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LogoPath",
                table: "Businesses",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PrimaryColor",
                table: "Businesses",
                type: "nvarchar(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "#854f6c");

            migrationBuilder.AddColumn<string>(
                name: "PublicEmail",
                table: "Businesses",
                type: "nvarchar(254)",
                maxLength: 254,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicPhone",
                table: "Businesses",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "BlockedPeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Start = table.Column<TimeOnly>(type: "time", nullable: false),
                    End = table.Column<TimeOnly>(type: "time", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockedPeriods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BlockedPeriods_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleExceptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    IsClosed = table.Column<bool>(type: "bit", nullable: false),
                    Start = table.Column<TimeOnly>(type: "time", nullable: true),
                    End = table.Column<TimeOnly>(type: "time", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleExceptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduleExceptions_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Services",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    BufferMinutes = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Services", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Services_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WeeklyBreaks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Day = table.Column<int>(type: "int", nullable: false),
                    Start = table.Column<TimeOnly>(type: "time", nullable: false),
                    End = table.Column<TimeOnly>(type: "time", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeeklyBreaks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WeeklyBreaks_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WeeklyWindows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Day = table.Column<int>(type: "int", nullable: false),
                    Start = table.Column<TimeOnly>(type: "time", nullable: false),
                    End = table.Column<TimeOnly>(type: "time", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeeklyWindows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WeeklyWindows_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BlockedPeriods_BusinessId",
                table: "BlockedPeriods",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleExceptions_BusinessId_Date",
                table: "ScheduleExceptions",
                columns: new[] { "BusinessId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Services_BusinessId_Name",
                table: "Services",
                columns: new[] { "BusinessId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyBreaks_BusinessId",
                table: "WeeklyBreaks",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyWindows_BusinessId",
                table: "WeeklyWindows",
                column: "BusinessId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BlockedPeriods");

            migrationBuilder.DropTable(
                name: "ScheduleExceptions");

            migrationBuilder.DropTable(
                name: "Services");

            migrationBuilder.DropTable(
                name: "WeeklyBreaks");

            migrationBuilder.DropTable(
                name: "WeeklyWindows");

            migrationBuilder.DropColumn(
                name: "AccentColor",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "CoverPath",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "Location",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "LogoPath",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "PrimaryColor",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "PublicEmail",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "PublicPhone",
                table: "Businesses");
        }
    }
}
