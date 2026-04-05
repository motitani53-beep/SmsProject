using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmsGateway.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignTrackingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "completed_at",
                table: "campaigns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "total_messages",
                table: "campaigns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "total_sent_messages",
                table: "campaigns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Existing campaigns: total_messages = recipient count; total_sent_messages already defaulted to 0 above.
            migrationBuilder.Sql("""
                UPDATE campaigns AS c
                SET total_messages = COALESCE(s.cnt, 0)
                FROM (
                    SELECT campaign_id, COUNT(*)::int AS cnt
                    FROM delivery_details
                    GROUP BY campaign_id
                ) AS s
                WHERE c.id = s.campaign_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "completed_at",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "total_messages",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "total_sent_messages",
                table: "campaigns");
        }
    }
}
