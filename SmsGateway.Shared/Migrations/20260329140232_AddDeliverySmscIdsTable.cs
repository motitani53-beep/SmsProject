using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SmsGateway.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliverySmscIdsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "delivery_smsc_ids",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    delivery_detail_id = table.Column<int>(type: "integer", nullable: false),
                    smsc_message_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    part_number = table.Column<int>(type: "integer", nullable: false),
                    total_parts = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    delivered_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_smsc_ids", x => x.id);
                    table.ForeignKey(
                        name: "FK_delivery_smsc_ids_delivery_details_delivery_detail_id",
                        column: x => x.delivery_detail_id,
                        principalTable: "delivery_details",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_delivery_smsc_ids_delivery_detail_id",
                table: "delivery_smsc_ids",
                column: "delivery_detail_id");

            migrationBuilder.CreateIndex(
                name: "idx_delivery_smsc_ids_smsc_message_id",
                table: "delivery_smsc_ids",
                column: "smsc_message_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delivery_smsc_ids");
        }
    }
}
