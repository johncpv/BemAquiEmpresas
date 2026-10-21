using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContractAmendments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contract_amendments",
                schema: "commercial",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    value_delta = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    days_delta = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    budget_deltas = table.Column<string>(type: "jsonb", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    self_approved = table.Column<bool>(type: "boolean", nullable: false),
                    rejection_comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_amendments", x => x.id);
                    table.ForeignKey(
                        name: "fk_contract_amendments_contracts_contract_id",
                        column: x => x.contract_id,
                        principalSchema: "commercial",
                        principalTable: "contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contract_amendment_milestones",
                schema: "commercial",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    amendment_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_contract_amendment_milestones", x => x.id);
                    table.ForeignKey(
                        name: "fk_contract_amendment_milestones_contract_amendments_amendment",
                        column: x => x.amendment_id,
                        principalSchema: "commercial",
                        principalTable: "contract_amendments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contract_amendment_reductions",
                schema: "commercial",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    amendment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receivable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    new_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_amendment_reductions", x => x.id);
                    table.ForeignKey(
                        name: "fk_contract_amendment_reductions_contract_amendments_amendment",
                        column: x => x.amendment_id,
                        principalSchema: "commercial",
                        principalTable: "contract_amendments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contract_amendment_milestones_amendment_id",
                schema: "commercial",
                table: "contract_amendment_milestones",
                column: "amendment_id");

            migrationBuilder.CreateIndex(
                name: "ix_contract_amendment_reductions_amendment_id_receivable_id",
                schema: "commercial",
                table: "contract_amendment_reductions",
                columns: new[] { "amendment_id", "receivable_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contract_amendments_contract_id_number",
                schema: "commercial",
                table: "contract_amendments",
                columns: new[] { "contract_id", "number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contract_amendment_milestones",
                schema: "commercial");

            migrationBuilder.DropTable(
                name: "contract_amendment_reductions",
                schema: "commercial");

            migrationBuilder.DropTable(
                name: "contract_amendments",
                schema: "commercial");
        }
    }
}
