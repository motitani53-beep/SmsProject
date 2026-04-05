using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SmsGateway.Shared.Migrations;

/// <inheritdoc />
public partial class InitialCreateAfterCleanup : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "campaigns",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                campaign_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                message_content = table.Column<string>(type: "text", nullable: false),
                message_language = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                sender_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                sender_value = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                scheduling_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                scheduled_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                priority = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                campaign_cost = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_campaigns", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "delivery_details",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                campaign_id = table.Column<int>(type: "integer", nullable: false),
                phone_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                actual_sender = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                message_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                message_content = table.Column<string>(type: "text", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                processed = table.Column<bool>(type: "boolean", nullable: false),
                processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                error_message = table.Column<string>(type: "text", nullable: true),
                sent_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                additional_data = table.Column<JsonElement>(type: "jsonb", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_delivery_details", x => x.id);
                table.ForeignKey(
                    name: "FK_delivery_details_campaigns_campaign_id",
                    column: x => x.campaign_id,
                    principalTable: "campaigns",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_campaigns_campaign_name",
            table: "campaigns",
            column: "campaign_name");

        migrationBuilder.CreateIndex(
            name: "IX_campaigns_created_at",
            table: "campaigns",
            column: "created_at");

        migrationBuilder.CreateIndex(
            name: "IX_campaigns_status",
            table: "campaigns",
            column: "status");

        migrationBuilder.CreateIndex(
            name: "idx_delivery_details_campaign_processed",
            table: "delivery_details",
            columns: new[] { "campaign_id", "processed" });

        migrationBuilder.CreateIndex(
            name: "idx_delivery_details_created_at",
            table: "delivery_details",
            column: "created_at");

        migrationBuilder.CreateIndex(
            name: "idx_delivery_details_message_id",
            table: "delivery_details",
            column: "message_id");

        migrationBuilder.CreateIndex(
            name: "idx_delivery_details_phone_number",
            table: "delivery_details",
            column: "phone_number");

        migrationBuilder.CreateIndex(
            name: "idx_delivery_details_status",
            table: "delivery_details",
            column: "status");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "delivery_details");

        migrationBuilder.DropTable(
            name: "campaigns");
    }
}
