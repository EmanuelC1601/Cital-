using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Belleza.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Delivery3ManagementTokenProtection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ManageTokenCiphertext",
                table: "Appointments",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ManageTokenCiphertext",
                table: "Appointments");
        }
    }
}
