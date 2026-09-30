namespace Ta3lom.Store.Application.DTOs.Allocation;

/// `GET /v1/admin/allocation/backlog` row (`v_allocation_backlog`, RUL-48/49).
public sealed record BacklogRow(Guid ProductId, string Slug, string TitleAr, long Awaiting, long Overdue30, long Allocated, long Completed,
    DateTimeOffset? OldestAwaiting, string? GroupLabelAr);

/// A sponsored unit (FIFO list, batch view, completion). Beneficiary shown by code only; donors never see it (PDPL).
public sealed record UnitRow(Guid Id, string Source, string UnitPrice, bool HasZakat, string Status, DateTimeOffset FundedAt,
    string? BeneficiaryCode, DateTimeOffset? AllocatedAt, string? AllocatedBy, DateTimeOffset? CompletedAt, DateTimeOffset? LapsedAt);

/// `POST …/allocate` body: 1–200 distinct beneficiaries.
public sealed record AllocateRequest(IReadOnlyList<Guid>? BeneficiaryIds, string? Note);

public sealed record AllocationResult(Guid BatchId, int Allocated, bool UnitsShort, IReadOnlyList<AllocatedUnit> Units);

public sealed record AllocatedUnit(Guid UnitId, string BeneficiaryCode, DateTimeOffset FundedAt, bool HasZakat);

/// `GET /v1/admin/allocation/batches/{id}`.
public sealed record AllocationBatchResponse(Guid Id, Guid ProductId, string Slug, string? Note, string CreatedBy, DateTimeOffset CreatedAt,
    IReadOnlyList<UnitRow> Units);

/// `POST /v1/admin/units/{id}/complete` (book copies): the delivery date (Riyadh) and an optional proof image.
public sealed record CompleteUnitRequest(DateOnly? DeliveredOn, Guid? ProofAssetId);
