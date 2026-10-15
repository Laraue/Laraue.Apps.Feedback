using Grpc.Core;
using Laraue.Apps.Feedback.Internal.Contracts;
using Laraue.Apps.Feedback.IntegrationTests.Infrastructure;
using Laraue.Apps.Feedback.Services.Notifications;
using Microsoft.EntityFrameworkCore;
using Moq;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using Domain = Laraue.Apps.Feedback.DataAccess.Entities;
using GetUserProfileRequest = Laraue.Apps.Identity.Internal.Contracts.GetUserProfileRequest;
using GetUserProfileResponse = Laraue.Apps.Identity.Internal.Contracts.GetUserProfileResponse;

namespace Laraue.Apps.Feedback.IntegrationTests;

[Collection("IntegrationTest")]
public class FeedbackNotificationTests(InternalApiTestHost host) : IClassFixture<InternalApiTestHost>
{
    // Telegram:ChatId in the tests' appsettings.json.
    private const long TeamChatId = -100500;

    [Fact]
    public async Task SubmitFeedback_ShouldNotifyTeamChat_WhenFeedbackIsSubmitted()
    {
        using var testScope = host.CreateTestScope();
        host.SetupProfile(new GetUserProfileResponse
        {
            DisplayName = "Ilya Belyansky",
            Initials = "IB",
            UserName = "ilya",
            GoogleEmail = "ilya@example.com",
        });
        var userId = Guid.NewGuid();

        var request = new SubmitFeedbackRequest
        {
            UserId = userId.ToString(),
            Type = FeedbackType.Bug,
            Message = "The board doesn't load",
        };
        request.Context.Add("page", "https://boards.laraue.com/organizations/test/issues");

        var response = await host.CreateFeedbackClient(ServiceId.LaraueBoards).SubmitFeedbackAsync(request);

        var text = Assert.Single(SentMessages()).Text;
        Assert.Equal(
            $"""
            🐞 Bug #{response.FeedbackId} · Laraue Boards
            From: Ilya Belyansky · @ilya · ilya@example.com ({userId})
            page: https://boards.laraue.com/organizations/test/issues

            The board doesn't load
            """,
            text);
    }

    [Fact]
    public async Task SubmitFeedback_ShouldShowContactAfterSender_WhenContactIsSet()
    {
        using var testScope = host.CreateTestScope();
        var userId = Guid.NewGuid();
        var request = ValidRequest(userId);
        request.Contact = "@ilya_tg";
        request.Context.Add("page", "https://boards.laraue.com/organizations/test/issues");

        await host.CreateFeedbackClient(ServiceId.LaraueBoards).SubmitFeedbackAsync(request);

        Assert.Contains(
            $"""
            ({userId})
            Contact: @ilya_tg
            page: https://boards.laraue.com/organizations/test/issues

            Add dark mode
            """,
            Assert.Single(SentMessages()).Text);
    }

    [Fact]
    public async Task SubmitFeedback_ShouldNotShowContact_WhenContactIsNotSet()
    {
        using var testScope = host.CreateTestScope();

        await host.CreateFeedbackClient(ServiceId.LaraueBoards).SubmitFeedbackAsync(ValidRequest());

        Assert.DoesNotContain("Contact:", Assert.Single(SentMessages()).Text);
    }

    [Fact]
    public async Task SubmitFeedback_ShouldListEveryContextEntry_WhenContextIsSet()
    {
        using var testScope = host.CreateTestScope();
        var request = ValidRequest();
        request.Context.Add("locale", "en");
        request.Context.Add("browser", "Firefox 140");

        await host.CreateFeedbackClient(ServiceId.LaraueBoards).SubmitFeedbackAsync(request);

        var text = Assert.Single(SentMessages()).Text;
        Assert.Contains("\nlocale: en\n", text);
        Assert.Contains("\nbrowser: Firefox 140\n", text);
    }

    [Fact]
    public async Task SubmitFeedback_ShouldSendToTeamChat_WhenFeedbackIsSubmitted()
    {
        using var testScope = host.CreateTestScope();

        await host.CreateFeedbackClient(ServiceId.LaraueBoards).SubmitFeedbackAsync(ValidRequest());

        Assert.Equal(TeamChatId, Assert.Single(SentMessages()).ChatId.Identifier);
    }

    [Fact]
    public async Task SubmitFeedback_ShouldNotDuplicateUserName_WhenItIsTheDisplayName()
    {
        using var testScope = host.CreateTestScope();
        host.SetupProfile(new GetUserProfileResponse { DisplayName = "ilya", Initials = "I", UserName = "ilya" });

        await host.CreateFeedbackClient(ServiceId.LaraueBoards).SubmitFeedbackAsync(ValidRequest());

        Assert.Contains("From: ilya (", Assert.Single(SentMessages()).Text);
    }

    [Fact]
    public async Task SubmitFeedback_ShouldNotifyWithUserIdOnly_WhenIdentityIsUnavailable()
    {
        using var testScope = host.CreateTestScope();
        host.IdentityClientMock
            .Setup(x => x.GetUserProfileAsync(
                It.IsAny<GetUserProfileRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Unavailable, "Identity is down")));
        var userId = Guid.NewGuid();

        var response = await host.CreateFeedbackClient(ServiceId.LaraueBoards)
            .SubmitFeedbackAsync(ValidRequest(userId));

        Assert.Contains($"From: {userId}\n", Assert.Single(SentMessages()).Text);
        Assert.True(await testScope.Database.FeedbackItems.AnyAsync(x => x.Id == response.FeedbackId));
    }

    [Fact]
    public async Task SubmitFeedback_ShouldStoreFeedback_WhenTelegramSendFails()
    {
        using var testScope = host.CreateTestScope();
        host.BotClientMock
            .Setup(x => x.SendRequest(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiRequestException("Bad Request: chat not found", 400));

        var response = await host.CreateFeedbackClient(ServiceId.LaraueBoards).SubmitFeedbackAsync(ValidRequest());

        Assert.True(await testScope.Database.FeedbackItems.AnyAsync(x => x.Id == response.FeedbackId));
    }

    [Fact]
    public async Task SubmitFeedback_ShouldNotNotify_WhenFeedbackIsInvalid()
    {
        using var testScope = host.CreateTestScope();
        var request = ValidRequest();
        request.Message = "";

        await Assert.ThrowsAsync<RpcException>(() => host.CreateFeedbackClient(ServiceId.LaraueBoards)
            .SubmitFeedbackAsync(request).ResponseAsync);

        Assert.Empty(SentMessages());
    }

    [Fact]
    public void BuildText_ShouldCutMessage_WhenTextExceedsTelegramLimit()
    {
        var item = NewItem(new string('a', Domain.FeedbackItem.MessageMaxLength));

        var text = FeedbackNotifier.BuildText(item, sender: new string('s', 2000));

        Assert.Equal(4096, text.Length);
        Assert.EndsWith("a…", text);
    }

    [Fact]
    public void BuildText_ShouldCutContextValue_WhenItIsLongerThanShown()
    {
        var item = NewItem("Add dark mode");
        item.Context["page"] = new string('p', Domain.FeedbackItem.ContextValueMaxLength);

        var text = FeedbackNotifier.BuildText(item, sender: null);

        Assert.Contains($"\npage: {new string('p', FeedbackNotifier.ContextValueShownMaxLength - 1)}…\n", text);
    }

    [Fact]
    public void BuildText_ShouldDropContextEntriesKeepingMessage_WhenTheyDontFit()
    {
        var message = new string('a', Domain.FeedbackItem.MessageMaxLength);
        var item = NewItem(message);
        item.Contact = new string('c', Domain.FeedbackItem.ContactMaxLength);
        for (var i = 0; i < Domain.FeedbackItem.ContextMaxEntries; i++)
        {
            item.Context[$"key{i}"] = new string('v', Domain.FeedbackItem.ContextValueMaxLength);
        }

        var text = FeedbackNotifier.BuildText(item, sender: null);

        Assert.True(text.Length <= 4096);
        Assert.Contains("\nkey0: ", text);
        Assert.DoesNotContain($"\nkey{Domain.FeedbackItem.ContextMaxEntries - 1}: ", text);
        Assert.EndsWith($"\n…\n\n{message}", text);
    }

    private static Domain.FeedbackItem NewItem(string message) => new()
    {
        Id = 1,
        UserId = Guid.NewGuid(),
        ServiceId = Domain.ServiceId.LaraueBoards,
        Type = Domain.FeedbackType.Bug,
        Message = message,
    };

    private static SubmitFeedbackRequest ValidRequest(Guid? userId = null) => new()
    {
        UserId = (userId ?? Guid.NewGuid()).ToString(),
        Type = FeedbackType.Idea,
        Message = "Add dark mode",
    };

    private IEnumerable<SendMessageRequest> SentMessages() => host.BotClientMock.Invocations
        .Where(x => x.Method.Name == nameof(Telegram.Bot.ITelegramBotClient.SendRequest))
        .Select(x => x.Arguments[0])
        .OfType<SendMessageRequest>();
}
