namespace VolturaAir.Host.Features.Presentations;

internal static class PresentationReportDemoData
{
    public static void AddTo(InMemoryPresentationReportStore store)
    {
        var now = DateTimeOffset.Now;
        Add(store, "demo-powerpoint", "powerpoint", "Joakim's phone", now.AddHours(-2), 3_422, [420, 185], 28);
        Add(store, "demo-google-slides", "google-slides", "Meeting room tablet", now.AddDays(-1).AddHours(-3), 2_106, [305], 17);
        Add(store, "demo-pdf", "pdf", "Joakim's phone", now.AddDays(-4).AddHours(-1), 4_815, [610, 240, 130], 36, "Presentation (1)");
    }

    private static void Add(
        InMemoryPresentationReportStore store,
        string id,
        string target,
        string deviceName,
        DateTimeOffset startedAt,
        double presentationSeconds,
        IReadOnlyList<double> breakSeconds,
        int slideCount,
        string presentationName = PresentationReportNames.DefaultName)
    {
        var sessionDuration = presentationSeconds / (breakSeconds.Count + 1);
        var breaks = breakSeconds.Select((duration, index) =>
        {
            var checkpoint = sessionDuration * (index + 1);
            var breakStart = startedAt.AddSeconds(checkpoint + breakSeconds.Take(index).Sum());
            var slideMinimum = index * slideCount / (breakSeconds.Count + 1) + 1;
            var slideMaximum = (index + 1) * slideCount / (breakSeconds.Count + 1);
            return new PresentationReportBreak(
                index + 1,
                checkpoint,
                duration,
                breakStart,
                breakStart.AddSeconds(duration),
                slideMinimum,
                slideMaximum,
                slideMaximum,
                slideMaximum);
        }).ToList();
        var slides = Enumerable.Range(1, slideCount)
            .Select(number => new PresentationReportSlide(number, presentationSeconds / slideCount))
            .ToList();
        var endedAt = startedAt.AddSeconds(presentationSeconds + breakSeconds.Sum());
        var localStart = startedAt.ToLocalTime();
        store.Add(new PresentationReport(
            id,
            $"operation-{id}",
            presentationName,
            target,
            $"isolated-{deviceName.GetHashCode(StringComparison.Ordinal):x8}",
            deviceName,
            startedAt,
            endedAt,
            (int)localStart.Offset.TotalMinutes,
            3_600,
            presentationSeconds,
            false,
            breaks,
            slides,
            PresentationFilePath: null,
            PresentationUrl: target == "google-slides" ? "https://docs.google.com/presentation/d/example" : null));
    }
}
