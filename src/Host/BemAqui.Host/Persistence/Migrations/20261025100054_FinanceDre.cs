using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinanceDre : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "review_state",
                schema: "finance",
                table: "parameters",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reviewed_at",
                schema: "finance",
                table: "parameters",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reviewed_by",
                schema: "finance",
                table: "parameters",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "text",
                schema: "finance",
                table: "parameters",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "dre_view_marks",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    viewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dre_view_marks", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_receivables_issue_date",
                schema: "finance",
                table: "receivables",
                columns: new[] { "issue_date", "account_id" })
                .Annotation("Npgsql:IndexInclude", new[] { "amount" });

            migrationBuilder.CreateIndex(
                name: "ix_payables_competence",
                schema: "finance",
                table: "payables",
                column: "competence",
                filter: "competence IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payables_issue_date",
                schema: "finance",
                table: "payables",
                columns: new[] { "issue_date", "account_id" })
                .Annotation("Npgsql:IndexInclude", new[] { "amount" });

            migrationBuilder.CreateIndex(
                name: "ix_dre_view_marks_user_id",
                schema: "finance",
                table: "dre_view_marks",
                column: "user_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dre_view_marks",
                schema: "finance");

            migrationBuilder.DropIndex(
                name: "ix_receivables_issue_date",
                schema: "finance",
                table: "receivables");

            migrationBuilder.DropIndex(
                name: "ix_payables_competence",
                schema: "finance",
                table: "payables");

            migrationBuilder.DropIndex(
                name: "ix_payables_issue_date",
                schema: "finance",
                table: "payables");

            migrationBuilder.DropColumn(
                name: "review_state",
                schema: "finance",
                table: "parameters");

            migrationBuilder.DropColumn(
                name: "reviewed_at",
                schema: "finance",
                table: "parameters");

            migrationBuilder.DropColumn(
                name: "reviewed_by",
                schema: "finance",
                table: "parameters");

            migrationBuilder.DropColumn(
                name: "text",
                schema: "finance",
                table: "parameters");
        }
    }
}
