using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SmsGateway.Shared.Migrations
{
    /// <summary>
    /// Adds test_messages and test_smsc_ids tables for the "Send Test SMS" feature.
    /// Independent of campaigns/delivery_details — supports multi-part test messages.
    /// </summary>
    public partial class AddTestMessagesTables : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "test_messages",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    phone_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    message_content = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_test_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "test_smsc_ids",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    test_message_id = table.Column<int>(type: "integer", nullable: false),
                    smsc_message_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    part_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_test_smsc_ids", x => x.id);
                    table.ForeignKey(
                        name: "FK_test_smsc_ids_test_messages_test_message_id",
                        column: x => x.test_message_id,
                        principalTable: "test_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_test_messages_created_at",
                table: "test_messages",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "idx_test_messages_phone_number",
                table: "test_messages",
                column: "phone_number");

            migrationBuilder.CreateIndex(
                name: "idx_test_smsc_ids_test_message_id",
                table: "test_smsc_ids",
                column: "test_message_id");

            migrationBuilder.CreateIndex(
                name: "idx_test_smsc_ids_smsc_message_id",
                table: "test_smsc_ids",
                column: "smsc_message_id");

            migrationBuilder.CreateIndex(
                name: "idx_test_smsc_ids_composite",
                table: "test_smsc_ids",
                columns: new[] { "smsc_message_id", "part_number" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "test_smsc_ids");
            migrationBuilder.DropTable(name: "test_messages");
        }
    }
}
