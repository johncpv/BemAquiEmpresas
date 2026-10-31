using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PrivacyRequestsLgpd01a : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "privacy_requests",
                schema: "settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    subject_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subject_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    subject_contact = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    requested_on = table.Column<DateOnly>(type: "date", nullable: false),
                    channel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    due_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    identity_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    identity_verified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    identity_method = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    identity_checked_on = table.Column<DateOnly>(type: "date", nullable: true),
                    identity_notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    response = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    responded_on = table.Column<DateOnly>(type: "date", nullable: true),
                    file_sent = table.Column<bool>(type: "boolean", nullable: true),
                    refusal_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    correction_link = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    export_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    export_attachment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    exported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    export_error = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    download_token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    download_token_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_privacy_requests", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_privacy_requests_download_token_hash",
                schema: "settings",
                table: "privacy_requests",
                column: "download_token_hash",
                unique: true,
                filter: "download_token_hash IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_privacy_requests_number",
                schema: "settings",
                table: "privacy_requests",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_privacy_requests_status_due_on",
                schema: "settings",
                table: "privacy_requests",
                columns: new[] { "status", "due_on" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "privacy_requests",
                schema: "settings");
        }
    }
}
