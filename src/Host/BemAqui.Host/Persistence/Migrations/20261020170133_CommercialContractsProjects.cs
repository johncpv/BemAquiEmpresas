using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CommercialContractsProjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "approved_at",
                schema: "commercial",
                table: "quotes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "approved_by",
                schema: "commercial",
                table: "quotes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cost_type",
                schema: "finance",
                table: "payables",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "contracts",
                schema: "commercial",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    quote_id = table.Column<Guid>(type: "uuid", nullable: false),
                    party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    signed_at = table.Column<DateOnly>(type: "date", nullable: false),
                    total_value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    revenue_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contracts", x => x.id);
                    table.ForeignKey(
                        name: "fk_contracts_quotes_quote_id",
                        column: x => x.quote_id,
                        principalSchema: "commercial",
                        principalTable: "quotes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contract_milestones",
                schema: "commercial",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    percent = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    trigger = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    trigger_event = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    days_after_event = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_milestones", x => x.id);
                    table.ForeignKey(
                        name: "fk_contract_milestones_contracts_contract_id",
                        column: x => x.contract_id,
                        principalSchema: "commercial",
                        principalTable: "contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "projects",
                schema: "commercial",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    site_address_id = table.Column<Guid>(type: "uuid", nullable: true),
                    promised_date = table.Column<DateOnly>(type: "date", nullable: true),
                    forecast_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: true),
                    manager_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_projects", x => x.id);
                    table.ForeignKey(
                        name: "fk_projects_contracts_contract_id",
                        column: x => x.contract_id,
                        principalSchema: "commercial",
                        principalTable: "contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "project_budget",
                schema: "commercial",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cost_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    budgeted_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_budget", x => x.id);
                    table.ForeignKey(
                        name: "fk_project_budget_projects_project_id",
                        column: x => x.project_id,
                        principalSchema: "commercial",
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "project_items",
                schema: "commercial",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quote_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_project_items_projects_project_id",
                        column: x => x.project_id,
                        principalSchema: "commercial",
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_project_items_quote_items_quote_item_id",
                        column: x => x.quote_item_id,
                        principalSchema: "commercial",
                        principalTable: "quote_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payables_project_id",
                schema: "finance",
                table: "payables",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_contract_milestones_contract_id",
                schema: "commercial",
                table: "contract_milestones",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "ix_contracts_number",
                schema: "commercial",
                table: "contracts",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contracts_party_id",
                schema: "commercial",
                table: "contracts",
                column: "party_id");

            migrationBuilder.CreateIndex(
                name: "ix_contracts_quote_id",
                schema: "commercial",
                table: "contracts",
                column: "quote_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_budget_project_id_cost_type",
                schema: "commercial",
                table: "project_budget",
                columns: new[] { "project_id", "cost_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_items_item_id",
                schema: "commercial",
                table: "project_items",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_items_project_id",
                schema: "commercial",
                table: "project_items",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_items_quote_item_id",
                schema: "commercial",
                table: "project_items",
                column: "quote_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_projects_contract_id",
                schema: "commercial",
                table: "projects",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "ix_projects_number",
                schema: "commercial",
                table: "projects",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_projects_party_id",
                schema: "commercial",
                table: "projects",
                column: "party_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contract_milestones",
                schema: "commercial");

            migrationBuilder.DropTable(
                name: "project_budget",
                schema: "commercial");

            migrationBuilder.DropTable(
                name: "project_items",
                schema: "commercial");

            migrationBuilder.DropTable(
                name: "projects",
                schema: "commercial");

            migrationBuilder.DropTable(
                name: "contracts",
                schema: "commercial");

            migrationBuilder.DropIndex(
                name: "ix_payables_project_id",
                schema: "finance",
                table: "payables");

            migrationBuilder.DropColumn(
                name: "approved_at",
                schema: "commercial",
                table: "quotes");

            migrationBuilder.DropColumn(
                name: "approved_by",
                schema: "commercial",
                table: "quotes");

            migrationBuilder.DropColumn(
                name: "cost_type",
                schema: "finance",
                table: "payables");
        }
    }
}
