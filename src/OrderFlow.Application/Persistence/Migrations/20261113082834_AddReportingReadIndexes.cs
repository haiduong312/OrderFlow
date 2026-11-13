using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderFlow.Application.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReportingReadIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_OccurredAt_Id",
                schema: "inventory",
                table: "StockMovements",
                columns: new[] { "OccurredAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_SucceededAt_Id",
                schema: "payments",
                table: "Refunds",
                columns: new[] { "SucceededAt", "Id" },
                filter: "\"Status\" = 'Succeeded'");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCaptures_Order_RecordedAt_Id",
                schema: "payments",
                table: "PaymentCaptures",
                columns: new[] { "OrderId", "RecordedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCaptures_RecordedAt_Id",
                schema: "payments",
                table: "PaymentCaptures",
                columns: new[] { "RecordedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAttempts_Order_CreatedAt_Id",
                schema: "payments",
                table: "PaymentAttempts",
                columns: new[] { "OrderId", "CreatedAt", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockMovements_OccurredAt_Id",
                schema: "inventory",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_Refunds_SucceededAt_Id",
                schema: "payments",
                table: "Refunds");

            migrationBuilder.DropIndex(
                name: "IX_PaymentCaptures_Order_RecordedAt_Id",
                schema: "payments",
                table: "PaymentCaptures");

            migrationBuilder.DropIndex(
                name: "IX_PaymentCaptures_RecordedAt_Id",
                schema: "payments",
                table: "PaymentCaptures");

            migrationBuilder.DropIndex(
                name: "IX_PaymentAttempts_Order_CreatedAt_Id",
                schema: "payments",
                table: "PaymentAttempts");
        }
    }
}
