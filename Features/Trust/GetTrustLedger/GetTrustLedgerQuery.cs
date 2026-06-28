using MediatR;
using VibeTrade.Backend.Features.Trust.Dtos;

namespace VibeTrade.Backend.Features.Trust.GetTrustLedger;

public sealed record GetTrustLedgerQuery(string SubjectType, string SubjectId, int Limit)
    : IRequest<IReadOnlyList<TrustHistoryItemDto>>;
