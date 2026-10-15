using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Feedback.Services.Notifications;

public class IdentityOptions
{
    /// <summary>
    /// Base address of Laraue.Apps.Identity.InternalApiHost's gRPC endpoint.
    /// </summary>
    [Required]
    [Url]
    public required string GrpcUrl { get; set; }
}
