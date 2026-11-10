using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalHub.Web.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentDecisionNote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DecisionNote",
                table: "Payment",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payment_BillingPeriodId_ReportedAtUtc",
                table: "Payment",
                columns: new[] { "BillingPeriodId", "ReportedAtUtc" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payment_DecisionNote",
                table: "Payment",
                sql: "\"DecisionNote\" IS NULL OR \"Status\" = 'Rejected'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Payment_BillingPeriodId_ReportedAtUtc",
                table: "Payment");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Payment_DecisionNote",
                table: "Payment");

            migrationBuilder.DropColumn(
                name: "DecisionNote",
                table: "Payment");
        }
    }
}
