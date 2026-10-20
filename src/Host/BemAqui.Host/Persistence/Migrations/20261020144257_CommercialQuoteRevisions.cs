using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CommercialQuoteRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expired_at",
                schema: "commercial",
                table: "quotes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "parent_quote_id",
                schema: "commercial",
                table: "quotes",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "expired_at",
                schema: "commercial",
                table: "quotes");

            migrationBuilder.DropColumn(
                name: "parent_quote_id",
                schema: "commercial",
                table: "quotes");
        }
    }
}
