using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SmsGateway.Shared.Migrations;

/// <inheritdoc />
public partial class AddResultProcessorAndIndexes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "smsc_message_id",
            table: "delivery_details",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "failed_results_log",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                source_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                smsc_message_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                payload_json = table.Column<string>(type: "jsonb", nullable: true),
                retry_count = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_failed_results_log", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "idx_delivery_details_smsc_message_id",
            table: "delivery_details",
            column: "smsc_message_id");

        migrationBuilder.CreateIndex(
            name: "idx_delivery_details_campaign_id",
            table: "delivery_details",
            column: "campaign_id");

        migrationBuilder.CreateIndex(
            name: "idx_failed_results_log_created_at",
            table: "failed_results_log",
            column: "created_at");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "idx_delivery_details_smsc_message_id",
            table: "delivery_details");

        migrationBuilder.DropIndex(
            name: "idx_delivery_details_campaign_id",
            table: "delivery_details");

        migrationBuilder.DropTable(
            name: "failed_results_log");

        migrationBuilder.DropColumn(
            name: "smsc_message_id",
            table: "delivery_details");
    }
}
