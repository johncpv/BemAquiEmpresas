using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TaxRulesFis02b : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tax_rules",
                schema: "fiscal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_version = table.Column<int>(type: "integer", nullable: false),
                    operation = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    nature_of_operation = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    cfop_internal = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    cfop_interstate = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    crt_scope = table.Column<int>(type: "integer", nullable: false),
                    icms = table.Column<string>(type: "jsonb", nullable: true),
                    icms_st = table.Column<string>(type: "jsonb", nullable: true),
                    fcp = table.Column<string>(type: "jsonb", nullable: true),
                    ipi = table.Column<string>(type: "jsonb", nullable: true),
                    pis = table.Column<string>(type: "jsonb", nullable: true),
                    cofins = table.Column<string>(type: "jsonb", nullable: true),
                    iss = table.Column<string>(type: "jsonb", nullable: true),
                    ibs_cbs = table.Column<string>(type: "jsonb", nullable: true),
                    difal = table.Column<string>(type: "jsonb", nullable: true),
                    cost_taxes = table.Column<string[]>(type: "text[]", nullable: false),
                    complementary_info = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    conditions = table.Column<string>(type: "jsonb", nullable: false),
                    is_seed = table.Column<bool>(type: "boolean", nullable: false),
                    seed_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    needs_review = table.Column<bool>(type: "boolean", nullable: false),
                    editors = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    activated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    self_activated = table.Column<bool>(type: "boolean", nullable: false),
                    confirmed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deactivated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deactivation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tax_rules", x => x.id);
                    table.CheckConstraint("ck_tax_rules_validity", "valid_to IS NULL OR valid_to >= valid_from");
                    table.CheckConstraint("ck_tax_rules_version", "rule_version >= 1");
                });

            migrationBuilder.CreateIndex(
                name: "ix_tax_rules_status_operation",
                schema: "fiscal",
                table: "tax_rules",
                columns: new[] { "status", "operation" });

            migrationBuilder.CreateIndex(
                name: "ux_tax_rules_one_draft",
                schema: "fiscal",
                table: "tax_rules",
                column: "rule_id",
                unique: true,
                filter: "status = 'Draft'");

            migrationBuilder.CreateIndex(
                name: "ux_tax_rules_rule_version",
                schema: "fiscal",
                table: "tax_rules",
                columns: new[] { "rule_id", "rule_version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tax_rules",
                schema: "fiscal");
        }
    }
}
