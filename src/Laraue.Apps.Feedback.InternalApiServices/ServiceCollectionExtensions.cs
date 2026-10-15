using Laraue.Apps.Feedback.Services;
using Laraue.Apps.Feedback.Services.Notifications;
using Laraue.Apps.Identity.Internal.Contracts;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Core.DateTime.Services.Impl;
using Laraue.Grpc.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace Laraue.Apps.Feedback.InternalApiServices;

public static class ServiceCollectionExtensions
{
    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder AddInternalApiServices()
        {
            builder.Services
                .AddSingleton<IDateTimeProvider, DateTimeProvider>()
                .AddScoped<IFeedbackService, FeedbackService>()
                .AddScoped<IFeedbackNotifier, FeedbackNotifier>();

            builder.AddValidatedOptions<TelegramOptions>(
                "Telegram",
                o => o.ChatId != 0,
                "Telegram:ChatId is required.");

            builder.Services.AddSingleton<ITelegramBotClient>(
                sp => new TelegramBotClient(sp.GetRequiredService<IOptions<TelegramOptions>>().Value.GetRequiredToken()));
            builder.Services.AddSingleton(
                sp => new Lazy<ITelegramBotClient>(sp.GetRequiredService<ITelegramBotClient>));

            builder.AddValidatedOptions<IdentityOptions>("Identity");

            // AddLaraueGrpcClient's configureClient callback has no IServiceProvider access, so the URL is
            // read off configuration directly, same as Boards' Billing clients. GetUserProfile doesn't need
            // the x-laraue-service-id header, so no ServiceIdInterceptor (Identity has no ServiceId for
            // this service either).
            var identityUrl = builder.Configuration["Identity:GrpcUrl"]
                ?? throw new InvalidOperationException("Identity:GrpcUrl is not configured.");

            builder.Services.AddLaraueGrpcClient<UserIdentityService.UserIdentityServiceClient>(
                o => o.Address = new Uri(identityUrl));

            return builder;
        }

        /// <summary>
        /// Binds <typeparamref name="TOptions"/> to the given configuration section. In the Production
        /// environment it also validates the options' data annotations and <paramref name="validation"/>
        /// when the host starts, so a missing setting stops the host on deploy instead of failing on first
        /// use. Other environments only bind. Same helper as Boards'.
        /// </summary>
        public OptionsBuilder<TOptions> AddValidatedOptions<TOptions>(
            string sectionName,
            Func<TOptions, bool>? validation = null,
            string? failureMessage = null)
            where TOptions : class
        {
            var options = builder.Services
                .AddOptions<TOptions>()
                .Bind(builder.Configuration.GetSection(sectionName));

            if (!builder.Environment.IsProduction())
            {
                return options;
            }

            options.ValidateDataAnnotations();
            if (validation is not null)
            {
                options.Validate(validation, failureMessage ?? $"{sectionName} is invalid.");
            }

            return options.ValidateOnStart();
        }
    }
}
