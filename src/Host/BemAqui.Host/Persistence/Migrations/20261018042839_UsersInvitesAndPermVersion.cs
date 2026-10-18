using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UsersInvitesAndPermVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "perm_version",
                schema: "identity",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "canceled_at",
                schema: "identity",
                table: "user_invites",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid[]>(
                name: "group_ids",
                schema: "identity",
                table: "user_invites",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "perm_version",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "canceled_at",
                schema: "identity",
                table: "user_invites");

            migrationBuilder.DropColumn(
                name: "group_ids",
                schema: "identity",
                table: "user_invites");
        }
    }
}
