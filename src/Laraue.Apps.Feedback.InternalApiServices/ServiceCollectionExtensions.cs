using Laraue.Apps.Feedback.Services;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Core.DateTime.Services.Impl;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Feedback.InternalApiServices;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddInternalApiServices()
        {
            services
                .AddSingleton<IDateTimeProvider, DateTimeProvider>()
                .AddScoped<IFeedbackService, FeedbackService>();

            return services;
        }
    }
}
