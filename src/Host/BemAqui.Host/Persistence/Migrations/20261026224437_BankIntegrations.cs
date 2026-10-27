using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BankIntegrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "provider",
                schema: "finance",
                table: "bank_accounts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.CreateTable(
                name: "bank_integrations",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    environment = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    client_id_cipher = table.Column<string>(type: "text", nullable: true),
                    client_id_hint = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    client_secret_cipher = table.Column<string>(type: "text", nullable: true),
                    certificate_cipher = table.Column<string>(type: "text", nullable: true),
                    private_key_cipher = table.Column<string>(type: "text", nullable: true),
                    certificate_password_cipher = table.Column<string>(type: "text", nullable: true),
                    certificate_format = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    certificate_not_after = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    certificate_thumbprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    certificate_subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    account_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    granted_scopes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    configured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    configured_by = table.Column<Guid>(type: "uuid", nullable: true),
                    rotated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rotated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    last_test_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_test_outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    last_test_message = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    disabled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    disabled_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_integrations", x => x.id);
                    table.ForeignKey(
                        name: "fk_bank_integrations_bank_accounts_bank_account_id",
                        column: x => x.bank_account_id,
                        principalSchema: "finance",
                        principalTable: "bank_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bank_integrations_bank_account_id",
                schema: "finance",
                table: "bank_integrations",
                column: "bank_account_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bank_integrations",
                schema: "finance");

            migrationBuilder.DropColumn(
                name: "provider",
                schema: "finance",
                table: "bank_accounts");
        }
    }
}
