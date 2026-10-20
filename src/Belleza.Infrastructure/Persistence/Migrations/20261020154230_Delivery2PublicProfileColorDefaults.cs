using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Belleza.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Delivery2PublicProfileColorDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Normaliza los valores de negocios ya guardados antes de habilitar los defaults.
            migrationBuilder.Sql("UPDATE dbo.Businesses SET PrimaryColor = '#854f6c' WHERE PrimaryColor = ''; UPDATE dbo.Businesses SET AccentColor = '#f3e9ef' WHERE AccentColor = '';");
            migrationBuilder.AlterColumn<string>(
                name: "PrimaryColor",
                table: "Businesses",
                type: "nvarchar(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "#854f6c",
                oldClrType: typeof(string),
                oldType: "nvarchar(7)",
                oldMaxLength: 7);

            migrationBuilder.AlterColumn<string>(
                name: "AccentColor",
                table: "Businesses",
                type: "nvarchar(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "#f3e9ef",
                oldClrType: typeof(string),
                oldType: "nvarchar(7)",
                oldMaxLength: 7);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "PrimaryColor",
                table: "Businesses",
                type: "nvarchar(7)",
                maxLength: 7,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(7)",
                oldMaxLength: 7,
                oldDefaultValue: "#854f6c");

            migrationBuilder.AlterColumn<string>(
                name: "AccentColor",
                table: "Businesses",
                type: "nvarchar(7)",
                maxLength: 7,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(7)",
                oldMaxLength: 7,
                oldDefaultValue: "#f3e9ef");
        }
    }
}
