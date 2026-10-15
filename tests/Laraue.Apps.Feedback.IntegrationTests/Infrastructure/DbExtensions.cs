using Laraue.Apps.Feedback.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Feedback.IntegrationTests.Infrastructure;

public static class DbExtensions
{
    /// <summary>
    /// Deletes every row written by a test without touching the static seed data
    /// (<see cref="DatabaseContext.Services"/>).
    /// </summary>
    public static void CleanDatabase(this DatabaseContext dbContext)
    {
        dbContext.FeedbackItems.ExecuteDelete();
    }
}
