namespace Ta3lom.Store.Domain.Entities.Donations;

/// Table `order_line`: the snapshot of one gift line at checkout (titles and option labels as they were, D-07).
public sealed class OrderLine : Entity
{
    internal OrderLine(Guid orderId, short lineNo, OrderLineDraft d) : base(Guid.CreateVersion7())
    {
        Guard.Against(d.TargetKind == TargetKind.Project
            ? d.ProjectId is null || d.CategoryId is not null
            : d.CategoryId is null || d.ProjectId is not null || d.OptionId is not null, "order.line_target_invalid");
        Guard.Against(!d.UnitAmount.IsPositive, "order.line_amount_invalid");
        OrderId = orderId;
        LineNo = lineNo;
        TargetKind = d.TargetKind;
        ProjectId = d.ProjectId;
        CategoryId = d.CategoryId;
        OptionId = d.OptionId;
        TitleAr = Guard.NotEmpty(d.TitleAr);
        TitleEn = d.TitleEn;
        OptionLabelAr = d.OptionLabelAr;
        OptionLabelEn = d.OptionLabelEn;
        Quantity = Guard.InRange(d.Quantity, 1, CartLimits.MaxQuantity);
        UnitAmount = d.UnitAmount;
        Amount = d.UnitAmount * d.Quantity;
    }

    private OrderLine()
    {
    }

    public Guid OrderId { get; private set; }

    public short LineNo { get; private set; }

    public TargetKind TargetKind { get; private set; }

    public Guid? ProjectId { get; private set; }

    public Guid? CategoryId { get; private set; }

    public Guid? OptionId { get; private set; }

    public string TitleAr { get; private set; } = null!;

    public string? TitleEn { get; private set; }

    public string? OptionLabelAr { get; private set; }

    public string? OptionLabelEn { get; private set; }

    public int Quantity { get; private set; }

    public Money UnitAmount { get; private set; }

    public Money Amount { get; private set; }
}
