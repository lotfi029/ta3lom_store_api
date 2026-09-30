using Ta3lom.Store.Domain.Common;

namespace Ta3lom.Store.Domain.Tests.Common;

public sealed class AggregateRootTests
{
    [Fact]
    public void Raised_events_are_kept_in_order_until_cleared()
    {
        var aggregate = new SampleAggregate(Guid.CreateVersion7());

        aggregate.DoSomething("a");
        aggregate.DoSomething("b");

        aggregate.DomainEvents.Cast<SampleHappened>().Select(e => e.Value).ShouldBe(["a", "b"]);

        aggregate.ClearDomainEvents();
        aggregate.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Empty_id_is_rejected() =>
        Should.Throw<DomainException>(() => new SampleAggregate(Guid.Empty)).Code.ShouldBe("guard.empty");

    [Fact]
    public void Entities_are_equal_by_type_and_id()
    {
        var id = Guid.CreateVersion7();

        new SampleAggregate(id).ShouldBe(new SampleAggregate(id));
        new SampleAggregate(id).ShouldNotBe(new SampleAggregate(Guid.CreateVersion7()));
        new SampleAggregate(id).GetHashCode().ShouldBe(new SampleAggregate(id).GetHashCode());
    }

    private sealed record SampleHappened(string Value) : IDomainEvent;

    private sealed class SampleAggregate(Guid id) : AggregateRoot(id)
    {
        public void DoSomething(string value) => Raise(new SampleHappened(value));
    }
}
