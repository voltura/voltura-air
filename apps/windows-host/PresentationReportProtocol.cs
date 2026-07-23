using System.Text.Json;

namespace VolturaAir.Host;

internal static class PresentationReportProtocol
{
    public const int MaxBreakCount = 100;
    public const int MaxSlideCount = 1000;
    internal const double MaxDurationSeconds = 7 * 24 * 60 * 60;
    private const int MaxIdentifierLength = 64;
    private const int MinUtcOffsetMinutes = -14 * 60;
    private const int MaxUtcOffsetMinutes = 14 * 60;

    public static bool TryParse(JsonElement root, out PresentationReportSaveRequest request)
    {
        request = EmptyRequest();
        if (!TryIdentifier(root, "operationId", out var operationId) ||
            !TryIdentifier(root, "reportId", out var reportId) ||
            !TryTarget(root, out var target) ||
            !TryDate(root, "startedAt", out var startedAt) ||
            !TryDate(root, "endedAt", out var endedAt) ||
            endedAt < startedAt ||
            endedAt - startedAt > TimeSpan.FromSeconds(MaxDurationSeconds) ||
            !TryInteger(root, "utcOffsetMinutes", MinUtcOffsetMinutes, MaxUtcOffsetMinutes, out var utcOffsetMinutes) ||
            !TryDuration(root, "plannedDurationSeconds", out var plannedDurationSeconds) ||
            !TryDuration(root, "presentationDurationSeconds", out var presentationDurationSeconds) ||
            !TryBoolean(root, "endedDuringBreak", out var endedDuringBreak) ||
            !TryBreaks(root, startedAt, endedAt, presentationDurationSeconds, out var breaks) ||
            !TrySlides(root, out var slides) ||
            !IsValidEnding(endedDuringBreak, breaks, endedAt, presentationDurationSeconds))
        {
            return false;
        }

        request = new(
            operationId,
            reportId,
            target,
            startedAt,
            endedAt,
            utcOffsetMinutes,
            plannedDurationSeconds,
            presentationDurationSeconds,
            endedDuringBreak,
            breaks,
            slides);
        return true;
    }

    private static bool TryBreaks(
        JsonElement root,
        DateTimeOffset reportStartedAt,
        DateTimeOffset reportEndedAt,
        double presentationDurationSeconds,
        out IReadOnlyList<PresentationReportBreak> breaks)
    {
        breaks = [];
        if (!root.TryGetProperty("breaks", out var value) ||
            value.ValueKind != JsonValueKind.Array ||
            value.GetArrayLength() > MaxBreakCount)
        {
            return false;
        }

        var parsed = new List<PresentationReportBreak>(value.GetArrayLength());
        DateTimeOffset? previousEndedAt = null;
        double previousPresentationElapsed = 0;
        var expectedNumber = 1;
        foreach (var entry in value.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object ||
                !HasExactBreakProperties(entry) ||
                !TryInteger(entry, "breakNumber", expectedNumber, expectedNumber, out var breakNumber) ||
                !TryDuration(entry, "presentationElapsedSeconds", out var presentationElapsedSeconds) ||
                presentationElapsedSeconds < previousPresentationElapsed ||
                presentationElapsedSeconds > presentationDurationSeconds ||
                !TryDuration(entry, "breakDurationSeconds", out var breakDurationSeconds) ||
                !TryDate(entry, "startedAt", out var startedAt) ||
                !TryDate(entry, "endedAt", out var endedAt) ||
                startedAt < reportStartedAt ||
                endedAt < startedAt ||
                endedAt > reportEndedAt ||
                previousEndedAt is { } previous && startedAt < previous ||
                !TryOptionalPositiveInteger(entry, "sessionSlideMinimum", out var sessionSlideMinimum) ||
                !TryOptionalPositiveInteger(entry, "sessionSlideMaximum", out var sessionSlideMaximum) ||
                !TryOptionalPositiveInteger(entry, "slideNumberAtStart", out var slideNumberAtStart) ||
                !TryOptionalPositiveInteger(entry, "slideNumberAtEnd", out var slideNumberAtEnd) ||
                !IsValidSlideRange(sessionSlideMinimum, sessionSlideMaximum))
            {
                return false;
            }

            parsed.Add(new(
                breakNumber,
                presentationElapsedSeconds,
                breakDurationSeconds,
                startedAt,
                endedAt,
                sessionSlideMinimum,
                sessionSlideMaximum,
                slideNumberAtStart,
                slideNumberAtEnd));
            previousEndedAt = endedAt;
            previousPresentationElapsed = presentationElapsedSeconds;
            expectedNumber++;
        }

        breaks = parsed;
        return true;
    }

    private static bool TrySlides(JsonElement root, out IReadOnlyList<PresentationReportSlide> slides)
    {
        slides = [];
        if (!root.TryGetProperty("slides", out var value) ||
            value.ValueKind != JsonValueKind.Array ||
            value.GetArrayLength() > MaxSlideCount)
        {
            return false;
        }

        var parsed = new List<PresentationReportSlide>(value.GetArrayLength());
        var seen = new HashSet<int>();
        foreach (var entry in value.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object ||
                !HasExactSlideProperties(entry) ||
                !TryInteger(entry, "slideNumber", 1, MaxSlideCount, out var slideNumber) ||
                !seen.Add(slideNumber) ||
                !TryOptionalDuration(entry, "durationSeconds", out var durationSeconds))
            {
                return false;
            }

            parsed.Add(new(slideNumber, durationSeconds));
        }

        slides = [.. parsed.OrderBy(entry => entry.SlideNumber)];
        return true;
    }

    private static bool TryTarget(JsonElement root, out string target)
    {
        target = string.Empty;
        if (!root.TryGetProperty("target", out var value) || value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        target = value.GetString() ?? string.Empty;
        return PresentationCommands.IsTarget(target);
    }

    private static bool TryIdentifier(JsonElement root, string propertyName, out string identifier)
    {
        identifier = string.Empty;
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        identifier = value.GetString() ?? string.Empty;
        return identifier is { Length: > 0 and <= MaxIdentifierLength } &&
            identifier.All(character => char.IsAsciiLetterOrDigit(character) || character is '-');
    }

    private static bool TryDate(JsonElement root, string propertyName, out DateTimeOffset date)
    {
        date = default;
        return root.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.String &&
            value.TryGetDateTimeOffset(out date);
    }

    private static bool TryDuration(JsonElement root, string propertyName, out double duration)
    {
        duration = 0;
        return root.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetDouble(out duration) &&
            double.IsFinite(duration) &&
            duration >= 0 &&
            duration <= MaxDurationSeconds;
    }

    private static bool TryOptionalDuration(JsonElement root, string propertyName, out double? duration)
    {
        duration = null;
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return true;
        }

        if (value.ValueKind != JsonValueKind.Number ||
            !value.TryGetDouble(out var parsed) ||
            !double.IsFinite(parsed) ||
            parsed < 0 ||
            parsed > MaxDurationSeconds)
        {
            return false;
        }

        duration = parsed;
        return true;
    }

    private static bool TryInteger(JsonElement root, string propertyName, int minimum, int maximum, out int result)
    {
        result = 0;
        return root.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out result) &&
            result >= minimum &&
            result <= maximum;
    }

    private static bool TryOptionalPositiveInteger(JsonElement root, string propertyName, out int? result)
    {
        result = null;
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return true;
        }

        if (value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out var parsed) ||
            parsed is < 1 or > MaxSlideCount)
        {
            return false;
        }

        result = parsed;
        return true;
    }

    private static bool IsValidSlideRange(int? minimum, int? maximum) =>
        minimum is null && maximum is null ||
        minimum is not null && maximum is not null && minimum <= maximum;

    private static bool TryBoolean(JsonElement root, string propertyName, out bool result)
    {
        result = false;
        if (!root.TryGetProperty(propertyName, out var value) ||
            value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            return false;
        }

        result = value.GetBoolean();
        return true;
    }

    private static bool IsValidEnding(
        bool endedDuringBreak,
        IReadOnlyList<PresentationReportBreak> breaks,
        DateTimeOffset endedAt,
        double presentationDurationSeconds) =>
        !endedDuringBreak ||
        breaks.Count > 0 &&
        breaks[^1].EndedAt == endedAt &&
        breaks[^1].PresentationElapsedSeconds == presentationDurationSeconds;

    private static bool HasExactBreakProperties(JsonElement entry) =>
        HasExactProperties(entry,
        [
            "breakNumber",
            "presentationElapsedSeconds",
            "breakDurationSeconds",
            "startedAt",
            "endedAt",
            "sessionSlideMinimum",
            "sessionSlideMaximum",
            "slideNumberAtStart",
            "slideNumberAtEnd"
        ]);

    private static bool HasExactSlideProperties(JsonElement entry) =>
        HasExactProperties(entry, ["slideNumber", "durationSeconds"]);

    private static bool HasExactProperties(JsonElement entry, IReadOnlyList<string> allowed)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in entry.EnumerateObject())
        {
            if (!allowed.Contains(property.Name) || !seen.Add(property.Name))
            {
                return false;
            }
        }

        return true;
    }

    private static PresentationReportSaveRequest EmptyRequest() =>
        new(string.Empty, string.Empty, string.Empty, default, default, 0, 0, 0, false, [], []);
}
