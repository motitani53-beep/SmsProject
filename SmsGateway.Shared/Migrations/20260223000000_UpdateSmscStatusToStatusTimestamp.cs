using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmsGateway.Shared.Migrations;

/// <inheritdoc />
public partial class UpdateSmscStatusToStatusTimestamp : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_smsc_status_updated_at",
            table: "smsc_status");

        migrationBuilder.DropColumn(
            name: "is_connected",
            table: "smsc_status");

        migrationBuilder.DropColumn(
            name: "last_enquire_link_at",
            table: "smsc_status");

        migrationBuilder.DropColumn(
            name: "last_error",
            table: "smsc_status");

        migrationBuilder.DropColumn(
            name: "updated_at",
            table: "smsc_status");

        migrationBuilder.AddColumn<string>(
            name: "status",
            table: "smsc_status",
            type: "character varying(50)",
            maxLength: 50,
            nullable: false,
            defaultValue: "OK");

        migrationBuilder.AddColumn<DateTime>(
            name: "timestamp",
            table: "smsc_status",
            type: "timestamp with time zone",
            nullable: false,
            defaultValue: new DateTime(2026, 2, 23, 0, 0, 0, 0, DateTimeKind.Utc));

        migrationBuilder.CreateIndex(
            name: "idx_smsc_status_timestamp",
            table: "smsc_status",
            column: "timestamp");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "idx_smsc_status_timestamp",
            table: "smsc_status");

        migrationBuilder.DropColumn(
            name: "status",
            table: "smsc_status");

        migrationBuilder.DropColumn(
            name: "timestamp",
            table: "smsc_status");

        migrationBuilder.AddColumn<bool>(
            name: "is_connected",
            table: "smsc_status",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<DateTime>(
            name: "last_enquire_link_at",
            table: "smsc_status",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "last_error",
            table: "smsc_status",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "updated_at",
            table: "smsc_status",
            type: "timestamp with time zone",
            nullable: false,
            defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

        migrationBuilder.CreateIndex(
            name: "IX_smsc_status_updated_at",
            table: "smsc_status",
            column: "updated_at");
    }
}
