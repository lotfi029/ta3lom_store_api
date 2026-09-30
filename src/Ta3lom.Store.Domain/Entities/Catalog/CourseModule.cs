namespace Ta3lom.Store.Domain.Entities.Catalog;

/// Table `course_module` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
public sealed class CourseModule : Entity
{
    public CourseModule(Guid productId, int sort, string titleAr) : base(Guid.CreateVersion7())
    {
        ProductId = Guard.NotEmpty(productId);
        Sort = sort;
        TitleAr = Guard.NotNull(titleAr);
        DurationSeconds = 0;
    }

    private CourseModule()
    {
    }

    public Guid ProductId { get; private set; }

    public int Sort { get; private set; }

    public string TitleAr { get; private set; } = null!;

    public string? TitleEn { get; private set; }

    public int DurationSeconds { get; private set; }

    public static CourseModule Create(Guid productId, int sort, string titleAr, string? titleEn, int durationSeconds)
    {
        var module = new CourseModule(productId, sort, titleAr);
        module.TitleEn = titleEn;
        module.DurationSeconds = Math.Max(0, durationSeconds);
        return module;
    }
}
