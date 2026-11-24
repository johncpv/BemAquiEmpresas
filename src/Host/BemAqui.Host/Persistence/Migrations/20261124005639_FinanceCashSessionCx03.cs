using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinanceCashSessionCx03 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "pos_cash_session_id",
                schema: "finance",
                table: "bank_transfers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_receivables_cash_session_id",
                schema: "finance",
                table: "receivables",
                column: "cash_session_id",
                filter: "cash_session_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_bank_transfers_pos_cash_session_id",
                schema: "finance",
                table: "bank_transfers",
                column: "pos_cash_session_id",
                filter: "pos_cash_session_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_receivables_cash_session_id",
                schema: "finance",
                table: "receivables");

            migrationBuilder.DropIndex(
                name: "ix_bank_transfers_pos_cash_session_id",
                schema: "finance",
                table: "bank_transfers");

            migrationBuilder.DropColumn(
                name: "pos_cash_session_id",
                schema: "finance",
                table: "bank_transfers");
        }
    }
}
