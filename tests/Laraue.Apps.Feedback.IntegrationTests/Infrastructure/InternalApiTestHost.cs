using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Laraue.Apps.Feedback.DataAccess;
using Laraue.Apps.Feedback.Internal.Contracts;
using Laraue.Apps.Feedback.InternalApiHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Exporter;

namespace Laraue.Apps.Feedback.IntegrationTests.Infrastructure;

public class InternalApiTestHost : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            config.AddJsonFile("appsettings.json", optional: true);
        });

        // The exporter serves a cached scrape for 300 ms by default; a test scraping right after another
        // would read stale numbers.
        builder.ConfigureServices(services => services.Configure<PrometheusAspNetCoreOptions>(
            options => options.ScrapeResponseCacheDurationMilliseconds = 0));

        return base.CreateHost(builder);
    }

    /// <summary>
    /// A gRPC client wired to this in-memory test server (no real network socket), for calling
    /// <see cref="FeedbackService"/> the same way another Laraue app would - including going through
    /// <see cref="ServiceIdInterceptor"/> to attach <paramref name="callingService"/> as a header.
    /// </summary>
    public FeedbackService.FeedbackServiceClient CreateFeedbackClient(ServiceId callingService)
    {
        return new FeedbackService.FeedbackServiceClient(
            CreateChannel().Intercept(new ServiceIdInterceptor(callingService)));
    }

    /// <summary>
    /// A client with no <see cref="ServiceIdInterceptor"/> attached - for the "caller didn't identify
    /// itself" path and for <c>ListFeedback</c>, which doesn't need the header.
    /// </summary>
    public FeedbackService.FeedbackServiceClient CreateFeedbackClientWithoutServiceIdHeader()
    {
        return new FeedbackService.FeedbackServiceClient(CreateChannel());
    }

    private GrpcChannel CreateChannel()
    {
        var client = CreateDefaultClient();

        return GrpcChannel.ForAddress(client.BaseAddress!, new GrpcChannelOptions
        {
            HttpClient = client,
        });
    }

    public InternalApiTestHostScope CreateTestScope()
    {
        var scope = Services.CreateScope();

        return new InternalApiTestHostScope(scope);
    }
}

/// <summary>
/// Per-test scope, same shape as Identity's: creating one wipes the test database, so every test starts
/// with <c>using var testScope = host.CreateTestScope();</c> and uses <see cref="Database"/> for any
/// direct seeding/assertions.
/// </summary>
public class InternalApiTestHostScope : IDisposable
{
    private readonly IServiceScope _scope;
    public DatabaseContext Database => _scope.ServiceProvider.GetRequiredService<DatabaseContext>();

    public InternalApiTestHostScope(IServiceScope scope)
    {
        _scope = scope;
        Database.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        Database.CleanDatabase();
    }

    public void Dispose()
    {
        _scope.Dispose();
    }
}
