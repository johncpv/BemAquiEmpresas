using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Platform.Infrastructure.Catalog.Migrations
{
    /// <inheritdoc />
    public partial class ProvisioningAndSchemaVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "migrated_at",
                schema: "catalog",
                table: "tenants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "schema_version",
                schema: "catalog",
                table: "tenants",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "provisioning_logs",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provisioning_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_provisioning_logs_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "catalog",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_provisioning_logs_tenant_id_at",
                schema: "catalog",
                table: "provisioning_logs",
                columns: new[] { "tenant_id", "at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "provisioning_logs",
                schema: "catalog");

            migrationBuilder.DropColumn(
                name: "migrated_at",
                schema: "catalog",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "schema_version",
                schema: "catalog",
                table: "tenants");
        }
    }
}
