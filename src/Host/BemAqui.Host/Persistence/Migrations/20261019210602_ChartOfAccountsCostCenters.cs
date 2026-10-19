using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChartOfAccountsCostCenters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chart_of_accounts",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    dre_group = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    accepts_entries = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    seed_key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chart_of_accounts", x => x.id);
                    table.ForeignKey(
                        name: "fk_chart_of_accounts_chart_of_accounts_parent_id",
                        column: x => x.parent_id,
                        principalSchema: "finance",
                        principalTable: "chart_of_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cost_centers",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    seed_key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cost_centers", x => x.id);
                    table.ForeignKey(
                        name: "fk_cost_centers_cost_centers_parent_id",
                        column: x => x.parent_id,
                        principalSchema: "finance",
                        principalTable: "cost_centers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chart_of_accounts_code",
                schema: "finance",
                table: "chart_of_accounts",
                column: "code",
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_chart_of_accounts_parent_id",
                schema: "finance",
                table: "chart_of_accounts",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_chart_of_accounts_seed_key",
                schema: "finance",
                table: "chart_of_accounts",
                column: "seed_key",
                unique: true,
                filter: "seed_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_cost_centers_code",
                schema: "finance",
                table: "cost_centers",
                column: "code",
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_cost_centers_parent_id",
                schema: "finance",
                table: "cost_centers",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_cost_centers_seed_key",
                schema: "finance",
                table: "cost_centers",
                column: "seed_key",
                unique: true,
                filter: "seed_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chart_of_accounts",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "cost_centers",
                schema: "finance");
        }
    }
}
