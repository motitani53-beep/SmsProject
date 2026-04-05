namespace WebApplication1.Services;

/// <summary>
/// Singleton in-memory store for current SMSC status and reason.
/// Used to avoid redundant DB writes when status has not changed.
/// </summary>
public sealed class SmscStatusStore
{
    private readonly object _lock = new();
    private string _currentStatus = string.Empty;
    private string _lastReason = string.Empty;

    /// <summary>Current SMSC status (e.g. "OK", "Down").</summary>
    public string CurrentStatus
    {
        get { lock (_lock) { return _currentStatus; } }
    }

    /// <summary>Last reason associated with the status (e.g. error message).</summary>
    public string LastReason
    {
        get { lock (_lock) { return _lastReason; } }
    }

    /// <summary>
    /// Loads initial state (e.g. from database on startup).
    /// </summary>
    public void Initialize(string status, string reason)
    {
        lock (_lock)
        {
            _currentStatus = status ?? string.Empty;
            _lastReason = reason ?? string.Empty;
        }
    }

    /// <summary>
    /// Returns true only if the status has changed. When true, updates internal state to the new status and reason.
    /// Use this to decide whether to persist to the database (only when true).
    /// </summary>
    public bool ShouldUpdateDatabase(string newStatus, string newReason)
    {
        lock (_lock)
        {
            var status = newStatus ?? string.Empty;
            var reason = newReason ?? string.Empty;
            if (string.Equals(_currentStatus, status, StringComparison.OrdinalIgnoreCase))
                return false;
            _currentStatus = status;
            _lastReason = reason;
            return true;
        }
    }
}
