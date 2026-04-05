using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmsGateway.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddCompositeIndexToSmscIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_delivery_smsc_ids_smsc_message_id",
                table: "delivery_smsc_ids");

            migrationBuilder.CreateIndex(
                name: "idx_delivery_smsc_ids_composite",
                table: "delivery_smsc_ids",
                columns: new[] { "smsc_message_id", "part_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_delivery_smsc_ids_composite",
                table: "delivery_smsc_ids");

            migrationBuilder.CreateIndex(
                name: "idx_delivery_smsc_ids_smsc_message_id",
                table: "delivery_smsc_ids",
                column: "smsc_message_id",
                unique: true);
        }
    }
}
