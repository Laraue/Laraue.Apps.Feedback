using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Feedback.DataAccess.Entities;

/// <summary>
/// One piece of feedback a user left in a Laraue app, sent here by that app.
/// </summary>
public class FeedbackItem
{
    /// <summary>
    /// Max length of <see cref="Message"/>. The Telegram notification carries the whole message plus a
    /// few header lines, and a Telegram message is at most 4096 characters.
    /// </summary>
    public const int MessageMaxLength = 3000;

    /// <summary>
    /// Limits of <see cref="Context"/>: enough for a page URL and a handful of details, while keeping a
    /// caller from storing arbitrary amounts of data in it.
    /// </summary>
    public const int ContextMaxEntries = 20;

    /// <inheritdoc cref="ContextMaxEntries"/>
    public const int ContextKeyMaxLength = 64;

    /// <inheritdoc cref="ContextMaxEntries"/>
    public const int ContextValueMaxLength = 2048;

    public long Id { get; set; }

    /// <summary>
    /// The global Laraue user id (from Identity) of the user who left the feedback.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// The app the feedback came from - the caller's <c>x-laraue-service-id</c> header, not a request
    /// field.
    /// </summary>
    public ServiceId ServiceId { get; set; }

    public Service? Service { get; set; }

    public FeedbackType Type { get; set; }

    [MaxLength(MessageMaxLength)]
    public required string Message { get; set; }

    /// <summary>
    /// Free-form key/value pairs the calling app attaches (page URL, organization, app version, locale,
    /// browser). Stored as <c>jsonb</c>; this service doesn't interpret them.
    /// </summary>
    public Dictionary<string, string> Context { get; set; } = new();

    public FeedbackStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }
}

public enum FeedbackType
{
    Bug = 1,
    Idea = 2,
    Question = 3,
}

public enum FeedbackStatus
{
    New = 1,
    Seen = 2,
    Done = 3,
}
