namespace GoldTracker.Shared.UI.Models;

public record SyncProgress
{
    public double Percentage { get; init; }
    public string Message { get; init; } = string.Empty;
    public int ProcessedCount { get; init; }
    public int TotalCount { get; init; }

    public SyncProgress(double percentage, string message)
    {
        Percentage = percentage;
        Message = message;
    }

    public static SyncProgress Empty => new(0, string.Empty);
}
