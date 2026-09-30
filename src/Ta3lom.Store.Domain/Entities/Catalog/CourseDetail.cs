namespace Ta3lom.Store.Domain.Entities.Catalog;

/// Table `course_detail` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
public sealed class CourseDetail
{
    public CourseDetail(Guid productId, Money price, string instructorAr)
    {
        ProductId = Guard.NotEmpty(productId);
        Price = price;
        Lessons = 0;
        DurationMinutes = 0;
        InstructorAr = Guard.NotNull(instructorAr);
        LearnerCount = 0;
    }

    private CourseDetail()
    {
    }

    public Guid ProductId { get; private set; }

    public Money Price { get; private set; }

    public Money? CompareAtPrice { get; private set; }

    public int Lessons { get; private set; }

    public int DurationMinutes { get; private set; }

    public string? LevelAr { get; private set; }

    public string? LevelEn { get; private set; }

    public string InstructorAr { get; private set; } = null!;

    public string? InstructorEn { get; private set; }

    public int? AccessDays { get; private set; }

    public string? LmsCourseRef { get; private set; }

    public int LearnerCount { get; private set; }

    /// RUL-04: one price; a compare-at price is shown only when it is higher. RUL-42: access_days NULL = lifetime.
    public void Update(Money price, Money? compareAtPrice, int lessons, int durationMinutes, string? levelAr, string? levelEn,
        string instructorAr, string? instructorEn, int? accessDays, string? lmsCourseRef, int learnerCount)
    {
        Guard.Against(compareAtPrice is { } c && c <= price, "course.compare_at_not_higher");
        Price = price;
        CompareAtPrice = compareAtPrice;
        Lessons = Math.Max(0, lessons);
        DurationMinutes = Math.Max(0, durationMinutes);
        LevelAr = levelAr;
        LevelEn = levelEn;
        InstructorAr = Guard.NotEmpty(instructorAr);
        InstructorEn = instructorEn;
        AccessDays = accessDays;
        LmsCourseRef = lmsCourseRef;
        LearnerCount = Math.Max(0, learnerCount);
    }
}
