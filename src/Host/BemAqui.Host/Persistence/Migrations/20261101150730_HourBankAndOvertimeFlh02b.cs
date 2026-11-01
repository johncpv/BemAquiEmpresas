using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HourBankAndOvertimeFlh02b : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "bank_payout_minutes",
                schema: "hr",
                table: "timesheets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "hour_bank_decision_minutes",
                schema: "hr",
                table: "payslips",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "hour_bank_decision_note",
                schema: "hr",
                table: "payslips",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "bank_payout_minutes",
                schema: "hr",
                table: "timesheets");

            migrationBuilder.DropColumn(
                name: "hour_bank_decision_minutes",
                schema: "hr",
                table: "payslips");

            migrationBuilder.DropColumn(
                name: "hour_bank_decision_note",
                schema: "hr",
                table: "payslips");
        }
    }
}
