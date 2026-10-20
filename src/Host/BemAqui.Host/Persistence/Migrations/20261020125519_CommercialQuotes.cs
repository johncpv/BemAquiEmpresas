using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CommercialQuotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "commercial");

            migrationBuilder.CreateTable(
                name: "parameters",
                schema: "commercial",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    value = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_commercial_parameters", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "quotes",
                schema: "commercial",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    is_current = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    site_address_id = table.Column<Guid>(type: "uuid", nullable: true),
                    delivery_days = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    pricing_mode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    total_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    margin_pct = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quotes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "quote_items",
                schema: "commercial",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    quote_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    pct = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: true),
                    unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    price_overridden = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quote_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_quote_items_quotes_quote_id",
                        column: x => x.quote_id,
                        principalSchema: "commercial",
                        principalTable: "quotes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "quote_item_costs",
                schema: "commercial",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    quote_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cost_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    work_center_id = table.Column<Guid>(type: "uuid", nullable: true),
                    position_id = table.Column<Guid>(type: "uuid", nullable: true),
                    party_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quote_item_costs", x => x.id);
                    table.ForeignKey(
                        name: "fk_quote_item_costs_quote_items_quote_item_id",
                        column: x => x.quote_item_id,
                        principalSchema: "commercial",
                        principalTable: "quote_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_commercial_parameters_key",
                schema: "commercial",
                table: "parameters",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_quote_item_costs_item_id",
                schema: "commercial",
                table: "quote_item_costs",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_quote_item_costs_party_id",
                schema: "commercial",
                table: "quote_item_costs",
                column: "party_id");

            migrationBuilder.CreateIndex(
                name: "ix_quote_item_costs_quote_item_id",
                schema: "commercial",
                table: "quote_item_costs",
                column: "quote_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_quote_item_costs_work_center_id",
                schema: "commercial",
                table: "quote_item_costs",
                column: "work_center_id");

            migrationBuilder.CreateIndex(
                name: "ix_quote_items_item_id",
                schema: "commercial",
                table: "quote_items",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_quote_items_quote_id",
                schema: "commercial",
                table: "quote_items",
                column: "quote_id");

            migrationBuilder.CreateIndex(
                name: "ix_quotes_number_revision",
                schema: "commercial",
                table: "quotes",
                columns: new[] { "number", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_quotes_party_id",
                schema: "commercial",
                table: "quotes",
                column: "party_id");

            migrationBuilder.CreateIndex(
                name: "ux_quotes_number_current",
                schema: "commercial",
                table: "quotes",
                column: "number",
                unique: true,
                filter: "is_current");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "parameters",
                schema: "commercial");

            migrationBuilder.DropTable(
                name: "quote_item_costs",
                schema: "commercial");

            migrationBuilder.DropTable(
                name: "quote_items",
                schema: "commercial");

            migrationBuilder.DropTable(
                name: "quotes",
                schema: "commercial");
        }
    }
}
