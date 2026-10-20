using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Citalu.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Delivery5UniqueManualPaymentReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SubscriptionPayments_SubscriptionId",
                table: "SubscriptionPayments");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPayments_SubscriptionId_Reference",
                table: "SubscriptionPayments",
                columns: new[] { "SubscriptionId", "Reference" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SubscriptionPayments_SubscriptionId_Reference",
                table: "SubscriptionPayments");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPayments_SubscriptionId",
                table: "SubscriptionPayments",
                column: "SubscriptionId");
        }
    }
}
