using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AnonymizationLgpd01b : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "anonymization_request",
                schema: "identity",
                table: "users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "anonymized_at",
                schema: "identity",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "anonymization_status",
                schema: "settings",
                table: "privacy_requests",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<string>(
                name: "anonymization_steps",
                schema: "settings",
                table: "privacy_requests",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "anonymization_summary",
                schema: "settings",
                table: "privacy_requests",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "anonymized_at",
                schema: "settings",
                table: "privacy_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "anonymized_by",
                schema: "settings",
                table: "privacy_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "anonymization_request",
                schema: "parties",
                table: "parties",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "anonymized_at",
                schema: "parties",
                table: "parties",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "anonymization_request",
                schema: "hr",
                table: "employees",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "anonymized_at",
                schema: "hr",
                table: "employees",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "content_removed_at",
                schema: "shared",
                table: "attachments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "retention_rules",
                schema: "settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    field_group = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    rule = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    years = table.Column<int>(type: "integer", nullable: false),
                    base_date = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confirmed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    seed_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_retention_rules", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_retention_rules_seed_key",
                schema: "settings",
                table: "retention_rules",
                column: "seed_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_retention_rules_subject_type_field_group",
                schema: "settings",
                table: "retention_rules",
                columns: new[] { "subject_type", "field_group" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "retention_rules",
                schema: "settings");

            migrationBuilder.DropColumn(
                name: "anonymization_request",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "anonymized_at",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "anonymization_status",
                schema: "settings",
                table: "privacy_requests");

            migrationBuilder.DropColumn(
                name: "anonymization_steps",
                schema: "settings",
                table: "privacy_requests");

            migrationBuilder.DropColumn(
                name: "anonymization_summary",
                schema: "settings",
                table: "privacy_requests");

            migrationBuilder.DropColumn(
                name: "anonymized_at",
                schema: "settings",
                table: "privacy_requests");

            migrationBuilder.DropColumn(
                name: "anonymized_by",
                schema: "settings",
                table: "privacy_requests");

            migrationBuilder.DropColumn(
                name: "anonymization_request",
                schema: "parties",
                table: "parties");

            migrationBuilder.DropColumn(
                name: "anonymized_at",
                schema: "parties",
                table: "parties");

            migrationBuilder.DropColumn(
                name: "anonymization_request",
                schema: "hr",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "anonymized_at",
                schema: "hr",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "content_removed_at",
                schema: "shared",
                table: "attachments");
        }
    }
}
