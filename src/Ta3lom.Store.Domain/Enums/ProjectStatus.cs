namespace Ta3lom.Store.Domain.Enums;

/// D-07 draft → published → archived (and back to published). Stored as snake_case text with a CHECK list.
public enum ProjectStatus
{
    Draft,
    Published,
    Archived,
}
