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
using Moq;
using OpenTelemetry.Exporter;
using Telegram.Bot;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using UserIdentityService = Laraue.Apps.Identity.Internal.Contracts.UserIdentityService;
using GetUserProfileRequest = Laraue.Apps.Identity.Internal.Contracts.GetUserProfileRequest;
using GetUserProfileResponse = Laraue.Apps.Identity.Internal.Contracts.GetUserProfileResponse;

namespace Laraue.Apps.Feedback.IntegrationTests.Infrastructure;

public class InternalApiTestHost : WebApplicationFactory<Program>
{
    /// <summary>
    /// Stands in for the real bot, so no test reaches Telegram. Reset by <see cref="CreateTestScope"/> to
    /// accept every message; tests verify what was sent through it.
    /// </summary>
    public Mock<ITelegramBotClient> BotClientMock { get; } = new();

    /// <summary>
    /// Stands in for Laraue.Apps.Identity. Reset by <see cref="CreateTestScope"/> to return
    /// <see cref="DefaultProfile"/> for any user.
    /// </summary>
    public Mock<UserIdentityService.UserIdentityServiceClient> IdentityClientMock { get; } = new();

    public static GetUserProfileResponse DefaultProfile => new()
    {
        DisplayName = "Test User",
        Initials = "TU",
    };

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

        // Registered after the host's own, so these win.
        builder.ConfigureServices(services => services
            .AddSingleton(BotClientMock.Object)
            .AddSingleton(IdentityClientMock.Object));

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
        ResetMocks();

        var scope = Services.CreateScope();

        return new InternalApiTestHostScope(scope);
    }

    private void ResetMocks()
    {
        BotClientMock.Reset();
        BotClientMock
            .Setup(x => x.SendRequest(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Message());

        IdentityClientMock.Reset();
        SetupProfile(DefaultProfile);
    }

    /// <summary>
    /// Makes Identity return <paramref name="profile"/> for any user.
    /// </summary>
    public void SetupProfile(GetUserProfileResponse profile)
    {
        IdentityClientMock
            .Setup(x => x.GetUserProfileAsync(
                It.IsAny<GetUserProfileRequest>(),
                It.IsAny<global::Grpc.Core.Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() => GrpcTestHelpers.AsyncUnaryCallOf(profile));
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
