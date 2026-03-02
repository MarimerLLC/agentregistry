using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarimerLLC.AgentRegistry.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEndpointLastAliveAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_alive_at",
                table: "endpoints",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_alive_at",
                table: "endpoints");
        }
    }
}
