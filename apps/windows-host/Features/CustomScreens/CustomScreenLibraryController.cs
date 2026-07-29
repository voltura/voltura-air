using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using CheckBox = System.Windows.Controls.CheckBox;
using Cursors = System.Windows.Input.Cursors;
using DataObject = System.Windows.DataObject;
using DragDrop = System.Windows.DragDrop;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using IDataObject = System.Windows.IDataObject;
using MessageBox = System.Windows.MessageBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Orientation = System.Windows.Controls.Orientation;
using Point = System.Windows.Point;

namespace VolturaAir.Host.Features.CustomScreens;

internal sealed class CustomScreenLibraryController(
    Window owner,
    StackPanel list,
    CustomScreenService service,
    PairingManager pairingManager,
    Func<string, Brush> brush,
    Action<CustomScreenDefinition> openEditor,
    Func<string, UrlOpenExecutionResult> openPreview,
    CustomScreenEditorActivityLog activityLog,
    Action<string> showToast)
{
    private const string ScreenDragFormat = "VolturaAir.CustomScreenOrder";
    private Point? _dragStart;
    private string? _dragScreenId;
    private Border? _dragSourceCard;
    private string[] _originalOrder = [];
    private bool _orderChanged;
    private bool _dropCommitted;

    public void Refresh()
    {
        list.Children.Clear();
        if (service.LoadError is { } loadError)
        {
            list.Children.Add(CreateMessage(loadError, danger: true));
            return;
        }

        var screens = service.GetAll();
        if (screens.Count == 0)
        {
            list.Children.Add(CreateMessage(
                "No custom screens yet. Create one to turn a paired device into a purpose-built control surface.",
                danger: false));
            return;
        }

        foreach (var screen in screens)
        {
            list.Children.Add(CreateLibraryCard(screen));
        }
    }

    private Border CreateLibraryCard(CustomScreenDefinition screen)
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var details = new StackPanel();
        details.Children.Add(new TextBlock
        {
            Text = screen.Name,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = brush("TextBrush")
        });
        details.Children.Add(new TextBlock
        {
            Text = $"{screen.Sections.Count} panels · {screen.Sections.Sum(section => section.Buttons.Count)} buttons",
            Margin = new Thickness(0, 4, 0, 8),
            Foreground = brush("MutedTextBrush")
        });
        details.Children.Add(CreateAssignmentPanel(screen));
        root.Children.Add(details);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        actions.Children.Add(ActionButton("Edit", () => openEditor(screen)));
        actions.Children.Add(ActionButton("Preview", () => Preview(screen.Id)));
        actions.Children.Add(ActionButton("Duplicate", () => Duplicate(screen.Id)));
        var deleteButton = ActionButton(
            "Delete",
            () => Delete(screen),
            automationName: $"Delete {screen.Name}");
        deleteButton.SetResourceReference(FrameworkElement.StyleProperty, "DangerButtonStyle");
        actions.Children.Add(deleteButton);
        Grid.SetColumn(actions, 1);
        root.Children.Add(actions);

        var dragHandle = new Button
        {
            Content = "⠿",
            Width = 38,
            Height = 38,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.SizeAll,
            ToolTip = "Drag to reorder"
        };
        AutomationProperties.SetName(dragHandle, $"Drag to reorder {screen.Name}");
        Grid.SetColumn(dragHandle, 3);
        root.Children.Add(dragHandle);

        var card = new Border
        {
            Background = brush("SurfaceBrush"),
            BorderBrush = brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Child = root,
            Tag = screen.Id
        };
        AttachOrderDrag(card, dragHandle, screen.Id);
        return card;
    }

    private WrapPanel CreateAssignmentPanel(CustomScreenDefinition screen)
    {
        var panel = new WrapPanel();
        foreach (var device in pairingManager.GetDevices())
        {
            var checkBox = new CheckBox
            {
                Content = device.DeviceName,
                IsChecked = screen.AssignedClientIds.Contains(device.ClientId, StringComparer.Ordinal),
                Margin = new Thickness(0, 0, 14, 4),
                Tag = device.ClientId
            };
            AutomationProperties.SetName(
                checkBox,
                $"Make {screen.Name} available to {device.DeviceName}");
            checkBox.Click += (_, _) =>
            {
                var selected = panel.Children.OfType<CheckBox>()
                    .Where(item => item.IsChecked == true)
                    .Select(item => (string)item.Tag)
                    .ToArray();
                if (!service.TryAssign(screen.Id, selected, out var error))
                {
                    activityLog.Write("assign", succeeded: false);
                    MessageBox.Show(
                        owner,
                        error,
                        "Custom screens",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                else
                {
                    activityLog.Write("assign", succeeded: true);
                    showToast("Screen assignments updated");
                }
                Refresh();
            };
            panel.Children.Add(checkBox);
        }

        if (panel.Children.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Pair a device before assigning this screen.",
                Foreground = brush("MutedTextBrush")
            });
        }

        return panel;
    }

    private void Duplicate(string screenId)
    {
        if (!service.TryDuplicate(screenId, out _, out var error))
        {
            activityLog.Write("duplicate", succeeded: false);
            MessageBox.Show(
                owner,
                error,
                "Custom screens",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        else
        {
            activityLog.Write("duplicate", succeeded: true);
            showToast("Custom screen duplicated");
        }
        Refresh();
    }

    private void AttachOrderDrag(Border card, Button dragHandle, string screenId)
    {
        card.AllowDrop = true;
        dragHandle.PreviewMouseLeftButtonDown += (_, eventArgs) =>
        {
            _dragStart = eventArgs.GetPosition(list);
            _dragScreenId = screenId;
            _dragSourceCard = card;
        };
        dragHandle.PreviewMouseMove += (_, eventArgs) =>
            BeginOrderDrag(dragHandle, eventArgs);
        card.DragOver += (_, eventArgs) =>
            ShowOrderDropTarget(card, screenId, eventArgs);
        card.Drop += (_, eventArgs) => DropScreenOrder(eventArgs);
    }

    private void BeginOrderDrag(Button dragHandle, MouseEventArgs eventArgs)
    {
        if (_dragStart is not { } start ||
            _dragScreenId is null ||
            _dragSourceCard is null ||
            eventArgs.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var position = eventArgs.GetPosition(list);
        if (Math.Abs(position.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _orderChanged = false;
        _dropCommitted = false;
        _originalOrder = CurrentVisualOrder();
        var sourceCard = _dragSourceCard;
        var previousOpacity = sourceCard.Opacity;
        sourceCard.Opacity = 0.5;

        var data = new DataObject();
        data.SetData(ScreenDragFormat, _dragScreenId);
        try
        {
            DragDrop.DoDragDrop(dragHandle, data, DragDropEffects.Move);
        }
        finally
        {
            sourceCard.Opacity = previousOpacity;
            if (!_dropCommitted)
            {
                RestoreVisualOrder();
            }
            _dragStart = null;
            _dragScreenId = null;
            _dragSourceCard = null;
            _originalOrder = [];
        }

        if (_dropCommitted && _orderChanged)
        {
            showToast("Custom-screen order updated");
            Refresh();
        }
    }

    private void ShowOrderDropTarget(
        Border targetCard,
        string targetScreenId,
        DragEventArgs eventArgs)
    {
        if (!TryGetDraggedScreen(eventArgs.Data, out var screenId))
        {
            eventArgs.Effects = DragDropEffects.None;
            eventArgs.Handled = true;
            return;
        }

        if (string.Equals(screenId, targetScreenId, StringComparison.Ordinal))
        {
            eventArgs.Effects = DragDropEffects.Move;
            eventArgs.Handled = true;
            return;
        }

        var insertAfter = eventArgs.GetPosition(targetCard).Y >= targetCard.ActualHeight / 2;
        MoveVisualCard(targetCard, insertAfter);
        eventArgs.Effects = DragDropEffects.Move;
        eventArgs.Handled = true;
    }

    private void DropScreenOrder(DragEventArgs eventArgs)
    {
        if (!TryGetDraggedScreen(eventArgs.Data, out var screenId))
        {
            eventArgs.Effects = DragDropEffects.None;
            eventArgs.Handled = true;
            return;
        }

        var destination = ResolveOrderPersistence(CurrentVisualOrder(), screenId);
        if (destination is null)
        {
            _dropCommitted = true;
            eventArgs.Effects = DragDropEffects.Move;
        }
        else if (service.TryReorder(
            screenId,
            destination.Value.TargetScreenId,
            destination.Value.InsertAfter,
            out var error))
        {
            _dropCommitted = true;
            activityLog.Write("reorder", succeeded: true);
            eventArgs.Effects = DragDropEffects.Move;
        }
        else
        {
            activityLog.Write("reorder", succeeded: false);
            MessageBox.Show(
                owner,
                error,
                "Custom screens",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            eventArgs.Effects = DragDropEffects.None;
        }
        eventArgs.Handled = true;
    }

    internal static (string TargetScreenId, bool InsertAfter)? ResolveOrderPersistence(
        IReadOnlyList<string> visualOrder,
        string draggedScreenId)
    {
        var sourceIndex = -1;
        for (var index = 0; index < visualOrder.Count; index++)
        {
            if (string.Equals(
                visualOrder[index],
                draggedScreenId,
                StringComparison.Ordinal))
            {
                sourceIndex = index;
                break;
            }
        }
        if (sourceIndex < 0 || visualOrder.Count < 2)
        {
            return null;
        }

        return sourceIndex > 0
            ? (visualOrder[sourceIndex - 1], true)
            : (visualOrder[1], false);
    }

    private static bool TryGetDraggedScreen(IDataObject data, out string screenId)
    {
        screenId = data.GetDataPresent(ScreenDragFormat)
            ? data.GetData(ScreenDragFormat) as string ?? string.Empty
            : string.Empty;
        return screenId.Length > 0;
    }

    private void MoveVisualCard(Border targetCard, bool insertAfter)
    {
        if (_dragSourceCard is null || ReferenceEquals(_dragSourceCard, targetCard))
        {
            return;
        }

        list.Children.Remove(_dragSourceCard);
        var targetIndex = list.Children.IndexOf(targetCard);
        list.Children.Insert(targetIndex + (insertAfter ? 1 : 0), _dragSourceCard);
        _orderChanged = !CurrentVisualOrder().SequenceEqual(
            _originalOrder,
            StringComparer.Ordinal);
    }

    private string[] CurrentVisualOrder() =>
        [.. list.Children
            .OfType<Border>()
            .Select(card => card.Tag as string)
            .Where(screenId => screenId is not null)
            .Cast<string>()];

    private void RestoreVisualOrder()
    {
        var cards = list.Children
            .OfType<Border>()
            .Where(card => card.Tag is string)
            .ToDictionary(card => (string)card.Tag, StringComparer.Ordinal);
        list.Children.Clear();
        foreach (var screenId in _originalOrder)
        {
            if (cards.TryGetValue(screenId, out var card))
            {
                list.Children.Add(card);
            }
        }
    }

    private void Delete(CustomScreenDefinition screen)
    {
        if (CustomScreenEditorSettings.ConfirmDeletes() &&
            !ThemedConfirmationDialog.Show(
                owner,
                "Delete custom screen",
                $"Delete “{screen.Name}”? This does not remove any approved applications.",
                "Delete",
                "Cancel",
                ConfirmationTone.Warning))
        {
            return;
        }
        if (service.TryDelete(screen.Id, out var error))
        {
            activityLog.Write("delete", succeeded: true);
            showToast("Custom screen deleted");
        }
        else
        {
            activityLog.Write("delete", succeeded: false);
            MessageBox.Show(
                owner,
                error,
                "Custom screens",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        Refresh();
    }

    private void Preview(string screenId)
    {
        var result = openPreview(screenId);
        activityLog.Write(
            "preview",
            result.Succeeded,
            result.Succeeded ? null : result.Code);
        if (result.Succeeded)
        {
            showToast("Preview window opened");
            return;
        }

        MessageBox.Show(
            owner,
            result.Message,
            "Custom screens",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private Border CreateMessage(string text, bool danger) => new()
    {
        Background = brush("SurfaceBrush"),
        BorderBrush = brush(danger ? "DangerBrush" : "BorderBrush"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(16),
        Child = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = brush("TextBrush")
        }
    };

    private static Button ActionButton(
        string label,
        Action action,
        bool enabled = true,
        string? automationName = null)
    {
        var button = new Button
        {
            Content = label,
            IsEnabled = enabled,
            Width = 92,
            MinHeight = 38,
            Margin = new Thickness(8, 0, 0, 0)
        };
        AutomationProperties.SetName(button, automationName ?? label);
        button.Click += (_, _) => action();
        return button;
    }
}
