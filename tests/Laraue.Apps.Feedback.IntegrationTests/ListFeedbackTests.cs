using Grpc.Core;
using Laraue.Apps.Feedback.Internal.Contracts;
using Laraue.Apps.Feedback.IntegrationTests.Infrastructure;
using Domain = Laraue.Apps.Feedback.DataAccess.Entities;

namespace Laraue.Apps.Feedback.IntegrationTests;

[Collection("IntegrationTest")]
public class ListFeedbackTests(InternalApiTestHost host) : IClassFixture<InternalApiTestHost>
{
    [Fact]
    public async Task ListFeedback_ShouldReturnNewestFirst_WhenNoFilterIsSet()
    {
        using var testScope = host.CreateTestScope();
        await SeedAsync(testScope, ("first", Domain.FeedbackStatus.New), ("second", Domain.FeedbackStatus.New));
        var client = host.CreateFeedbackClientWithoutServiceIdHeader();

        var response = await client.ListFeedbackAsync(new ListFeedbackRequest { Page = 0, PerPage = 10 });

        Assert.Equal(["second", "first"], response.Items.Select(x => x.Message));
        Assert.False(response.HasNextPage);
    }

    [Fact]
    public async Task ListFeedback_ShouldReturnItemFields_WhenFeedbackWasSubmitted()
    {
        using var testScope = host.CreateTestScope();
        var userId = Guid.NewGuid();
        var submitRequest = new SubmitFeedbackRequest
        {
            UserId = userId.ToString(),
            Type = FeedbackType.Question,
            Message = "How do I invite a member?",
            Contact = "@ilya_tg",
        };
        submitRequest.Context.Add("page", "https://boards.laraue.com/organizations/test/members");
        var submitted = await host.CreateFeedbackClient(ServiceId.LaraueBoards).SubmitFeedbackAsync(submitRequest);

        var response = await host.CreateFeedbackClientWithoutServiceIdHeader()
            .ListFeedbackAsync(new ListFeedbackRequest { Page = 0, PerPage = 10 });

        var item = Assert.Single(response.Items);
        Assert.Equal(submitted.FeedbackId, item.Id);
        Assert.Equal(userId.ToString(), item.UserId);
        Assert.Equal(ServiceId.LaraueBoards, item.Service);
        Assert.Equal(FeedbackType.Question, item.Type);
        Assert.Equal(FeedbackStatus.New, item.Status);
        Assert.Equal("@ilya_tg", item.Contact);
        Assert.Equal("https://boards.laraue.com/organizations/test/members", item.Context["page"]);
        Assert.NotNull(item.CreatedAt);
    }

    [Fact]
    public async Task ListFeedback_ShouldReturnOnlyMatchingStatus_WhenStatusFilterIsSet()
    {
        using var testScope = host.CreateTestScope();
        await SeedAsync(testScope, ("new", Domain.FeedbackStatus.New), ("seen", Domain.FeedbackStatus.Seen));
        var client = host.CreateFeedbackClientWithoutServiceIdHeader();

        var response = await client.ListFeedbackAsync(new ListFeedbackRequest
        {
            Status = FeedbackStatus.Seen,
            Page = 0,
            PerPage = 10,
        });

        Assert.Equal(["seen"], response.Items.Select(x => x.Message));
    }

    [Fact]
    public async Task ListFeedback_ShouldReturnOnlyMatchingService_WhenServiceFilterIsSet()
    {
        using var testScope = host.CreateTestScope();
        await SeedAsync(testScope, ("boards", Domain.FeedbackStatus.New));
        var client = host.CreateFeedbackClientWithoutServiceIdHeader();

        var response = await client.ListFeedbackAsync(new ListFeedbackRequest
        {
            Service = ServiceId.LaraueBoards,
            Page = 0,
            PerPage = 10,
        });

        Assert.Equal(["boards"], response.Items.Select(x => x.Message));
    }

    [Fact]
    public async Task ListFeedback_ShouldSplitIntoPages_WhenThereAreMoreItemsThanPerPage()
    {
        using var testScope = host.CreateTestScope();
        await SeedAsync(
            testScope,
            ("1", Domain.FeedbackStatus.New),
            ("2", Domain.FeedbackStatus.New),
            ("3", Domain.FeedbackStatus.New));
        var client = host.CreateFeedbackClientWithoutServiceIdHeader();

        var firstPage = await client.ListFeedbackAsync(new ListFeedbackRequest { Page = 0, PerPage = 2 });
        var secondPage = await client.ListFeedbackAsync(new ListFeedbackRequest { Page = 1, PerPage = 2 });

        Assert.Equal(["3", "2"], firstPage.Items.Select(x => x.Message));
        Assert.True(firstPage.HasNextPage);
        Assert.Equal(["1"], secondPage.Items.Select(x => x.Message));
        Assert.False(secondPage.HasNextPage);
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(0, 0)]
    [InlineData(0, 101)]
    public async Task ListFeedback_ShouldReturnInvalidArgument_WhenPaginationIsOutOfRange(int page, int perPage)
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateFeedbackClientWithoutServiceIdHeader();

        var exception = await Assert.ThrowsAsync<RpcException>(() => client.ListFeedbackAsync(
            new ListFeedbackRequest { Page = page, PerPage = perPage }).ResponseAsync);

        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
    }

    /// <summary>
    /// Adds feedback rows in the given order, oldest first.
    /// </summary>
    private static async Task SeedAsync(
        InternalApiTestHostScope testScope,
        params (string Message, Domain.FeedbackStatus Status)[] items)
    {
        foreach (var (message, status) in items)
        {
            testScope.Database.FeedbackItems.Add(new Domain.FeedbackItem
            {
                UserId = Guid.NewGuid(),
                ServiceId = Domain.ServiceId.LaraueBoards,
                Type = Domain.FeedbackType.Bug,
                Message = message,
                Status = status,
                CreatedAt = DateTime.UtcNow,
            });

            await testScope.Database.SaveChangesAsync();
        }
    }
}
