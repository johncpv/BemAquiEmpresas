using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PaymentNoticeBnk02 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "billing_slip_id",
                schema: "finance",
                table: "receivable_settlements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source",
                schema: "finance",
                table: "receivable_settlements",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Manual");

            migrationBuilder.AddColumn<Guid>(
                name: "webhook_event_id",
                schema: "finance",
                table: "receivable_settlements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reversed_payment_at",
                schema: "finance",
                table: "payables",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "billing_slip_id",
                schema: "finance",
                table: "financial_transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "pending_review",
                schema: "finance",
                table: "financial_transactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "status",
                schema: "finance",
                table: "billing_slips",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AddColumn<DateOnly>(
                name: "credit_date",
                schema: "finance",
                table: "billing_slips",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "paid_event_id",
                schema: "finance",
                table: "billing_slips",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "pending_transaction_id",
                schema: "finance",
                table: "billing_slips",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "review_decision",
                schema: "finance",
                table: "billing_slips",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "review_reason",
                schema: "finance",
                table: "billing_slips",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reviewed_at",
                schema: "finance",
                table: "billing_slips",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reviewed_by",
                schema: "finance",
                table: "billing_slips",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "settlement_id",
                schema: "finance",
                table: "billing_slips",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_reconcile_at",
                schema: "finance",
                table: "bank_integrations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_reconcile_error",
                schema: "finance",
                table: "bank_integrations",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_reconcile_report",
                schema: "finance",
                table: "bank_integrations",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "reconcile_failures",
                schema: "finance",
                table: "bank_integrations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "webhook_mode",
                schema: "finance",
                table: "bank_integrations",
                type: "character varying(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "webhook_registered_at",
                schema: "finance",
                table: "bank_integrations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "webhook_registered_by",
                schema: "finance",
                table: "bank_integrations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "webhook_rotated_at",
                schema: "finance",
                table: "bank_integrations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_financial_transactions_billing_slip_id",
                schema: "finance",
                table: "financial_transactions",
                column: "billing_slip_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_financial_transactions_billing_slip_id",
                schema: "finance",
                table: "financial_transactions");

            migrationBuilder.DropColumn(
                name: "billing_slip_id",
                schema: "finance",
                table: "receivable_settlements");

            migrationBuilder.DropColumn(
                name: "source",
                schema: "finance",
                table: "receivable_settlements");

            migrationBuilder.DropColumn(
                name: "webhook_event_id",
                schema: "finance",
                table: "receivable_settlements");

            migrationBuilder.DropColumn(
                name: "reversed_payment_at",
                schema: "finance",
                table: "payables");

            migrationBuilder.DropColumn(
                name: "billing_slip_id",
                schema: "finance",
                table: "financial_transactions");

            migrationBuilder.DropColumn(
                name: "pending_review",
                schema: "finance",
                table: "financial_transactions");

            migrationBuilder.DropColumn(
                name: "credit_date",
                schema: "finance",
                table: "billing_slips");

            migrationBuilder.DropColumn(
                name: "paid_event_id",
                schema: "finance",
                table: "billing_slips");

            migrationBuilder.DropColumn(
                name: "pending_transaction_id",
                schema: "finance",
                table: "billing_slips");

            migrationBuilder.DropColumn(
                name: "review_decision",
                schema: "finance",
                table: "billing_slips");

            migrationBuilder.DropColumn(
                name: "review_reason",
                schema: "finance",
                table: "billing_slips");

            migrationBuilder.DropColumn(
                name: "reviewed_at",
                schema: "finance",
                table: "billing_slips");

            migrationBuilder.DropColumn(
                name: "reviewed_by",
                schema: "finance",
                table: "billing_slips");

            migrationBuilder.DropColumn(
                name: "settlement_id",
                schema: "finance",
                table: "billing_slips");

            migrationBuilder.DropColumn(
                name: "last_reconcile_at",
                schema: "finance",
                table: "bank_integrations");

            migrationBuilder.DropColumn(
                name: "last_reconcile_error",
                schema: "finance",
                table: "bank_integrations");

            migrationBuilder.DropColumn(
                name: "last_reconcile_report",
                schema: "finance",
                table: "bank_integrations");

            migrationBuilder.DropColumn(
                name: "reconcile_failures",
                schema: "finance",
                table: "bank_integrations");

            migrationBuilder.DropColumn(
                name: "webhook_mode",
                schema: "finance",
                table: "bank_integrations");

            migrationBuilder.DropColumn(
                name: "webhook_registered_at",
                schema: "finance",
                table: "bank_integrations");

            migrationBuilder.DropColumn(
                name: "webhook_registered_by",
                schema: "finance",
                table: "bank_integrations");

            migrationBuilder.DropColumn(
                name: "webhook_rotated_at",
                schema: "finance",
                table: "bank_integrations");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                schema: "finance",
                table: "billing_slips",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(24)",
                oldMaxLength: 24);
        }
    }
}
