using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProfileSeedKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "seed_key",
                schema: "parties",
                table: "parties",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "seed_key",
                schema: "finance",
                table: "bank_accounts",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_parties_seed_key",
                schema: "parties",
                table: "parties",
                column: "seed_key",
                unique: true,
                filter: "seed_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_bank_accounts_seed_key",
                schema: "finance",
                table: "bank_accounts",
                column: "seed_key",
                unique: true,
                filter: "seed_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_parties_seed_key",
                schema: "parties",
                table: "parties");

            migrationBuilder.DropIndex(
                name: "ix_bank_accounts_seed_key",
                schema: "finance",
                table: "bank_accounts");

            migrationBuilder.DropColumn(
                name: "seed_key",
                schema: "parties",
                table: "parties");

            migrationBuilder.DropColumn(
                name: "seed_key",
                schema: "finance",
                table: "bank_accounts");
        }
    }
}
