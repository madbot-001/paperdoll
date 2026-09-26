using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Paperdoll.App;

/// <summary>
/// Dragging a marking onto another in the same layer of the explorer moves it there. The first
/// marking in a layer is drawn on top.
/// </summary>
public partial class MainWindow
{
    private Node? _dragFrom;
    private Point _dragStart;
    private bool _dragging;
    private TreeViewItem? _dropTarget;

    private void SetUpExplorerDrag()
    {
        // Tunnelling, so the drag is seen before the tree handles the press itself.
        Explorer.AddHandler(PointerPressedEvent, OnExplorerPointerPressed, RoutingStrategies.Tunnel);
        Explorer.AddHandler(PointerMovedEvent, OnExplorerPointerMoved, RoutingStrategies.Tunnel);
        Explorer.AddHandler(PointerReleasedEvent, OnExplorerPointerReleased, RoutingStrategies.Tunnel);
        Explorer.AddHandler(PointerCaptureLostEvent, (_, _) => EndDrag(), RoutingStrategies.Bubble);
    }

    private TreeViewItem? ItemAt(Point point) =>
        (Explorer.InputHitTest(point) as Visual)?.GetSelfAndVisualAncestors().OfType<TreeViewItem>().FirstOrDefault();

    private void OnExplorerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        EndDrag();
        if (!e.GetCurrentPoint(Explorer).Properties.IsLeftButtonPressed)
            return;
        var point = e.GetPosition(Explorer);
        if (ItemAt(point)?.Tag is Node { Kind: NodeKind.Marking } node)
        {
            _dragFrom = node;
            _dragStart = point;
        }
    }

    private void OnExplorerPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragFrom is not { } from || !e.GetCurrentPoint(Explorer).Properties.IsLeftButtonPressed)
            return;
        var point = e.GetPosition(Explorer);
        if (!_dragging && Math.Abs(point.Y - _dragStart.Y) < 5 && Math.Abs(point.X - _dragStart.X) < 5)
            return;
        _dragging = true;

        var target = ItemAt(point);
        if (target?.Tag is Node { Kind: NodeKind.Marking } to && to.Organ == from.Organ && to.Layer == from.Layer)
        {
            ShowDropTarget(to.Index == from.Index ? null : target);
            SetStatus(to.Index == from.Index
                ? "Drop on another marking in this layer to move it there."
                : $"Release to move it to place {to.Index + 1} in {Words(from.Layer!)}. The first marking is drawn on top.");
        }
        else
        {
            ShowDropTarget(null);
            SetStatus("Markings move only within their own layer.");
        }
    }

    private void OnExplorerPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragging && _dragFrom is { } from && _dropTarget?.Tag is Node { Kind: NodeKind.Marking } to
            && to.Organ == from.Organ && to.Layer == from.Layer && to.Index != from.Index)
        {
            e.Handled = true;
            EndDrag();
            _selected = to;
            Apply(s => s.MoveMarking(from.Organ!, from.Layer!, from.Index, to.Index - from.Index));
            SetStatus($"Moved to place {to.Index + 1} in {Words(from.Layer!)}.");
            return;
        }
        EndDrag();
    }

    private void ShowDropTarget(TreeViewItem? item)
    {
        if (_dropTarget?.Header is Panel previous)
            previous.Background = null;
        _dropTarget = item;
        if (item?.Header is Panel panel)
            panel.Background = new SolidColorBrush(Color.Parse("#5C86B8"), 0.35);
    }

    private void EndDrag()
    {
        ShowDropTarget(null);
        _dragFrom = null;
        _dragging = false;
    }
}
