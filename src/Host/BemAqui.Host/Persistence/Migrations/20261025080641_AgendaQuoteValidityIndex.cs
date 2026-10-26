using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgendaQuoteValidityIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_quotes_sent_valid_until",
                schema: "commercial",
                table: "quotes",
                column: "valid_until",
                filter: "is_current AND status = 'Sent'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_quotes_sent_valid_until",
                schema: "commercial",
                table: "quotes");
        }
    }
}
