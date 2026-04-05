using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmsGateway.Shared.Migrations;

/// <inheritdoc />
public partial class AddCampaignAssignedQueue : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "assigned_queue",
            table: "campaigns",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "assigned_queue",
            table: "campaigns");
    }
}
