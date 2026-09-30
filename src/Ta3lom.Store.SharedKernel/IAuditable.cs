namespace Ta3lom.Store.SharedKernel;

/// Marks an entity whose staff changes are written to `audit_log` by the save interceptor
/// (Architecture-alignment-DMS §3-l). Time always comes from IClock, never from the entity (K-e).
public interface IAuditable;
