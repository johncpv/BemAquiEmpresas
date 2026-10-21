using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CommercialQuoteDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid[]>(
                name: "attachment_ids",
                schema: "shared",
                table: "email_logs",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);

            migrationBuilder.AddColumn<string>(
                name: "cc",
                schema: "shared",
                table: "email_logs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "from_name",
                schema: "shared",
                table: "email_logs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reply_to",
                schema: "shared",
                table: "email_logs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "scan_waits",
                schema: "shared",
                table: "email_logs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "origin",
                schema: "shared",
                table: "attachments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "quote_dispatches",
                schema: "commercial",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    quote_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email_log_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attachment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    cc = table.Column<string>(type: "character varying(800)", maxLength: 800, nullable: true),
                    sent_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quote_dispatches", x => x.id);
                    table.ForeignKey(
                        name: "fk_quote_dispatches_quotes_quote_id",
                        column: x => x.quote_id,
                        principalSchema: "commercial",
                        principalTable: "quotes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_quote_dispatches_created_at",
                schema: "commercial",
                table: "quote_dispatches",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_quote_dispatches_quote_id",
                schema: "commercial",
                table: "quote_dispatches",
                column: "quote_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "quote_dispatches",
                schema: "commercial");

            migrationBuilder.DropColumn(
                name: "attachment_ids",
                schema: "shared",
                table: "email_logs");

            migrationBuilder.DropColumn(
                name: "cc",
                schema: "shared",
                table: "email_logs");

            migrationBuilder.DropColumn(
                name: "from_name",
                schema: "shared",
                table: "email_logs");

            migrationBuilder.DropColumn(
                name: "reply_to",
                schema: "shared",
                table: "email_logs");

            migrationBuilder.DropColumn(
                name: "scan_waits",
                schema: "shared",
                table: "email_logs");

            migrationBuilder.DropColumn(
                name: "origin",
                schema: "shared",
                table: "attachments");
        }
    }
}
