namespace Ta3lom.Store.Domain.Events;

/// A staff e-mail (`staff_alert` template): integrity check failures, a dead-lettered outbox message, the OTP volume alert.
public sealed record StaffAlert(string Kind, string? Reference, string? Detail) : IDomainEvent;
