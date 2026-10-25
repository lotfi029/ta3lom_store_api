using Ta3lom.Store.Domain.Entities.Media;
using Ta3lom.Store.Domain.Events;

namespace Ta3lom.Store.Domain.Entities.Catalog;

/// Table `project` (BR-A02 … BR-A04): a donation project in a category, with 0…n fixed-price options and/or an open amount.
/// Status changes follow D-07; publishing needs the D-06 fields; a published project keeps its old slugs for redirects (D-08).
public sealed class Project : AggregateRoot
{
    public const int TitleMax = 160;
    public const int SummaryMax = 300;

    private readonly List<ProjectOption> _options = [];
    private readonly List<ProjectSlugHistory> _slugHistory = [];

    private Project(Guid id, DateTimeOffset now) : base(id)
    {
        Status = ProjectStatus.Draft;
        AllowOpenAmount = true;
        CreatedAt = now;
        UpdatedAt = now;
    }

    private Project()
    {
    }

    public Guid CategoryId { get; private set; }

    public string Slug { get; private set; } = null!;

    public ProjectStatus Status { get; private set; }

    public string TitleAr { get; private set; } = null!;

    public string? TitleEn { get; private set; }

    public string? SummaryAr { get; private set; }

    public string? SummaryEn { get; private set; }

    /// Markdown.
    public string? BodyAr { get; private set; }

    public string? BodyEn { get; private set; }

    public Guid? CoverAssetId { get; private set; }

    public bool AllowOpenAmount { get; private set; }

    /// The project's own minimum for open amounts; null = the `min_gift_amount` setting (D-03).
    public Money? MinAmount { get; private set; }

    public ProjectBadge? Badge { get; private set; }

    public int Sort { get; private set; }

    public string? SeoTitleAr { get; private set; }

    public string? SeoTitleEn { get; private set; }

    public string? SeoDescAr { get; private set; }

    public string? SeoDescEn { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public IReadOnlyList<ProjectOption> Options => _options;

    public IReadOnlyList<ProjectSlugHistory> SlugHistory => _slugHistory;

    public bool IsPublished => Status == ProjectStatus.Published;

    public bool IsVersion(uint version) => Version == version;

    public static Project CreateDraft(ProjectDetails details, Category category, MediaAsset? cover, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(category);
        var project = new Project(Guid.CreateVersion7(), now);
        project.Apply(details, category, cover, now);
        project.Raise(new CatalogChanged("project", project.Id, "created"));
        return project;
    }

    /// Edits the details. A published project must stay publishable (D-06), so an edit that breaks it is refused.
    public Result Update(ProjectDetails details, Category category, MediaAsset? cover, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(category);
        if (IsPublished && MissingForPublish(details.TitleAr, details.SummaryAr, details.BodyAr, cover, category, details.AllowOpenAmount, _options) is { Count: > 0 } missing)
        {
            return CatalogErrors.PublishIncomplete(missing);
        }

        Apply(details, category, cover, now);
        Raise(new CatalogChanged("project", Id, "updated"));
        return Result.Success();
    }

    /// Replaces the option list. Options already used in an order are deactivated, not deleted (their order lines keep
    /// the snapshot). An input without an id that has the label of an existing option reuses that option.
    public Result ReplaceOptions(IReadOnlyList<ProjectOptionInput> inputs, IReadOnlySet<Guid> usedInOrders, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(usedInOrders);
        Guard.Against(inputs.Select(i => i.LabelAr.Trim()).Distinct(StringComparer.Ordinal).Count() != inputs.Count, "project.option_label_duplicate");

        if (IsPublished && !AllowOpenAmount && !inputs.Any(i => i.IsActive))
        {
            return CatalogErrors.PublishIncomplete(["amount"]);
        }

        var kept = new HashSet<Guid>();
        foreach (var input in inputs)
        {
            var existing = input.Id is { } id
                ? _options.FirstOrDefault(o => o.Id == id)
                : _options.FirstOrDefault(o => string.Equals(o.LabelAr, input.LabelAr.Trim(), StringComparison.Ordinal));
            if (existing is null)
            {
                existing = new ProjectOption(Id, input);
                _options.Add(existing);
            }
            else
            {
                existing.Change(input);
            }

            kept.Add(existing.Id);
        }

        foreach (var removed in _options.Where(o => !kept.Contains(o.Id)).ToList())
        {
            if (usedInOrders.Contains(removed.Id))
            {
                removed.Deactivate();
            }
            else
            {
                _options.Remove(removed);
            }
        }

        UpdatedAt = now;
        Raise(new CatalogChanged("project", Id, "options"));
        return Result.Success();
    }

    /// D-06 requirements that are not met yet (empty = publishable).
    public IReadOnlyList<string> MissingForPublish(Category? category, MediaAsset? cover) =>
        MissingForPublish(TitleAr, SummaryAr, BodyAr, cover, category, AllowOpenAmount, _options);

    /// D-07: draft → published and archived → published. D-06 must hold.
    public Result Publish(Category? category, MediaAsset? cover, DateTimeOffset now)
    {
        if (Status == ProjectStatus.Published)
        {
            return CatalogErrors.InvalidTransition(Status, ProjectStatus.Published);
        }

        if (MissingForPublish(category, cover) is { Count: > 0 } missing)
        {
            return CatalogErrors.PublishIncomplete(missing);
        }

        Status = ProjectStatus.Published;
        PublishedAt ??= now;
        UpdatedAt = now;
        Raise(new CatalogChanged("project", Id, "published"));
        return Result.Success();
    }

    /// D-07: published → archived. Archived projects leave the listings; their orders keep their snapshots.
    public Result Archive(DateTimeOffset now)
    {
        if (Status != ProjectStatus.Published)
        {
            return CatalogErrors.InvalidTransition(Status, ProjectStatus.Archived);
        }

        Status = ProjectStatus.Archived;
        UpdatedAt = now;
        Raise(new CatalogChanged("project", Id, "archived"));
        return Result.Success();
    }

    /// D-05: the active option with this id, if the project takes new lines.
    public ProjectOption? ActiveOption(Guid optionId) => _options.FirstOrDefault(o => o.Id == optionId && o.IsActive);

    /// The minimum of an open amount (D-03): the project's own, else the setting.
    public Money MinimumOpenAmount(Money settingMinimum) => MinAmount ?? settingMinimum;

    private void Apply(ProjectDetails d, Category category, MediaAsset? cover, DateTimeOffset now)
    {
        Guard.Against(!ValueObjects.Slug.IsValidProject(d.Slug), "project.slug_invalid");
        Guard.Against(cover is not null && !cover.IsPublicImage, "media.image_required");
        Guard.Against(d.MinAmount is { IsPositive: false }, "project.min_amount_invalid");
        var title = Guard.NotEmpty(d.TitleAr).Trim();
        Guard.Against(title.Length is < 2 or > TitleMax, "project.title_length");
        Guard.Against(d.SummaryAr is { Length: > SummaryMax } || d.SummaryEn is { Length: > SummaryMax }, "project.summary_length");

        ChangeSlug(d.Slug, now);
        CategoryId = category.Id;
        TitleAr = title;
        TitleEn = Clean(d.TitleEn);
        SummaryAr = Clean(d.SummaryAr);
        SummaryEn = Clean(d.SummaryEn);
        BodyAr = Clean(d.BodyAr);
        BodyEn = Clean(d.BodyEn);
        CoverAssetId = cover?.Id;
        AllowOpenAmount = d.AllowOpenAmount;
        MinAmount = d.MinAmount;
        Badge = d.Badge;
        Sort = d.Sort;
        SeoTitleAr = Clean(d.SeoTitleAr);
        SeoTitleEn = Clean(d.SeoTitleEn);
        SeoDescAr = Clean(d.SeoDescAr);
        SeoDescEn = Clean(d.SeoDescEn);
        UpdatedAt = now;
    }

    /// D-08: once the project has been published its old slug is kept, so links keep working through a redirect.
    private void ChangeSlug(string slug, DateTimeOffset now)
    {
        if (Slug is null || string.Equals(Slug, slug, StringComparison.Ordinal))
        {
            Slug = slug;
            return;
        }

        if (PublishedAt is not null && _slugHistory.All(h => h.OldSlug != Slug))
        {
            _slugHistory.Add(new ProjectSlugHistory(Id, Slug, now));
        }

        _slugHistory.RemoveAll(h => h.OldSlug == slug);                       // the project takes back an old slug
        Slug = slug;
    }

    private static List<string> MissingForPublish(string? titleAr, string? summaryAr, string? bodyAr, MediaAsset? cover, Category? category,
        bool allowOpenAmount, IEnumerable<ProjectOption> options)
    {
        List<string> missing = [];
        if (string.IsNullOrWhiteSpace(titleAr))
        {
            missing.Add("titleAr");
        }

        if (string.IsNullOrWhiteSpace(summaryAr))
        {
            missing.Add("summaryAr");
        }

        if (string.IsNullOrWhiteSpace(bodyAr))
        {
            missing.Add("bodyAr");
        }

        if (cover is not { IsPublicImage: true })
        {
            missing.Add("cover");
        }

        if (category is null)
        {
            missing.Add("category");
        }

        if (!allowOpenAmount && !options.Any(o => o.IsActive))
        {
            missing.Add("amount");
        }

        return missing;
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

/// The staff-editable fields of a project (the category and cover are passed as loaded entities).
public sealed record ProjectDetails(
    string Slug,
    string TitleAr,
    string? TitleEn,
    string? SummaryAr,
    string? SummaryEn,
    string? BodyAr,
    string? BodyEn,
    bool AllowOpenAmount,
    Money? MinAmount,
    ProjectBadge? Badge,
    int Sort,
    string? SeoTitleAr,
    string? SeoTitleEn,
    string? SeoDescAr,
    string? SeoDescEn);

/// One entry of `PUT /v1/admin/projects/{id}/options`; `Id` is null for a new option.
public sealed record ProjectOptionInput(Guid? Id, string LabelAr, string? LabelEn, Money Amount, int Sort, bool IsActive);
