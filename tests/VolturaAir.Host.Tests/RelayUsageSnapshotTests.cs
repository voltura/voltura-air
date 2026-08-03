using System.Text.Json;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class RelayUsageSnapshotTests
{
    [Fact]
    public void ReadsUsageAndProviderLimitsAsOneImmutableSnapshot()
    {
        using var document = JsonDocument.Parse("""
            {
              "usageBytes": 125000000000,
              "checkedAt": "2026-08-03T20:00:00Z",
              "usageWarningBytes": 750000000000,
              "usageCutoffBytes": 850000000000
            }
            """);

        var usage = Assert.IsType<RelayUsageSnapshot>(RelayHostConnection.ParseUsageSnapshot(document.RootElement));

        Assert.Equal(125_000_000_000, usage.Bytes);
        Assert.Equal(750_000_000_000, usage.WarningBytes);
        Assert.Equal(850_000_000_000, usage.CutoffBytes);
    }

    [Fact]
    public void KeepsUsageButRejectsInvalidOrMissingProviderLimits()
    {
        using var document = JsonDocument.Parse("""
            {
              "usageBytes": 125000000000,
              "checkedAt": "2026-08-03T20:00:00Z",
              "usageWarningBytes": 900000000000,
              "usageCutoffBytes": 850000000000
            }
            """);

        var usage = Assert.IsType<RelayUsageSnapshot>(RelayHostConnection.ParseUsageSnapshot(document.RootElement));

        Assert.Null(usage.WarningBytes);
        Assert.Null(usage.CutoffBytes);
    }

    [Fact]
    public void AcceptsAnUnlimitedProviderSnapshotWithoutInventingLimits()
    {
        using var document = JsonDocument.Parse("""
            {
              "usageBytes": 0,
              "checkedAt": "2026-08-03T20:00:00Z",
              "usageWarningBytes": null,
              "usageCutoffBytes": null
            }
            """);

        var usage = Assert.IsType<RelayUsageSnapshot>(RelayHostConnection.ParseUsageSnapshot(document.RootElement));

        Assert.Null(usage.WarningBytes);
        Assert.Null(usage.CutoffBytes);
    }
}
