using Laraue.Apps.Feedback.DataAccess;
using Laraue.Apps.Feedback.DataAccess.Entities;
using Laraue.Apps.Feedback.Services.Notifications;
using Laraue.Apps.Feedback.Services.Resources;
using Laraue.Core.DataAccess.Contracts;
using Laraue.Core.DataAccess.EFCore.Extensions;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Core.Exceptions.Web;

namespace Laraue.Apps.Feedback.Services;

public sealed record SubmitFeedbackData(
    Guid UserId,
    FeedbackType Type,
    string Message,
    string? Contact,
    IReadOnlyDictionary<string, string> Context);

public sealed record FeedbackFilter(ServiceId? ServiceId, FeedbackStatus? Status);

public sealed record FeedbackListItem
{
    public required long Id { get; init; }
    public required Guid UserId { get; init; }
    public required ServiceId ServiceId { get; init; }
    public required FeedbackType Type { get; init; }
    public required string Message { get; init; }
    public required string? Contact { get; init; }
    public required Dictionary<string, string> Context { get; init; }
    public required FeedbackStatus Status { get; init; }
    public required DateTime CreatedAt { get; init; }
}

public interface IFeedbackService
{
    /// <summary>
    /// Stores feedback from <paramref name="serviceId"/> as <see cref="FeedbackStatus.New"/>, notifies the
    /// team in Telegram and returns its id. Throws <see cref="BadRequestException"/> for an invalid value.
    /// </summary>
    Task<long> SubmitAsync(ServiceId serviceId, SubmitFeedbackData data, CancellationToken cancellationToken);

    /// <summary>
    /// Feedback newest first, page by page. Throws <see cref="BadRequestException"/> for an invalid page.
    /// </summary>
    Task<ShortPaginatedResult<FeedbackListItem>> ListAsync(
        FeedbackFilter filter,
        PaginationData pagination,
        CancellationToken cancellationToken);
}

public sealed class FeedbackService(
    DatabaseContext context,
    IDateTimeProvider dateTimeProvider,
    IFeedbackNotifier notifier)
    : IFeedbackService
{
    public const int PerPageMax = 100;

    public async Task<long> SubmitAsync(
        ServiceId serviceId,
        SubmitFeedbackData data,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(serviceId))
        {
            throw new BadRequestException(nameof(serviceId), string.Format(Errors.UnknownService, serviceId));
        }

        if (data.UserId == Guid.Empty)
        {
            throw new BadRequestException(nameof(data.UserId), string.Format(Errors.ValueRequired, nameof(data.UserId)));
        }

        if (!Enum.IsDefined(data.Type))
        {
            throw new BadRequestException(nameof(data.Type), string.Format(Errors.UnknownFeedbackType, data.Type));
        }

        var message = data.Message.Trim();
        if (message.Length == 0)
        {
            throw new BadRequestException(nameof(data.Message), string.Format(Errors.ValueRequired, nameof(data.Message)));
        }

        EnsureMaxLength(nameof(data.Message), message, FeedbackItem.MessageMaxLength);

        var contact = string.IsNullOrWhiteSpace(data.Contact) ? null : data.Contact.Trim();
        if (contact is not null)
        {
            EnsureMaxLength(nameof(data.Contact), contact, FeedbackItem.ContactMaxLength);
        }

        ValidateContext(data.Context);

        var item = new FeedbackItem
        {
            UserId = data.UserId,
            ServiceId = serviceId,
            Type = data.Type,
            Message = message,
            Contact = contact,
            Context = new Dictionary<string, string>(data.Context),
            Status = FeedbackStatus.New,
            CreatedAt = dateTimeProvider.UtcNow,
        };

        context.FeedbackItems.Add(item);
        await context.SaveChangesAsync(cancellationToken);

        // After the save, and not with the caller's token: the feedback is stored either way, and a caller
        // giving up on the response shouldn't cancel the team's notification. The notifier never throws.
        await notifier.NotifyAsync(item, CancellationToken.None);

        return item.Id;
    }

    public Task<ShortPaginatedResult<FeedbackListItem>> ListAsync(
        FeedbackFilter filter,
        PaginationData pagination,
        CancellationToken cancellationToken)
    {
        if (pagination.Page < 0 || pagination.PerPage is < 1 or > PerPageMax)
        {
            throw new BadRequestException(nameof(pagination), string.Format(Errors.InvalidPagination, PerPageMax));
        }

        var query = context.FeedbackItems.AsQueryable();

        if (filter.ServiceId is { } serviceId)
        {
            query = query.Where(x => x.ServiceId == serviceId);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        return query
            // Id is an identity column, so it follows the creation order.
            .OrderByDescending(x => x.Id)
            .Select(x => new FeedbackListItem
            {
                Id = x.Id,
                UserId = x.UserId,
                ServiceId = x.ServiceId,
                Type = x.Type,
                Message = x.Message,
                Contact = x.Contact,
                Context = x.Context,
                Status = x.Status,
                CreatedAt = x.CreatedAt,
            })
            .ShortPaginateEFAsync(pagination, cancellationToken);
    }

    private static void ValidateContext(IReadOnlyDictionary<string, string> feedbackContext)
    {
        if (feedbackContext.Count > FeedbackItem.ContextMaxEntries)
        {
            throw new BadRequestException(
                nameof(FeedbackItem.Context),
                string.Format(Errors.TooManyContextEntries, FeedbackItem.ContextMaxEntries));
        }

        foreach (var (key, value) in feedbackContext)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new BadRequestException(nameof(FeedbackItem.Context), Errors.ContextKeyRequired);
            }

            EnsureMaxLength($"{nameof(FeedbackItem.Context)} key", key, FeedbackItem.ContextKeyMaxLength);
            EnsureMaxLength($"{nameof(FeedbackItem.Context)}[{key}]", value, FeedbackItem.ContextValueMaxLength);
        }
    }

    private static void EnsureMaxLength(string fieldName, string value, int maxLength)
    {
        if (value.Length > maxLength)
        {
            throw new BadRequestException(fieldName, string.Format(Errors.ValueTooLong, fieldName, maxLength));
        }
    }
}
