using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContractReceivablesMilestones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "contract_id",
                schema: "finance",
                table: "receivables",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "days_after_event",
                schema: "finance",
                table: "receivables",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "trigger_event",
                schema: "finance",
                table: "receivables",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "closed_at",
                schema: "commercial",
                table: "projects",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "delivered_manually",
                schema: "commercial",
                table: "projects",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "delivered_on",
                schema: "commercial",
                table: "projects",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "fabricated_on",
                schema: "commercial",
                table: "projects",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cancel_reason",
                schema: "commercial",
                table: "contracts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cancelled_at",
                schema: "commercial",
                table: "contracts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "cancelled_by",
                schema: "commercial",
                table: "contracts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "completed_at",
                schema: "commercial",
                table: "contracts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "project_trigger_events",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    @event = table.Column<string>(name: "event", type: "character varying(24)", maxLength: 24, nullable: false),
                    occurred_on = table.Column<DateOnly>(type: "date", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_trigger_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_receivables_contract_id",
                schema: "finance",
                table: "receivables",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "ix_receivables_project_id",
                schema: "finance",
                table: "receivables",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ux_receivables_contract_milestone",
                schema: "finance",
                table: "receivables",
                column: "origin_id",
                unique: true,
                filter: "origin = 'Contract'");

            migrationBuilder.CreateIndex(
                name: "ux_project_trigger_events_project_event",
                schema: "finance",
                table: "project_trigger_events",
                columns: new[] { "project_id", "event" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "project_trigger_events",
                schema: "finance");

            migrationBuilder.DropIndex(
                name: "ix_receivables_contract_id",
                schema: "finance",
                table: "receivables");

            migrationBuilder.DropIndex(
                name: "ix_receivables_project_id",
                schema: "finance",
                table: "receivables");

            migrationBuilder.DropIndex(
                name: "ux_receivables_contract_milestone",
                schema: "finance",
                table: "receivables");

            migrationBuilder.DropColumn(
                name: "contract_id",
                schema: "finance",
                table: "receivables");

            migrationBuilder.DropColumn(
                name: "days_after_event",
                schema: "finance",
                table: "receivables");

            migrationBuilder.DropColumn(
                name: "trigger_event",
                schema: "finance",
                table: "receivables");

            migrationBuilder.DropColumn(
                name: "closed_at",
                schema: "commercial",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "delivered_manually",
                schema: "commercial",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "delivered_on",
                schema: "commercial",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "fabricated_on",
                schema: "commercial",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "cancel_reason",
                schema: "commercial",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "cancelled_at",
                schema: "commercial",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "cancelled_by",
                schema: "commercial",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "completed_at",
                schema: "commercial",
                table: "contracts");
        }
    }
}
