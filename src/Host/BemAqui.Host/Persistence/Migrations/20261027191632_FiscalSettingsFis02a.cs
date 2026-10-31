using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FiscalSettingsFis02a : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "certificate_uploads",
                schema: "fiscal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    thumbprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    document = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    not_after = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_certificate_uploads", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fiscal_settings",
                schema: "fiscal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slot = table.Column<int>(type: "integer", nullable: false),
                    regime = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    crt = table.Column<int>(type: "integer", nullable: false),
                    cnae_main = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    cnae_secondary = table.Column<string[]>(type: "text[]", nullable: false),
                    nfe_series = table.Column<int>(type: "integer", nullable: true),
                    environment = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    cfop_sale_in_state_contributor = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    cfop_sale_in_state_final = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    cfop_sale_interstate_contributor = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    cfop_sale_interstate_final = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    cfop_resale_in_state = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    cfop_resale_interstate = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    mixed_sale = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    cst_icms = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    cst_pis_cofins = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    cst_ipi = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    icms_internal_rate = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    icms_interstate_south_southeast_rate = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    icms_interstate_other_rate = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    icms_imported_rate = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    pis_rate = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    cofins_rate = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    ipi_from_ncm = table.Column<bool>(type: "boolean", nullable: false),
                    cost_taxes = table.Column<string[]>(type: "text[]", nullable: false),
                    nfse_service_item = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    nfse_municipal_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    nfse_ctn = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    nfse_nbs = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    nfse_iss_rate = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    nfse_iss_location = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    nfse_iss_withheld = table.Column<bool>(type: "boolean", nullable: false),
                    nfse_federal_withholding = table.Column<bool>(type: "boolean", nullable: false),
                    ibs_cbs_cst = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ibs_cbs_classification = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    cbs_rate = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    ibs_rate = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    ibs_city_rate = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    cancellation_window_hours = table.Column<int>(type: "integer", nullable: false),
                    freight_mode = table.Column<int>(type: "integer", nullable: false),
                    volume_species = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    include_duplicates = table.Column<bool>(type: "boolean", nullable: false),
                    certificate_not_after = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    certificate_subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    certificate_document = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    certificate_thumbprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    certificate_uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    certificate_uploaded_by = table.Column<Guid>(type: "uuid", nullable: true),
                    certificate_changed_outside_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fiscal_settings", x => x.id);
                    table.CheckConstraint("ck_fiscal_settings_cancellation_window", "cancellation_window_hours BETWEEN 1 AND 168");
                    table.CheckConstraint("ck_fiscal_settings_nfe_series", "nfe_series IS NULL OR nfe_series BETWEEN 1 AND 999");
                    table.CheckConstraint("ck_fiscal_settings_slot", "slot = 1");
                });

            migrationBuilder.CreateTable(
                name: "setting_confirmations",
                schema: "fiscal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    setting_key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    value_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    confirmed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_setting_confirmations", x => x.id);
                    table.CheckConstraint("ck_setting_confirmations_hash", "length(value_hash) = 64");
                });

            migrationBuilder.CreateIndex(
                name: "ix_certificate_uploads_at",
                schema: "fiscal",
                table: "certificate_uploads",
                column: "at");

            migrationBuilder.CreateIndex(
                name: "ux_fiscal_settings_slot",
                schema: "fiscal",
                table: "fiscal_settings",
                column: "slot",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_setting_confirmations_key",
                schema: "fiscal",
                table: "setting_confirmations",
                column: "setting_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "certificate_uploads",
                schema: "fiscal");

            migrationBuilder.DropTable(
                name: "fiscal_settings",
                schema: "fiscal");

            migrationBuilder.DropTable(
                name: "setting_confirmations",
                schema: "fiscal");
        }
    }
}
