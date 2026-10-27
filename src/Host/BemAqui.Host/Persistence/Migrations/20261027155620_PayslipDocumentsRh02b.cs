using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PayslipDocumentsRh02b : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "changed_after_notice",
                schema: "hr",
                table: "payslips",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "first_viewed_at",
                schema: "hr",
                table: "payslips",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "notice_email_log_id",
                schema: "hr",
                table: "payslips",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "notified_at",
                schema: "hr",
                table: "payslips",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "pdf_attachment_id",
                schema: "hr",
                table: "payslips",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "changed_after_notice",
                schema: "hr",
                table: "payslips");

            migrationBuilder.DropColumn(
                name: "first_viewed_at",
                schema: "hr",
                table: "payslips");

            migrationBuilder.DropColumn(
                name: "notice_email_log_id",
                schema: "hr",
                table: "payslips");

            migrationBuilder.DropColumn(
                name: "notified_at",
                schema: "hr",
                table: "payslips");

            migrationBuilder.DropColumn(
                name: "pdf_attachment_id",
                schema: "hr",
                table: "payslips");
        }
    }
}
