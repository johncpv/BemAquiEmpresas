using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InventoryTraceability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "corrected_at",
                schema: "inventory",
                table: "lots",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "corrected_by",
                schema: "inventory",
                table: "lots",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "correction_reason",
                schema: "inventory",
                table: "lots",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_serials_serial_number",
                schema: "inventory",
                table: "serials",
                column: "serial_number")
                .Annotation("Npgsql:IndexOperators", new[] { "text_pattern_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_lots_lot_number",
                schema: "inventory",
                table: "lots",
                column: "lot_number")
                .Annotation("Npgsql:IndexOperators", new[] { "text_pattern_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_serials_serial_number",
                schema: "inventory",
                table: "serials");

            migrationBuilder.DropIndex(
                name: "ix_lots_lot_number",
                schema: "inventory",
                table: "lots");

            migrationBuilder.DropColumn(
                name: "corrected_at",
                schema: "inventory",
                table: "lots");

            migrationBuilder.DropColumn(
                name: "corrected_by",
                schema: "inventory",
                table: "lots");

            migrationBuilder.DropColumn(
                name: "correction_reason",
                schema: "inventory",
                table: "lots");
        }
    }
}
