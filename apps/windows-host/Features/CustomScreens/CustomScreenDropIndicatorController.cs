using System.Windows;
using System.Windows.Documents;
using Brush = System.Windows.Media.Brush;

namespace VolturaAir.Host.Features.CustomScreens;

internal sealed class CustomScreenDropIndicatorController(
    Func<string, Brush> brush)
{
    private AdornerLayer? _layer;
    private CustomScreenDropIndicatorAdorner? _indicator;
    private FrameworkElement? _target;
    private CustomScreenDropEdge _edge;

    public void Show(FrameworkElement target, CustomScreenDropEdge edge)
    {
        if (ReferenceEquals(_target, target) &&
            _edge == edge &&
            _indicator is not null)
        {
            return;
        }

        Clear();
        _layer = AdornerLayer.GetAdornerLayer(target);
        if (_layer is null)
        {
            return;
        }

        _target = target;
        _edge = edge;
        _indicator = new CustomScreenDropIndicatorAdorner(
            target,
            brush("AccentBrush"),
            edge);
        _layer.Add(_indicator);
    }

    public void Clear()
    {
        if (_layer is not null && _indicator is not null)
        {
            _layer.Remove(_indicator);
        }

        _layer = null;
        _indicator = null;
        _target = null;
    }
}
