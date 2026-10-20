using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContractOnboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_onboarding",
                schema: "commercial",
                table: "contracts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "received_before_onboarding",
                schema: "commercial",
                table: "contract_milestones",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_onboarding",
                schema: "commercial",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "received_before_onboarding",
                schema: "commercial",
                table: "contract_milestones");
        }
    }
}
