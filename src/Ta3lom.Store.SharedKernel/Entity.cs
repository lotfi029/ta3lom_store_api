namespace Ta3lom.Store.SharedKernel;

/// Base for every entity. Ids are UUID v7, created by the caller (Guid.CreateVersion7()).
public abstract class Entity
{
    protected Entity(Guid id)
    {
        Guard.NotEmpty(id, nameof(id));
        Id = id;
    }

    /// For the persistence mapper only.
    protected Entity()
    {
    }

    public Guid Id { get; private init; }

    public override bool Equals(object? obj) =>
        obj is Entity other && other.GetType() == GetType() && other.Id == Id && Id != Guid.Empty;

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}
