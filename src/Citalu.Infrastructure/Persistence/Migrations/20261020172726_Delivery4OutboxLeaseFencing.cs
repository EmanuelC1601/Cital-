using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Citalu.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Delivery4OutboxLeaseFencing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LeaseId",
                table: "EmailOutbox",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LeaseId",
                table: "EmailOutbox");
        }
    }
}
