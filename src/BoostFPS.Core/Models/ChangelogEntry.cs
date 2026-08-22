namespace BoostFPS.Core.Models;

public sealed class ChangelogEntry
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public required string Action { get; init; }
    public required string Description { get; init; }
}
