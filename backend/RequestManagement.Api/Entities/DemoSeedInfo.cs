namespace RequestManagement.Api.Entities;

/// <summary>
/// Marks a database as a generated demo dataset and records how it was generated.
/// Its presence is what makes <c>seed</c> idempotent and what allows <c>reset-and-seed</c> to run.
/// </summary>
public sealed class DemoSeedInfo
{
    public int Id { get; set; }

    public required string SeedVersion { get; set; }

    public int RandomSeed { get; set; }

    public DateTimeOffset TimeAnchorUtc { get; set; }

    public int RequestCount { get; set; }

    public int HistoryCount { get; set; }

    public DateTimeOffset SeededAt { get; set; }
}
