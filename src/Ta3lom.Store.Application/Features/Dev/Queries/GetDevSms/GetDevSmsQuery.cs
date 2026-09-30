namespace Ta3lom.Store.Application.Features.Dev.Queries.GetDevSms;

/// `GET /dev/sms`: the development SMS sink (plan 14 §4.3). Development/Staging only.
public sealed record GetDevSmsQuery(int Count = 50) : IQuery<IReadOnlyList<DevSmsMessage>>;

internal sealed class GetDevSmsQueryHandler(IDevSmsInbox inbox) : IQueryHandler<GetDevSmsQuery, IReadOnlyList<DevSmsMessage>>
{
    public Task<Result<IReadOnlyList<DevSmsMessage>>> HandleAsync(GetDevSmsQuery query, CancellationToken ct = default) =>
        Task.FromResult(Result.Success(inbox.Latest(Math.Clamp(query.Count, 1, 200))));
}
