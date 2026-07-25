namespace VolturaAir.Host.Features.Presentations;

internal sealed class PresentationsPageController(
    IPresentationReportStore store,
    WebHostService webHost,
    Action<PresentationReport?> detailChanged)
{
    public PresentationsPageView CreateView()
    {
        var view = new PresentationsPageView(store, webHost);
        view.DetailChanged += detailChanged;
        return view;
    }
}
