namespace Ta3lom.Store.Domain.ReadModels;

/// `v_goal`: campaign progress including seed figures (RUL-08).
public sealed record GoalView(Guid ProductId, string Slug, string Type, decimal? TargetAmount, int? TargetUnits,
    decimal? RaisedAmount, int? FundedUnits, int? DonorCount, DateTimeOffset? EndsAt);

/// `v_sales_daily`: paid lines per Riyadh day, kind and product type.
public sealed record SalesDailyView(DateOnly? Day, string Kind, string ProductType, long Orders, decimal? Gross, decimal? Vat, decimal? Refunded);

/// `v_allocation_backlog`: units per status per sponsorable product (RUL-48, RUL-49).
public sealed record AllocationBacklogView(Guid ProductId, string Slug, long Awaiting, long Overdue30, long Allocated, long Completed, DateTimeOffset? OldestAwaiting);

/// `v_zakat_balance`: zakat in/out per product (RUL-20).
public sealed record ZakatBalanceView(Guid ProductId, decimal? Received, decimal? Disbursed, decimal? Refunded, decimal? Balance);
