namespace VolturaAir.Host.Features.Presentations;

internal sealed class PresentationsPageController(
    IPresentationReportStore store,
    Action<PresentationReport?> detailChanged)
{
    public PresentationsPageView CreateView()
    {
        var view = new PresentationsPageView(store);
        view.DetailChanged += detailChanged;
        return view;
    }
}
