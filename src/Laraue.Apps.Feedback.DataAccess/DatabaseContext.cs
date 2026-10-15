using Laraue.Apps.Feedback.DataAccess.Data;
using Laraue.Apps.Feedback.DataAccess.Entities;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Laraue.Apps.Feedback.DataAccess;

public class DatabaseContext : DbContext
{
    public DatabaseContext(DbContextOptions options)
        : base(options)
    {
    }

    public required DbSet<FeedbackItem> FeedbackItems { get; set; }
    public required DbSet<Service> Services { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Service>()
            .HasData(ServicesData.Services);

        modelBuilder.Entity<FeedbackItem>(builder =>
        {
            // Converted in the model rather than through Npgsql's dynamic JSON, which needs an
            // EnableDynamicJson() opt-in on every host's data source.
            builder.Property(x => x.Context)
                .HasColumnType("jsonb")
                .HasConversion(
                    x => JsonSerializer.Serialize(x, JsonSerializerOptions.Default),
                    x => JsonSerializer.Deserialize<Dictionary<string, string>>(x, JsonSerializerOptions.Default)
                        ?? new Dictionary<string, string>(),
                    new ValueComparer<Dictionary<string, string>>(
                        (a, b) => a!.Count == b!.Count && !a.Except(b).Any(),
                        x => x.Aggregate(0, (hash, pair) => HashCode.Combine(hash, pair.Key, pair.Value)),
                        x => new Dictionary<string, string>(x)));

            // ListFeedback: newest first, optionally filtered by service and status.
            builder.HasIndex(x => x.CreatedAt);
            builder.HasIndex(x => new { x.ServiceId, x.Status, x.CreatedAt });
        });
    }
}
