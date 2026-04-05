using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmsGateway.Shared.Migrations;

/// <inheritdoc />
public partial class UpdateDeliveryStatusEnumValues : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Migrate old DeliveryStatus enum values to new values
        migrationBuilder.Sql(@"
            UPDATE delivery_details
            SET status = CASE status
                WHEN -1 THEN 0
                WHEN 0 THEN 1
                WHEN 1 THEN 4
                WHEN 2 THEN 5
                WHEN 3 THEN 6
                WHEN 4 THEN 7
                ELSE status
            END;
        ");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            UPDATE delivery_details
            SET status = CASE status
                WHEN 0 THEN -1
                WHEN 1 THEN 0
                WHEN 4 THEN 1
                WHEN 5 THEN 2
                WHEN 6 THEN 3
                WHEN 7 THEN 4
                ELSE status
            END;
        ");
    }
}
