using Grpc.Core;
using Laraue.Apps.Feedback.Internal.Contracts;
using Laraue.Apps.Feedback.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Domain = Laraue.Apps.Feedback.DataAccess.Entities;

namespace Laraue.Apps.Feedback.IntegrationTests;

[Collection("IntegrationTest")]
public class SubmitFeedbackTests(InternalApiTestHost host) : IClassFixture<InternalApiTestHost>
{
    [Fact]
    public async Task SubmitFeedback_ShouldStoreNewFeedback_WhenRequestIsValid()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateFeedbackClient(ServiceId.LaraueBoards);
        var userId = Guid.NewGuid();

        var request = ValidRequest(userId);
        request.Type = FeedbackType.Idea;
        request.Message = "  Add dark mode  ";
        request.Context.Add("page", "https://boards.laraue.com/organizations/test/issues");
        request.Context.Add("locale", "en");

        var response = await client.SubmitFeedbackAsync(request);

        var item = await testScope.Database.FeedbackItems.SingleAsync(x => x.Id == response.FeedbackId);

        Assert.Equal(userId, item.UserId);
        Assert.Equal(Domain.ServiceId.LaraueBoards, item.ServiceId);
        Assert.Equal(Domain.FeedbackType.Idea, item.Type);
        Assert.Equal("Add dark mode", item.Message);
        Assert.Equal(Domain.FeedbackStatus.New, item.Status);
        Assert.Equal("https://boards.laraue.com/organizations/test/issues", item.Context["page"]);
        Assert.Equal("en", item.Context["locale"]);
    }

    [Fact]
    public async Task SubmitFeedback_ShouldAcceptMessage_WhenItIsAtMaxLength()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateFeedbackClient(ServiceId.LaraueBoards);

        var request = ValidRequest();
        request.Message = new string('a', Domain.FeedbackItem.MessageMaxLength);

        var response = await client.SubmitFeedbackAsync(request);

        Assert.True(await testScope.Database.FeedbackItems.AnyAsync(x => x.Id == response.FeedbackId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SubmitFeedback_ShouldReturnInvalidArgument_WhenMessageIsEmpty(string message)
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateFeedbackClient(ServiceId.LaraueBoards);

        var request = ValidRequest();
        request.Message = message;

        await AssertInvalidArgumentAsync(testScope, client, request);
    }

    [Fact]
    public async Task SubmitFeedback_ShouldReturnInvalidArgument_WhenMessageIsTooLong()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateFeedbackClient(ServiceId.LaraueBoards);

        var request = ValidRequest();
        request.Message = new string('a', Domain.FeedbackItem.MessageMaxLength + 1);

        await AssertInvalidArgumentAsync(testScope, client, request);
    }

    [Fact]
    public async Task SubmitFeedback_ShouldReturnInvalidArgument_WhenTypeIsUnspecified()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateFeedbackClient(ServiceId.LaraueBoards);

        var request = ValidRequest();
        request.Type = FeedbackType.Unspecified;

        await AssertInvalidArgumentAsync(testScope, client, request);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task SubmitFeedback_ShouldReturnInvalidArgument_WhenUserIdIsInvalid(string userId)
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateFeedbackClient(ServiceId.LaraueBoards);

        var request = ValidRequest();
        request.UserId = userId;

        await AssertInvalidArgumentAsync(testScope, client, request);
    }

    [Fact]
    public async Task SubmitFeedback_ShouldReturnInvalidArgument_WhenContextHasTooManyEntries()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateFeedbackClient(ServiceId.LaraueBoards);

        var request = ValidRequest();
        for (var i = 0; i <= Domain.FeedbackItem.ContextMaxEntries; i++)
        {
            request.Context.Add($"key{i}", "value");
        }

        await AssertInvalidArgumentAsync(testScope, client, request);
    }

    [Fact]
    public async Task SubmitFeedback_ShouldReturnInvalidArgument_WhenContextValueIsTooLong()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateFeedbackClient(ServiceId.LaraueBoards);

        var request = ValidRequest();
        request.Context.Add("page", new string('a', Domain.FeedbackItem.ContextValueMaxLength + 1));

        await AssertInvalidArgumentAsync(testScope, client, request);
    }

    [Fact]
    public async Task SubmitFeedback_ShouldReturnInvalidArgument_WhenServiceIdHeaderIsMissing()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateFeedbackClientWithoutServiceIdHeader();

        await AssertInvalidArgumentAsync(testScope, client, ValidRequest());
    }

    [Fact]
    public async Task SubmitFeedback_ShouldReturnInvalidArgument_WhenServiceIdIsUnknown()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateFeedbackClient((ServiceId)999);

        await AssertInvalidArgumentAsync(testScope, client, ValidRequest());
    }

    private static SubmitFeedbackRequest ValidRequest(Guid? userId = null) => new()
    {
        UserId = (userId ?? Guid.NewGuid()).ToString(),
        Type = FeedbackType.Bug,
        Message = "The board doesn't load",
    };

    private static async Task AssertInvalidArgumentAsync(
        InternalApiTestHostScope testScope,
        FeedbackService.FeedbackServiceClient client,
        SubmitFeedbackRequest request)
    {
        var exception = await Assert.ThrowsAsync<RpcException>(() => client.SubmitFeedbackAsync(request).ResponseAsync);

        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
        Assert.False(await testScope.Database.FeedbackItems.AnyAsync());
    }
}
