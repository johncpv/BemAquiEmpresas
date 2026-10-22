using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductionQuality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PRD-05: as tentativas começam em 1 (as OS, etapas e itens que já existem ficam na tentativa 1). Não destrutiva.
            migrationBuilder.AddColumn<int>(
                name: "quality_attempt",
                schema: "production",
                table: "production_orders",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "quality_attempt_started_at",
                schema: "production",
                table: "production_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "self_inspection",
                schema: "production",
                table: "production_orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "attempt",
                schema: "production",
                table: "production_order_steps",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "decided_at",
                schema: "production",
                table: "production_order_steps",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "decided_by",
                schema: "production",
                table: "production_order_steps",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "self_inspection",
                schema: "production",
                table: "production_order_steps",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "attempt",
                schema: "production",
                table: "production_order_checklist",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "non_conformities",
                schema: "production",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_step_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    cause = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    action = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    responsible_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    return_to_step_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    result = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    opened_by = table.Column<Guid>(type: "uuid", nullable: true),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    action_started_by = table.Column<Guid>(type: "uuid", nullable: true),
                    action_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_non_conformities", x => x.id);
                    table.CheckConstraint("ck_non_conformities_attempt", "attempt >= 1");
                    table.ForeignKey(
                        name: "fk_non_conformities_production_order_steps_order_step_id",
                        column: x => x.order_step_id,
                        principalSchema: "production",
                        principalTable: "production_order_steps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_non_conformities_production_order_steps_return_to_step_id",
                        column: x => x.return_to_step_id,
                        principalSchema: "production",
                        principalTable: "production_order_steps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_non_conformities_production_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "production",
                        principalTable: "production_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "quality_decisions",
                schema: "production",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    result = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    self_inspection = table.Column<bool>(type: "boolean", nullable: false),
                    non_conformity_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quality_decisions", x => x.id);
                    table.ForeignKey(
                        name: "fk_quality_decisions_non_conformities_non_conformity_id",
                        column: x => x.non_conformity_id,
                        principalSchema: "production",
                        principalTable: "non_conformities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_quality_decisions_production_order_steps_step_id",
                        column: x => x.step_id,
                        principalSchema: "production",
                        principalTable: "production_order_steps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_quality_decisions_production_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "production",
                        principalTable: "production_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_non_conformities_number",
                schema: "production",
                table: "non_conformities",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_non_conformities_order_id",
                schema: "production",
                table: "non_conformities",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_non_conformities_order_step_id_attempt",
                schema: "production",
                table: "non_conformities",
                columns: new[] { "order_step_id", "attempt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_non_conformities_responsible_user_id_status",
                schema: "production",
                table: "non_conformities",
                columns: new[] { "responsible_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_non_conformities_return_to_step_id",
                schema: "production",
                table: "non_conformities",
                column: "return_to_step_id");

            migrationBuilder.CreateIndex(
                name: "ix_non_conformities_status",
                schema: "production",
                table: "non_conformities",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_quality_decisions_non_conformity_id",
                schema: "production",
                table: "quality_decisions",
                column: "non_conformity_id");

            migrationBuilder.CreateIndex(
                name: "ix_quality_decisions_order_id",
                schema: "production",
                table: "quality_decisions",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_quality_decisions_step_id_attempt",
                schema: "production",
                table: "quality_decisions",
                columns: new[] { "step_id", "attempt" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "quality_decisions",
                schema: "production");

            migrationBuilder.DropTable(
                name: "non_conformities",
                schema: "production");

            migrationBuilder.DropColumn(
                name: "quality_attempt",
                schema: "production",
                table: "production_orders");

            migrationBuilder.DropColumn(
                name: "quality_attempt_started_at",
                schema: "production",
                table: "production_orders");

            migrationBuilder.DropColumn(
                name: "self_inspection",
                schema: "production",
                table: "production_orders");

            migrationBuilder.DropColumn(
                name: "attempt",
                schema: "production",
                table: "production_order_steps");

            migrationBuilder.DropColumn(
                name: "decided_at",
                schema: "production",
                table: "production_order_steps");

            migrationBuilder.DropColumn(
                name: "decided_by",
                schema: "production",
                table: "production_order_steps");

            migrationBuilder.DropColumn(
                name: "self_inspection",
                schema: "production",
                table: "production_order_steps");

            migrationBuilder.DropColumn(
                name: "attempt",
                schema: "production",
                table: "production_order_checklist");
        }
    }
}
