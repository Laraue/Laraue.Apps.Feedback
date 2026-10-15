using System.Text;
using Laraue.Apps.Feedback.DataAccess.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using UserIdentityService = Laraue.Apps.Identity.Internal.Contracts.UserIdentityService;
using GetUserProfileRequest = Laraue.Apps.Identity.Internal.Contracts.GetUserProfileRequest;

namespace Laraue.Apps.Feedback.Services.Notifications;

public interface IFeedbackNotifier
{
    /// <summary>
    /// Tells the team about a submitted feedback item in the Telegram chat. Never throws: the feedback is
    /// already saved, so a failed lookup or send is logged rather than failing the submit.
    /// </summary>
    Task NotifyAsync(FeedbackItem item, CancellationToken cancellationToken);
}

/// <param name="botClient">
/// Lazy, so a host without a configured bot token (a local run) still serves every call - the missing
/// token surfaces as a logged notification failure instead of failing to construct this class.
/// </param>
public sealed class FeedbackNotifier(
    Lazy<ITelegramBotClient> botClient,
    UserIdentityService.UserIdentityServiceClient identityClient,
    IOptions<TelegramOptions> telegramOptions,
    ILogger<FeedbackNotifier> logger)
    : IFeedbackNotifier
{
    /// <summary>
    /// How much of each context value the notification shows. A value may be up to
    /// <see cref="FeedbackItem.ContextValueMaxLength"/>, too long to list several next to the message.
    /// </summary>
    public const int ContextValueShownMaxLength = 300;

    /// <summary>
    /// How long the sender lookup may take before the notification goes out with the user id only.
    /// </summary>
    public static readonly TimeSpan IdentityTimeout = TimeSpan.FromSeconds(3);

    // Telegram's limit for a message text.
    private const int TelegramMessageMaxLength = 4096;

    public async Task NotifyAsync(FeedbackItem item, CancellationToken cancellationToken)
    {
        try
        {
            var sender = await GetSenderAsync(item.UserId, cancellationToken);

            await botClient.Value.SendMessage(
                telegramOptions.Value.ChatId,
                BuildText(item, sender),
                linkPreviewOptions: true,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Sending the notification about feedback {FeedbackId} failed", item.Id);
        }
    }

    /// <summary>
    /// The user's display name, @username and Google email from Identity, or null when Identity can't
    /// tell - the notification then shows the user id only.
    /// </summary>
    private async Task<string?> GetSenderAsync(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            var profile = await identityClient.GetUserProfileAsync(
                new GetUserProfileRequest { UserId = userId.ToString() },
                deadline: DateTime.UtcNow.Add(IdentityTimeout),
                cancellationToken: cancellationToken);

            var parts = new List<string> { profile.DisplayName };

            if (profile.HasUserName && profile.UserName != profile.DisplayName)
            {
                parts.Add($"@{profile.UserName}");
            }

            if (profile.HasGoogleEmail)
            {
                parts.Add(profile.GoogleEmail);
            }

            return string.Join(" · ", parts);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Resolving the sender of feedback from user {UserId} failed", userId);
            return null;
        }
    }

    /// <summary>
    /// Plain text, not HTML: the message is the user's own text, and HTML escaping could push a message
    /// within <see cref="FeedbackItem.MessageMaxLength"/> over Telegram's limit. The message goes in
    /// whole when it can; the context entries come after it in priority - the ones that don't fit in what
    /// is left are dropped, with a "…" line in their place.
    /// </summary>
    public static string BuildText(FeedbackItem item, string? sender)
    {
        var header = new StringBuilder()
            .AppendLine($"{TypeTitle(item.Type)} #{item.Id} · {ServiceTitle(item.ServiceId)}")
            .AppendLine(sender is null ? $"From: {item.UserId}" : $"From: {sender} ({item.UserId})");

        if (item.Contact is not null)
        {
            header.AppendLine($"Contact: {item.Contact}");
        }

        // The blank line between the header and the message.
        var blankLineLength = Environment.NewLine.Length;
        var message = Cut(item.Message, TelegramMessageMaxLength - header.Length - blankLineLength);
        var room = TelegramMessageMaxLength - header.Length - blankLineLength - message.Length;

        var droppedLine = $"…{Environment.NewLine}";
        var lines = item.Context
            .Select(x => $"{x.Key}: {Cut(x.Value, ContextValueShownMaxLength)}{Environment.NewLine}")
            .ToList();

        for (var i = 0; i < lines.Count; i++)
        {
            var isLast = i == lines.Count - 1;
            var needed = lines[i].Length + (isLast ? 0 : droppedLine.Length);
            if (needed > room)
            {
                if (droppedLine.Length <= room)
                {
                    header.Append(droppedLine);
                }

                break;
            }

            header.Append(lines[i]);
            room -= lines[i].Length;
        }

        return header.AppendLine().Append(message).ToString();
    }

    private static string Cut(string text, int maxLength) => text.Length <= maxLength
        ? text
        : text[..(maxLength - 1)] + "…";

    private static string TypeTitle(FeedbackType type) => type switch
    {
        FeedbackType.Bug => "🐞 Bug",
        FeedbackType.Idea => "💡 Idea",
        FeedbackType.Question => "❓ Question",
        _ => type.ToString(),
    };

    private static string ServiceTitle(ServiceId serviceId) => serviceId switch
    {
        ServiceId.LaraueBoards => "Laraue Boards",
        _ => serviceId.ToString(),
    };
}
