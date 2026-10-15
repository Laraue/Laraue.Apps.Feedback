using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Laraue.Apps.Feedback.Services;
using Laraue.Core.DataAccess.Contracts;
using Laraue.Core.Exceptions.Web;
// The generated proto types share names with the domain ones (FeedbackService, ServiceId, FeedbackType,
// FeedbackStatus, FeedbackItem) - alias the contract side to keep both usable here.
using Contracts = Laraue.Apps.Feedback.Internal.Contracts;
using Domain = Laraue.Apps.Feedback.DataAccess.Entities;

namespace Laraue.Apps.Feedback.InternalApiServices;

/// <summary>
/// gRPC-facing adapter over <see cref="IFeedbackService"/>: translates between the wire contract
/// (<c>Laraue.Apps.Feedback.Internal.Contracts</c>) and the domain types. No business logic of its own.
/// </summary>
public sealed class FeedbackGrpcService(IFeedbackService feedbackService)
    : Contracts.FeedbackService.FeedbackServiceBase
{
    public override async Task<Contracts.SubmitFeedbackResponse> SubmitFeedback(
        Contracts.SubmitFeedbackRequest request,
        ServerCallContext context)
    {
        var serviceId = ReadDomainServiceId(context);
        var data = new SubmitFeedbackData(
            ParseUserId(request.UserId),
            ToDomainType(request.Type),
            request.Message,
            request.Context);

        var id = await ExecuteAsync(() => feedbackService.SubmitAsync(serviceId, data, context.CancellationToken));

        return new Contracts.SubmitFeedbackResponse { FeedbackId = id };
    }

    public override async Task<Contracts.ListFeedbackResponse> ListFeedback(
        Contracts.ListFeedbackRequest request,
        ServerCallContext context)
    {
        var filter = new FeedbackFilter(
            request.HasService ? ToDomainServiceId(request.Service) : null,
            request.HasStatus ? ToDomainStatus(request.Status) : null);

        var page = await ExecuteAsync(() => feedbackService.ListAsync(
            filter,
            new PaginationData { Page = request.Page, PerPage = request.PerPage },
            context.CancellationToken));

        var response = new Contracts.ListFeedbackResponse { HasNextPage = page.HasNextPage };
        response.Items.AddRange(page.Data.Select(ToContractItem));

        return response;
    }

    private static async Task<T> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (BadRequestException ex)
        {
            var detail = string.Join(" ", ex.Errors.SelectMany(x => x.Value));
            throw new RpcException(new Status(StatusCode.InvalidArgument, detail));
        }
    }

    private static Contracts.FeedbackItem ToContractItem(FeedbackListItem item)
    {
        var contractItem = new Contracts.FeedbackItem
        {
            Id = item.Id,
            UserId = item.UserId.ToString(),
            Service = ToContractServiceId(item.ServiceId),
            Type = ToContractType(item.Type),
            Message = item.Message,
            Status = ToContractStatus(item.Status),
            CreatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(item.CreatedAt, DateTimeKind.Utc)),
        };

        contractItem.Context.Add(item.Context);

        return contractItem;
    }

    private static Guid ParseUserId(string userId)
    {
        return Guid.TryParse(userId, out var parsed)
            ? parsed
            : throw new RpcException(new Status(StatusCode.InvalidArgument, $"Invalid user id '{userId}'."));
    }

    /// <summary>
    /// The calling service, from the <see cref="Contracts.GrpcHeaders.ServiceIdHeaderName"/> header.
    /// </summary>
    private static Domain.ServiceId ReadDomainServiceId(ServerCallContext context)
    {
        var header = context.RequestHeaders.Get(Contracts.GrpcHeaders.ServiceIdHeaderName)?.Value;
        if (header is null || !int.TryParse(header, out var rawServiceId))
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                $"Missing or invalid '{Contracts.GrpcHeaders.ServiceIdHeaderName}' header."));
        }

        return ToDomainServiceId((Contracts.ServiceId)rawServiceId);
    }

    private static Domain.ServiceId ToDomainServiceId(Contracts.ServiceId serviceId) => serviceId switch
    {
        Contracts.ServiceId.LaraueBoards => Domain.ServiceId.LaraueBoards,
        _ => throw new RpcException(new Status(StatusCode.InvalidArgument, $"Unknown service '{serviceId}'.")),
    };

    private static Contracts.ServiceId ToContractServiceId(Domain.ServiceId serviceId) => serviceId switch
    {
        Domain.ServiceId.LaraueBoards => Contracts.ServiceId.LaraueBoards,
        _ => throw new ArgumentOutOfRangeException(nameof(serviceId), serviceId, null),
    };

    private static Domain.FeedbackType ToDomainType(Contracts.FeedbackType type) => type switch
    {
        Contracts.FeedbackType.Bug => Domain.FeedbackType.Bug,
        Contracts.FeedbackType.Idea => Domain.FeedbackType.Idea,
        Contracts.FeedbackType.Question => Domain.FeedbackType.Question,
        _ => throw new RpcException(new Status(StatusCode.InvalidArgument, $"Unknown feedback type '{type}'.")),
    };

    private static Contracts.FeedbackType ToContractType(Domain.FeedbackType type) => type switch
    {
        Domain.FeedbackType.Bug => Contracts.FeedbackType.Bug,
        Domain.FeedbackType.Idea => Contracts.FeedbackType.Idea,
        Domain.FeedbackType.Question => Contracts.FeedbackType.Question,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    private static Domain.FeedbackStatus ToDomainStatus(Contracts.FeedbackStatus status) => status switch
    {
        Contracts.FeedbackStatus.New => Domain.FeedbackStatus.New,
        Contracts.FeedbackStatus.Seen => Domain.FeedbackStatus.Seen,
        Contracts.FeedbackStatus.Done => Domain.FeedbackStatus.Done,
        _ => throw new RpcException(new Status(StatusCode.InvalidArgument, $"Unknown feedback status '{status}'.")),
    };

    private static Contracts.FeedbackStatus ToContractStatus(Domain.FeedbackStatus status) => status switch
    {
        Domain.FeedbackStatus.New => Contracts.FeedbackStatus.New,
        Domain.FeedbackStatus.Seen => Contracts.FeedbackStatus.Seen,
        Domain.FeedbackStatus.Done => Contracts.FeedbackStatus.Done,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}
