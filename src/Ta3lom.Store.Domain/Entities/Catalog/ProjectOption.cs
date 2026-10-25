namespace Ta3lom.Store.Domain.Entities.Catalog;

/// Table `project_option` (BR-A03): a fixed-price donation option of a project, e.g. "كفالة يوم 100 ﷼".
public sealed class ProjectOption : Entity
{
    public const int LabelMax = 60;

    internal ProjectOption(Guid projectId, ProjectOptionInput input) : base(Guid.CreateVersion7())
    {
        ProjectId = projectId;
        Change(input);
    }

    private ProjectOption()
    {
    }

    public Guid ProjectId { get; private set; }

    public string LabelAr { get; private set; } = null!;

    public string? LabelEn { get; private set; }

    public Money Amount { get; private set; }

    public int Sort { get; private set; }

    public bool IsActive { get; private set; }

    internal void Change(ProjectOptionInput input)
    {
        var label = Guard.NotEmpty(input.LabelAr).Trim();
        Guard.Against(label.Length is < 2 or > LabelMax, "project.option_label_length");
        Guard.Against(!input.Amount.IsPositive, "project.option_amount_invalid");
        LabelAr = label;
        LabelEn = string.IsNullOrWhiteSpace(input.LabelEn) ? null : input.LabelEn.Trim();
        Amount = input.Amount;
        Sort = input.Sort;
        IsActive = input.IsActive;
    }

    internal void Deactivate() => IsActive = false;
}

/// Table `project_slug_history` (D-08): an old slug of a project, answered with `{ redirectTo }`.
public sealed class ProjectSlugHistory
{
    internal ProjectSlugHistory(Guid projectId, string oldSlug, DateTimeOffset now)
    {
        ProjectId = projectId;
        OldSlug = oldSlug;
        ChangedAt = now;
    }

    private ProjectSlugHistory()
    {
    }

    public long Id { get; private set; }

    public Guid ProjectId { get; private set; }

    public string OldSlug { get; private set; } = null!;

    public DateTimeOffset ChangedAt { get; private set; }
}
