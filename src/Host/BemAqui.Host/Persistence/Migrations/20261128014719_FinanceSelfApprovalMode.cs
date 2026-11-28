using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinanceSelfApprovalMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "self_approval_mode",
                schema: "finance",
                table: "payment_order_approvals",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "finance",
                table: "parameters",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "self_approval_mode",
                schema: "finance",
                table: "payment_order_approvals");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "finance",
                table: "parameters");
        }
    }
}
