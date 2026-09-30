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
}
