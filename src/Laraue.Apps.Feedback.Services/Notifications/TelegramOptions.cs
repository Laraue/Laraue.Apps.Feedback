using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Feedback.Services.Notifications;

public class TelegramOptions
{
    /// <summary>
    /// Telegram bot token of the bot that posts feedback notifications.
    /// </summary>
    [Required]
    public required string Token { get; set; }

    /// <summary>
    /// The team chat feedback notifications go to. Validated as non-zero at registration -
    /// <c>[Required]</c> can't catch a missing <see cref="long"/>.
    /// </summary>
    public required long ChatId { get; set; }

    /// <summary>
    /// <see cref="Token"/>, or a clear error naming the missing setting. Production validates options
    /// at startup, other environments don't, so a local run without <c>Telegram:Token</c> would
    /// otherwise fail deep inside Telegram.Bot with an unhelpful exception.
    /// </summary>
    public string GetRequiredToken()
    {
        return string.IsNullOrWhiteSpace(Token)
            ? throw new InvalidOperationException(
                "Telegram:Token is not configured. Set it (and Telegram:ChatId) in the host's " +
                "appsettings.Development.json for local runs.")
            : Token;
    }
}
