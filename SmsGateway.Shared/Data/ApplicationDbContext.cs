using Microsoft.EntityFrameworkCore;
using SmsGateway.Shared.Models;

namespace SmsGateway.Shared.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Campaign> Campaigns { get; set; }
    public DbSet<DeliveryDetails> DeliveryDetails { get; set; }
    public DbSet<DeliverySmscId> DeliverySmscIds { get; set; }
    public DbSet<SmscStatus> SmscStatus { get; set; }
    public DbSet<FailedResultsLog> FailedResultsLog { get; set; }
    public DbSet<TestMessage> TestMessages { get; set; }
    public DbSet<TestSmscId> TestSmscIds { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Campaign entity
        modelBuilder.Entity<Campaign>(entity =>
        {
            entity.HasIndex(e => e.CampaignName);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => new { e.Status, e.SchedulingType, e.ScheduledTime })
                  .HasDatabaseName("idx_campaigns_schedule_pickup");

            entity.HasMany(e => e.DeliveryDetails)
                  .WithOne(e => e.Campaign)
                  .HasForeignKey(e => e.CampaignId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure DeliveryDetails entity
        modelBuilder.Entity<DeliveryDetails>(entity =>
        {
            entity.Property(e => e.Status)
                  .HasConversion<int>();

            entity.Property(e => e.AdditionalData)
                  .HasColumnType("jsonb");

            entity.HasIndex(e => new { e.CampaignId, e.Processed })
                  .HasDatabaseName("idx_delivery_details_campaign_processed");

            entity.HasIndex(e => e.Status)
                  .HasDatabaseName("idx_delivery_details_status");

            entity.HasIndex(e => e.PhoneNumber)
                  .HasDatabaseName("idx_delivery_details_phone_number");

            entity.HasIndex(e => e.CreatedAt)
                  .HasDatabaseName("idx_delivery_details_created_at");

            entity.HasIndex(e => e.SmscMessageId)
                  .HasDatabaseName("idx_delivery_details_smsc_message_id");

            entity.HasIndex(e => e.CampaignId)
                  .HasDatabaseName("idx_delivery_details_campaign_id");

            // Supports upsert lookups by campaign + recipient (see SmsBatchProcessor).
            entity.HasIndex(e => new { e.CampaignId, e.PhoneNumber })
                  .HasDatabaseName("idx_delivery_details_campaign_phone");

            entity.HasMany(e => e.SmscIds)
                  .WithOne(e => e.DeliveryDetail)
                  .HasForeignKey(e => e.DeliveryDetailId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DeliverySmscId>(entity =>
        {
            entity.HasIndex(e => new { e.SmscMessageId, e.PartNumber })
                  .IsUnique()
                  .HasDatabaseName("idx_delivery_smsc_ids_composite");

            entity.HasIndex(e => e.DeliveryDetailId)
                  .HasDatabaseName("idx_delivery_smsc_ids_delivery_detail_id");
        });

        modelBuilder.Entity<FailedResultsLog>(entity =>
        {
            entity.HasIndex(e => e.CreatedAt)
                  .HasDatabaseName("idx_failed_results_log_created_at");
        });

        modelBuilder.Entity<SmscStatus>(entity =>
        {
            entity.HasIndex(e => e.Timestamp)
                  .HasDatabaseName("idx_smsc_status_timestamp");
        });

        // Test SMS — independent of campaigns/delivery_details.
        modelBuilder.Entity<TestMessage>(entity =>
        {
            entity.HasIndex(e => e.CreatedAt).HasDatabaseName("idx_test_messages_created_at");
            entity.HasIndex(e => e.PhoneNumber).HasDatabaseName("idx_test_messages_phone_number");

            entity.HasMany(e => e.SmscIds)
                  .WithOne(e => e.TestMessage)
                  .HasForeignKey(e => e.TestMessageId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TestSmscId>(entity =>
        {
            entity.HasIndex(e => new { e.SmscMessageId, e.PartNumber })
                  .IsUnique()
                  .HasDatabaseName("idx_test_smsc_ids_composite");

            entity.HasIndex(e => e.TestMessageId)
                  .HasDatabaseName("idx_test_smsc_ids_test_message_id");

            entity.HasIndex(e => e.SmscMessageId)
                  .HasDatabaseName("idx_test_smsc_ids_smsc_message_id");
        });
    }
}
