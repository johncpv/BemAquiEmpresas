using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PayrollClosing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "closed_charges",
                schema: "hr",
                table: "payroll_runs",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "closed_net",
                schema: "hr",
                table: "payroll_runs",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "closed_payables",
                schema: "hr",
                table: "payroll_runs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "paid_at",
                schema: "hr",
                table: "payroll_runs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "payment_date",
                schema: "hr",
                table: "payroll_runs",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reopen_reason",
                schema: "hr",
                table: "payroll_runs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reopened_at",
                schema: "hr",
                table: "payroll_runs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reopened_by",
                schema: "hr",
                table: "payroll_runs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "self_paid",
                schema: "finance",
                table: "payment_orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "payment_method",
                schema: "finance",
                table: "payables",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "party_id",
                schema: "finance",
                table: "payables",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "payee_id",
                schema: "finance",
                table: "payables",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "payee_type",
                schema: "finance",
                table: "payables",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Party");

            // RH-02c CA 6 (ADR 0036 §1, expansão): os títulos existentes são de terceiro e o favorecido é o próprio terceiro; o CHECK entra
            // depois do backfill. O código N-1 continua lendo party_id (só a versão nova cria título de funcionário, com party_id nulo).
            migrationBuilder.Sql("UPDATE finance.payables SET payee_type = 'Party', payee_id = party_id WHERE party_id IS NOT NULL;");

            // N-1 tolerante: a versão anterior grava e troca só party_id; o gatilho mantém payee_id = party_id no título de terceiro (CHECK).
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION finance.payables_payee_default() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW.payee_type = 'Party' AND NEW.party_id IS NOT NULL THEN
                        NEW.payee_id := NEW.party_id;
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER trg_payables_payee_default BEFORE INSERT OR UPDATE OF party_id ON finance.payables
                    FOR EACH ROW EXECUTE FUNCTION finance.payables_payee_default();
                """);

            migrationBuilder.CreateTable(
                name: "payroll_forecasts",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    period = table.Column<DateOnly>(type: "date", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    charge_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    source_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_forecasts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payables_payee_type_payee_id",
                schema: "finance",
                table: "payables",
                columns: new[] { "payee_type", "payee_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_payables_payee",
                schema: "finance",
                table: "payables",
                sql: "(payee_type = 'Party' AND party_id IS NOT NULL AND payee_id = party_id) OR (payee_type = 'Employee' AND party_id IS NULL AND payee_id IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_forecasts_period_kind",
                schema: "hr",
                table: "payroll_forecasts",
                columns: new[] { "period", "kind" },
                filter: "cancelled_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payroll_forecasts",
                schema: "hr");

            migrationBuilder.DropIndex(
                name: "ix_payables_payee_type_payee_id",
                schema: "finance",
                table: "payables");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payables_payee",
                schema: "finance",
                table: "payables");

            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_payables_payee_default ON finance.payables; DROP FUNCTION IF EXISTS finance.payables_payee_default();");

            migrationBuilder.DropColumn(
                name: "closed_charges",
                schema: "hr",
                table: "payroll_runs");

            migrationBuilder.DropColumn(
                name: "closed_net",
                schema: "hr",
                table: "payroll_runs");

            migrationBuilder.DropColumn(
                name: "closed_payables",
                schema: "hr",
                table: "payroll_runs");

            migrationBuilder.DropColumn(
                name: "paid_at",
                schema: "hr",
                table: "payroll_runs");

            migrationBuilder.DropColumn(
                name: "payment_date",
                schema: "hr",
                table: "payroll_runs");

            migrationBuilder.DropColumn(
                name: "reopen_reason",
                schema: "hr",
                table: "payroll_runs");

            migrationBuilder.DropColumn(
                name: "reopened_at",
                schema: "hr",
                table: "payroll_runs");

            migrationBuilder.DropColumn(
                name: "reopened_by",
                schema: "hr",
                table: "payroll_runs");

            migrationBuilder.DropColumn(
                name: "self_paid",
                schema: "finance",
                table: "payment_orders");

            migrationBuilder.DropColumn(
                name: "payee_id",
                schema: "finance",
                table: "payables");

            migrationBuilder.DropColumn(
                name: "payee_type",
                schema: "finance",
                table: "payables");

            migrationBuilder.AlterColumn<string>(
                name: "payment_method",
                schema: "finance",
                table: "payables",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "party_id",
                schema: "finance",
                table: "payables",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
