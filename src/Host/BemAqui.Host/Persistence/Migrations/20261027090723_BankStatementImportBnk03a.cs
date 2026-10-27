using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BankStatementImportBnk03a : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bank_ofx_contingencies",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    granted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    valid_until = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_ofx_contingencies", x => x.id);
                    table.ForeignKey(
                        name: "fk_bank_ofx_contingencies_bank_accounts_bank_account_id",
                        column: x => x.bank_account_id,
                        principalSchema: "finance",
                        principalTable: "bank_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bank_statement_balances",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    source = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_statement_balances", x => x.id);
                    table.ForeignKey(
                        name: "fk_bank_statement_balances_bank_accounts_bank_account_id",
                        column: x => x.bank_account_id,
                        principalSchema: "finance",
                        principalTable: "bank_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bank_statement_batches",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    trigger = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    period_from = table.Column<DateOnly>(type: "date", nullable: true),
                    period_to = table.Column<DateOnly>(type: "date", nullable: true),
                    attachment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    file_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    bank_verified = table.Column<bool>(type: "boolean", nullable: false),
                    new_count = table.Column<int>(type: "integer", nullable: false),
                    duplicate_count = table.Column<int>(type: "integer", nullable: false),
                    rejected_count = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    imported_by = table.Column<Guid>(type: "uuid", nullable: true),
                    undone_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    undone_by = table.Column<Guid>(type: "uuid", nullable: true),
                    undo_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_statement_batches", x => x.id);
                    table.ForeignKey(
                        name: "fk_bank_statement_batches_bank_accounts_bank_account_id",
                        column: x => x.bank_account_id,
                        principalSchema: "finance",
                        principalTable: "bank_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bank_statement_lines",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    counterparty_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    counterparty_document = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    external_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    bank_verified = table.Column<bool>(type: "boolean", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ignore_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ignored_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ignored_by = table.Column<Guid>(type: "uuid", nullable: true),
                    matched_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_statement_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_bank_statement_lines_bank_accounts_bank_account_id",
                        column: x => x.bank_account_id,
                        principalSchema: "finance",
                        principalTable: "bank_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bank_statement_lines_bank_statement_batches_batch_id",
                        column: x => x.batch_id,
                        principalSchema: "finance",
                        principalTable: "bank_statement_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bank_ofx_contingencies_bank_account_id_valid_until",
                schema: "finance",
                table: "bank_ofx_contingencies",
                columns: new[] { "bank_account_id", "valid_until" });

            migrationBuilder.CreateIndex(
                name: "ix_bank_statement_balances_bank_account_id_date_source",
                schema: "finance",
                table: "bank_statement_balances",
                columns: new[] { "bank_account_id", "date", "source" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bank_statement_batches_bank_account_id_imported_at",
                schema: "finance",
                table: "bank_statement_batches",
                columns: new[] { "bank_account_id", "imported_at" });

            migrationBuilder.CreateIndex(
                name: "ux_bank_statement_batches_account_file",
                schema: "finance",
                table: "bank_statement_batches",
                columns: new[] { "bank_account_id", "file_sha256" },
                unique: true,
                filter: "file_sha256 IS NOT NULL AND undone_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_bank_statement_lines_bank_account_id_date",
                schema: "finance",
                table: "bank_statement_lines",
                columns: new[] { "bank_account_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_bank_statement_lines_bank_account_id_external_id",
                schema: "finance",
                table: "bank_statement_lines",
                columns: new[] { "bank_account_id", "external_id" });

            migrationBuilder.CreateIndex(
                name: "ix_bank_statement_lines_batch_id",
                schema: "finance",
                table: "bank_statement_lines",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ux_bank_statement_lines_account_source_external",
                schema: "finance",
                table: "bank_statement_lines",
                columns: new[] { "bank_account_id", "source", "external_id" },
                unique: true,
                filter: "status <> 'Undone'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bank_ofx_contingencies",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "bank_statement_balances",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "bank_statement_lines",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "bank_statement_batches",
                schema: "finance");
        }
    }
}
