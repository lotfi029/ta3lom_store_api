using FluentValidation;
using Ta3lom.Store.Application.DTOs.AdminCatalog;
using Ta3lom.Store.Domain.Entities.Catalog;

namespace Ta3lom.Store.Application.Features.AdminCatalog.Commands.UpsertCourseDetail;

/// `PUT /v1/admin/products/{id}/course` (#58, RUL-04, RUL-42).
public sealed record UpsertCourseDetailCommand(Guid ProductId, CourseDetailInput Input) : ICommand<AdminProductResponse>;

internal sealed class UpsertCourseDetailCommandHandler(
    IProductRepository products,
    IGenericRepository<CourseDetail> courses,
    AdminCatalogReader reader,
    IUnitOfWork uow,
    IClock clock) : ICommandHandler<UpsertCourseDetailCommand, AdminProductResponse>
{
    public async Task<Result<AdminProductResponse>> HandleAsync(UpsertCourseDetailCommand command, CancellationToken ct = default)
    {
        var product = await products.GetByIdAsync(command.ProductId, ct);
        if (product is null)
        {
            return AdminCatalogErrors.ProductNotFound;
        }

        if (product.Type != ProductType.Course)
        {
            return Error.Validation(new Dictionary<string, string[]> { ["type"] = ["The product is not a course."] });
        }

        var i = command.Input;
        var price = AdminInput.Money(i.Price);
        var detail = await courses.GetByIdAsync(product.Id, ct);
        if (detail is null)
        {
            detail = new CourseDetail(product.Id, price, i.InstructorAr);
            courses.Add(detail);
        }

        detail.Update(price, AdminInput.OptionalMoney(i.CompareAtPrice), i.Lessons, i.DurationMinutes, i.LevelAr, i.LevelEn, i.InstructorAr,
            i.InstructorEn, i.AccessDays, i.LmsCourseRef, i.LearnerCount);
        product.Touch(clock.UtcNow);
        await uow.SaveChangesAsync(ct);
        return (await reader.ProductAsync(product.Id, ct))!;
    }
}

internal sealed class UpsertCourseDetailCommandValidator : AbstractValidator<UpsertCourseDetailCommand>
{
    public UpsertCourseDetailCommandValidator()
    {
        RuleFor(c => c.Input.Price).Must(AdminInput.IsMoney).WithMessage("price must be a money string such as \"350.00\".");
        RuleFor(c => c.Input.CompareAtPrice).Must(AdminInput.IsOptionalMoney).WithMessage("compareAtPrice must be a money string.");
        RuleFor(c => c.Input).Must(i => i.CompareAtPrice is null || !AdminInput.IsMoney(i.Price) || !AdminInput.IsMoney(i.CompareAtPrice)
                || AdminInput.Decimal(i.CompareAtPrice) > AdminInput.Decimal(i.Price))
            .WithName("compareAtPrice").WithMessage("compareAtPrice must be higher than price (RUL-04).");
        RuleFor(c => c.Input.InstructorAr).NotEmpty();
        RuleFor(c => c.Input.Lessons).GreaterThanOrEqualTo(0);
        RuleFor(c => c.Input.DurationMinutes).GreaterThanOrEqualTo(0);
        RuleFor(c => c.Input.AccessDays).GreaterThan(0).When(c => c.Input.AccessDays is not null);
    }
}
