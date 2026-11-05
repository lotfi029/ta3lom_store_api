namespace Ta3lom.Store.Application.Interfaces;

/// What the store takes (showcase mode): payments (`Store:Payments:Provider` is not `none`) and donor sign-in
/// (`Store:Features:CustomerSignIn`). Fixed for the life of the process (configuration, read at start-up).
public interface IStoreMode
{
    bool PaymentsEnabled { get; }

    bool CustomerSignIn { get; }

    /// `live` when payments are on, otherwise `showcase`.
    string Mode => PaymentsEnabled ? "live" : "showcase";
}
