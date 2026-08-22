using BoostFPS.Core.Models;

namespace BoostFPS.Core.Services;

/// <summary>Append-only action log, mirrored to disk after every entry so a crash keeps the history.</summary>
public sealed class ChangelogService
{
    private readonly Lock _lock = new();
    private readonly List<ChangelogEntry> _entries;

    public ChangelogService()
    {
        AppPaths.EnsureCreated();
        _entries = Json.Read<List<ChangelogEntry>>(AppPaths.ChangelogFile) ?? [];
    }

    public IReadOnlyList<ChangelogEntry> Entries
    {
        get { lock (_lock) return _entries.ToList(); }
    }

    public void Add(string action, string description)
    {
        lock (_lock)
        {
            _entries.Add(new ChangelogEntry { Action = action, Description = description });

            // Keep the file bounded; the UI never needs more than the recent history.
            if (_entries.Count > 5000) _entries.RemoveRange(0, _entries.Count - 5000);

            Json.Write(AppPaths.ChangelogFile, _entries);
        }
    }
}
