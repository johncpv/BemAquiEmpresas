using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CommercialQuoteConditions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "copied_from_quote_id",
                schema: "commercial",
                table: "quotes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "lost_at",
                schema: "commercial",
                table: "quotes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "lost_by",
                schema: "commercial",
                table: "quotes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "lost_detail",
                schema: "commercial",
                table: "quotes",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "lost_reason",
                schema: "commercial",
                table: "quotes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "valid_until",
                schema: "commercial",
                table: "quotes",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "quote_milestones",
                schema: "commercial",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    quote_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_quote_milestones", x => x.id);
                    table.ForeignKey(
                        name: "fk_quote_milestones_quotes_quote_id",
                        column: x => x.quote_id,
                        principalSchema: "commercial",
                        principalTable: "quotes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_quote_milestones_quote_id",
                schema: "commercial",
                table: "quote_milestones",
                column: "quote_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "quote_milestones",
                schema: "commercial");

            migrationBuilder.DropColumn(
                name: "copied_from_quote_id",
                schema: "commercial",
                table: "quotes");

            migrationBuilder.DropColumn(
                name: "lost_at",
                schema: "commercial",
                table: "quotes");

            migrationBuilder.DropColumn(
                name: "lost_by",
                schema: "commercial",
                table: "quotes");

            migrationBuilder.DropColumn(
                name: "lost_detail",
                schema: "commercial",
                table: "quotes");

            migrationBuilder.DropColumn(
                name: "lost_reason",
                schema: "commercial",
                table: "quotes");

            migrationBuilder.DropColumn(
                name: "valid_until",
                schema: "commercial",
                table: "quotes");
        }
    }
}
