using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PdvRetaguardaVendasPdv07 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "cancel_alert_percent",
                schema: "pos",
                table: "stores",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 3m);

            migrationBuilder.CreateIndex(
                name: "ix_sales_day_store",
                schema: "sales",
                table: "sales",
                columns: new[] { "business_date", "store_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sales_day_store",
                schema: "sales",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "cancel_alert_percent",
                schema: "pos",
                table: "stores");
        }
    }
}
