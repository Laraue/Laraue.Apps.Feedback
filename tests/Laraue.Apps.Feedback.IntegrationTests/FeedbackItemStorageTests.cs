using Laraue.Apps.Feedback.DataAccess.Entities;
using Laraue.Apps.Feedback.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Feedback.IntegrationTests;

[Collection("IntegrationTest")]
public class FeedbackItemStorageTests(InternalApiTestHost host) : IClassFixture<InternalApiTestHost>
{
    [Fact]
    public async Task SaveFeedbackItem_ShouldKeepContext_WhenReadBack()
    {
        using var testScope = host.CreateTestScope();
        var db = testScope.Database;

        var item = new FeedbackItem
        {
            UserId = Guid.NewGuid(),
            ServiceId = ServiceId.LaraueBoards,
            Type = FeedbackType.Bug,
            Message = "The board doesn't load",
            Context = new Dictionary<string, string>
            {
                ["page"] = "https://boards.laraue.com/organizations/test/issues",
                ["locale"] = "en",
            },
            Status = FeedbackStatus.New,
            CreatedAt = DateTime.UtcNow,
        };

        db.FeedbackItems.Add(item);
        await db.SaveChangesAsync();

        var saved = await db.FeedbackItems.SingleAsync(x => x.Id == item.Id);

        Assert.Equal("https://boards.laraue.com/organizations/test/issues", saved.Context["page"]);
        Assert.Equal("en", saved.Context["locale"]);
    }
}
