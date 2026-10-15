using Laraue.Apps.Feedback.DataAccess.Data;
using Laraue.Apps.Feedback.DataAccess.Entities;
using Microsoft.EntityFrameworkCore;

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
            // Needs EnableDynamicJson() on the host's data source - see InternalApiHost's Program.cs.
            builder.Property(x => x.Context)
                .HasColumnType("jsonb");
        });
    }
}
