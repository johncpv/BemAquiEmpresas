using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PurchaseOrderDispatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "sent_at",
                schema: "purchasing",
                table: "purchase_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "requested_by",
                schema: "shared",
                table: "email_logs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "purchase_order_dispatches",
                schema: "purchasing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email_log_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attachment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    cc = table.Column<string>(type: "character varying(800)", maxLength: 800, nullable: true),
                    recipient_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    is_resend = table.Column<bool>(type: "boolean", nullable: false),
                    sent_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_order_dispatches", x => x.id);
                    table.ForeignKey(
                        name: "fk_purchase_order_dispatches_purchase_orders_purchase_order_id",
                        column: x => x.purchase_order_id,
                        principalSchema: "purchasing",
                        principalTable: "purchase_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_email_logs_template_created_at",
                schema: "shared",
                table: "email_logs",
                columns: new[] { "template", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_dispatches_created_at",
                schema: "purchasing",
                table: "purchase_order_dispatches",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_dispatches_purchase_order_id",
                schema: "purchasing",
                table: "purchase_order_dispatches",
                column: "purchase_order_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "purchase_order_dispatches",
                schema: "purchasing");

            migrationBuilder.DropIndex(
                name: "ix_email_logs_template_created_at",
                schema: "shared",
                table: "email_logs");

            migrationBuilder.DropColumn(
                name: "sent_at",
                schema: "purchasing",
                table: "purchase_orders");

            migrationBuilder.DropColumn(
                name: "requested_by",
                schema: "shared",
                table: "email_logs");
        }
    }
}
