using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SmsGateway.Shared.Migrations;

/// <inheritdoc />
public partial class UpdateModelChanges : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "delivered_at",
            table: "delivery_details",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "smsc_status",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                is_connected = table.Column<bool>(type: "boolean", nullable: false),
                last_enquire_link_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_smsc_status", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_smsc_status_updated_at",
            table: "smsc_status",
            column: "updated_at");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "smsc_status");

        migrationBuilder.DropColumn(
            name: "delivered_at",
            table: "delivery_details");
    }
}
