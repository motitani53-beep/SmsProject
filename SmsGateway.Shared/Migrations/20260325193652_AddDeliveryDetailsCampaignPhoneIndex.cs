using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmsGateway.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryDetailsCampaignPhoneIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "idx_delivery_details_campaign_phone",
                table: "delivery_details",
                columns: new[] { "campaign_id", "phone_number" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_delivery_details_campaign_phone",
                table: "delivery_details");
        }
    }
}
