using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BillingSlipsBnk01b : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "billing_slip_id",
                schema: "finance",
                table: "receivables",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "billing_reference_sequences",
                schema: "finance",
                columns: table => new
                {
                    bank_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_billing_reference_sequences", x => x.bank_account_id);
                    table.ForeignKey(
                        name: "fk_billing_reference_sequences_bank_accounts_bank_account_id",
                        column: x => x.bank_account_id,
                        principalSchema: "finance",
                        principalTable: "bank_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "billing_slips",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    receivable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    our_reference = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    our_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    provider_request_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    barcode = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    digitable_line = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    pix_copy_paste = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    qr_code = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    receivable_due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    fine_from = table.Column<DateOnly>(type: "date", nullable: false),
                    fine_pct = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    interest_month_pct = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    discount_until = table.Column<DateOnly>(type: "date", nullable: true),
                    message = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    pdf_attachment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    failure_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    failure_action = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    issue_attempts = table.Column<int>(type: "integer", nullable: false),
                    poll_attempts = table.Column<int>(type: "integer", nullable: false),
                    cancel_reason = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    cancel_requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancel_requested_by = table.Column<Guid>(type: "uuid", nullable: true),
                    cancel_failure_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_by = table.Column<Guid>(type: "uuid", nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    paid_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    expired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    provider_payload = table.Column<string>(type: "jsonb", nullable: true),
                    client_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    send_on_issue = table.Column<bool>(type: "boolean", nullable: false),
                    scheduled_email_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    scheduled_email_to = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    scheduled_email_cc = table.Column<string>(type: "character varying(800)", maxLength: 800, nullable: true),
                    last_email_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: true),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_billing_slips", x => x.id);
                    table.ForeignKey(
                        name: "fk_billing_slips_bank_accounts_bank_account_id",
                        column: x => x.bank_account_id,
                        principalSchema: "finance",
                        principalTable: "bank_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_billing_slips_receivables_receivable_id",
                        column: x => x.receivable_id,
                        principalSchema: "finance",
                        principalTable: "receivables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_receivables_billing_slip_id",
                schema: "finance",
                table: "receivables",
                column: "billing_slip_id",
                filter: "billing_slip_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_billing_slips_status",
                schema: "finance",
                table: "billing_slips",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ux_billing_slips_account_reference",
                schema: "finance",
                table: "billing_slips",
                columns: new[] { "bank_account_id", "our_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_billing_slips_client_request",
                schema: "finance",
                table: "billing_slips",
                column: "client_request_id",
                unique: true,
                filter: "client_request_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_billing_slips_receivable_active",
                schema: "finance",
                table: "billing_slips",
                column: "receivable_id",
                unique: true,
                filter: "status IN ('Requested','Issued')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "billing_reference_sequences",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "billing_slips",
                schema: "finance");

            migrationBuilder.DropIndex(
                name: "ix_receivables_billing_slip_id",
                schema: "finance",
                table: "receivables");

            migrationBuilder.DropColumn(
                name: "billing_slip_id",
                schema: "finance",
                table: "receivables");
        }
    }
}
