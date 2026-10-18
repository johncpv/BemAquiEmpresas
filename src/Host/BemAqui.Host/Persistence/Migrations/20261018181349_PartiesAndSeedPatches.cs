using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PartiesAndSeedPatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "parties");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateTable(
                name: "parties",
                schema: "parties",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    document = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    legal_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    trade_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    search_text = table.Column<string>(type: "character varying(420)", maxLength: 420, nullable: false),
                    state_registration = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    municipal_registration = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    tax_regime = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    phone = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parties", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "seed_patches",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seed_patches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "party_addresses",
                schema: "parties",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    zip_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    street = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    complement = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    district = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ibge_code = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    state = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_party_addresses", x => x.id);
                    table.ForeignKey(
                        name: "fk_party_addresses_parties_party_id",
                        column: x => x.party_id,
                        principalSchema: "parties",
                        principalTable: "parties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "party_contacts",
                schema: "parties",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    phone = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: true),
                    receives_billing = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_party_contacts", x => x.id);
                    table.ForeignKey(
                        name: "fk_party_contacts_parties_party_id",
                        column: x => x.party_id,
                        principalSchema: "parties",
                        principalTable: "parties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "party_roles",
                schema: "parties",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_party_roles", x => x.id);
                    table.ForeignKey(
                        name: "fk_party_roles_parties_party_id",
                        column: x => x.party_id,
                        principalSchema: "parties",
                        principalTable: "parties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_parties_document_trgm",
                schema: "parties",
                table: "parties",
                column: "document")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_parties_document_unique",
                schema: "parties",
                table: "parties",
                column: "document",
                unique: true,
                filter: "NOT is_deleted AND document IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_parties_search_text_trgm",
                schema: "parties",
                table: "parties",
                column: "search_text")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_party_addresses_default",
                schema: "parties",
                table: "party_addresses",
                columns: new[] { "party_id", "kind" },
                unique: true,
                filter: "is_default");

            migrationBuilder.CreateIndex(
                name: "ix_party_contacts_party_id",
                schema: "parties",
                table: "party_contacts",
                column: "party_id");

            migrationBuilder.CreateIndex(
                name: "ix_party_roles_party_id_role",
                schema: "parties",
                table: "party_roles",
                columns: new[] { "party_id", "role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_party_roles_role",
                schema: "parties",
                table: "party_roles",
                column: "role");

            migrationBuilder.CreateIndex(
                name: "ix_seed_patches_key",
                schema: "identity",
                table: "seed_patches",
                column: "key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "party_addresses",
                schema: "parties");

            migrationBuilder.DropTable(
                name: "party_contacts",
                schema: "parties");

            migrationBuilder.DropTable(
                name: "party_roles",
                schema: "parties");

            migrationBuilder.DropTable(
                name: "seed_patches",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "parties",
                schema: "parties");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");
        }
    }
}
