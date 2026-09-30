namespace Ta3lom.Store.SharedKernel;

/// Value for generic plumbing that needs a result type but returns nothing.
public readonly record struct Unit
{
    public static Unit Value { get; }
}
