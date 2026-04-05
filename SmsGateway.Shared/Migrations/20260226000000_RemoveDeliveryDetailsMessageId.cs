using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmsGateway.Shared.Migrations;

/// <inheritdoc />
public partial class RemoveDeliveryDetailsMessageId : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "idx_delivery_details_message_id",
            table: "delivery_details");

        migrationBuilder.DropColumn(
            name: "message_id",
            table: "delivery_details");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "message_id",
            table: "delivery_details",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "idx_delivery_details_message_id",
            table: "delivery_details",
            column: "message_id");
    }
}
