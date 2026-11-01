using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceReceiptFis04b : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "input_tax_credit_amount",
                schema: "fiscal",
                table: "incoming_invoices",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "input_tax_credit_applied",
                schema: "fiscal",
                table: "incoming_invoices",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "insurance",
                schema: "purchasing",
                table: "goods_receipts",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "origin",
                schema: "purchasing",
                table: "goods_receipts",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "Manual");

            migrationBuilder.AddColumn<decimal>(
                name: "other_expenses",
                schema: "purchasing",
                table: "goods_receipts",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "tax_credit",
                schema: "purchasing",
                table: "goods_receipts",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "insurance_share",
                schema: "purchasing",
                table: "goods_receipt_lines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "other_share",
                schema: "purchasing",
                table: "goods_receipt_lines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "tax_credit_share",
                schema: "purchasing",
                table: "goods_receipt_lines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string[]>(
                name: "input_tax_credit",
                schema: "fiscal",
                table: "fiscal_settings",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.CreateIndex(
                name: "ix_incoming_invoices_goods_receipt_id",
                schema: "fiscal",
                table: "incoming_invoices",
                column: "goods_receipt_id",
                filter: "goods_receipt_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_goods_receipts_incoming_invoice",
                schema: "purchasing",
                table: "goods_receipts",
                column: "incoming_invoice_id",
                unique: true,
                filter: "incoming_invoice_id IS NOT NULL AND status = 'Completed'");

            // FIS-04b: o valor do recebimento passa a somar o seguro e as outras despesas da nota (as linhas antigas têm zero nas colunas
            // novas, então a regra nova vale para elas); a origem Invoice sempre com a nota. Só troca de CHECK: nada destrutivo.
            migrationBuilder.Sql("""
                ALTER TABLE purchasing.goods_receipts
                    DROP CONSTRAINT IF EXISTS ck_goods_receipts_amounts,
                    ADD CONSTRAINT ck_goods_receipts_amounts CHECK (subtotal >= 0 AND freight >= 0 AND ipi_amount >= 0 AND insurance >= 0 AND other_expenses >= 0 AND tax_credit >= 0
                        AND amount > 0 AND amount = subtotal + freight + ipi_amount + insurance + other_expenses),
                    ADD CONSTRAINT ck_goods_receipts_origin CHECK (origin IN ('Manual', 'Invoice') AND ((origin = 'Invoice') = (incoming_invoice_id IS NOT NULL)));
                ALTER TABLE purchasing.goods_receipt_lines
                    ADD CONSTRAINT ck_goods_receipt_lines_invoice_shares CHECK (insurance_share >= 0 AND other_share >= 0 AND tax_credit_share >= 0);
                ALTER TABLE fiscal.incoming_invoices
                    ADD CONSTRAINT ck_incoming_invoices_received CHECK ((status = 'Received') = (goods_receipt_id IS NOT NULL) AND input_tax_credit_amount >= 0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE fiscal.incoming_invoices DROP CONSTRAINT IF EXISTS ck_incoming_invoices_received;
                ALTER TABLE purchasing.goods_receipt_lines DROP CONSTRAINT IF EXISTS ck_goods_receipt_lines_invoice_shares;
                ALTER TABLE purchasing.goods_receipts DROP CONSTRAINT IF EXISTS ck_goods_receipts_origin, DROP CONSTRAINT IF EXISTS ck_goods_receipts_amounts,
                    ADD CONSTRAINT ck_goods_receipts_amounts CHECK (subtotal >= 0 AND freight >= 0 AND ipi_amount >= 0 AND amount > 0 AND amount = subtotal + freight + ipi_amount);
                """);

            migrationBuilder.DropIndex(
                name: "ix_incoming_invoices_goods_receipt_id",
                schema: "fiscal",
                table: "incoming_invoices");

            migrationBuilder.DropIndex(
                name: "ux_goods_receipts_incoming_invoice",
                schema: "purchasing",
                table: "goods_receipts");

            migrationBuilder.DropColumn(
                name: "input_tax_credit_amount",
                schema: "fiscal",
                table: "incoming_invoices");

            migrationBuilder.DropColumn(
                name: "input_tax_credit_applied",
                schema: "fiscal",
                table: "incoming_invoices");

            migrationBuilder.DropColumn(
                name: "insurance",
                schema: "purchasing",
                table: "goods_receipts");

            migrationBuilder.DropColumn(
                name: "origin",
                schema: "purchasing",
                table: "goods_receipts");

            migrationBuilder.DropColumn(
                name: "other_expenses",
                schema: "purchasing",
                table: "goods_receipts");

            migrationBuilder.DropColumn(
                name: "tax_credit",
                schema: "purchasing",
                table: "goods_receipts");

            migrationBuilder.DropColumn(
                name: "insurance_share",
                schema: "purchasing",
                table: "goods_receipt_lines");

            migrationBuilder.DropColumn(
                name: "other_share",
                schema: "purchasing",
                table: "goods_receipt_lines");

            migrationBuilder.DropColumn(
                name: "tax_credit_share",
                schema: "purchasing",
                table: "goods_receipt_lines");

            migrationBuilder.DropColumn(
                name: "input_tax_credit",
                schema: "fiscal",
                table: "fiscal_settings");
        }
    }
}
