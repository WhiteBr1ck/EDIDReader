using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace EDIDReader.App.Controls;

public sealed class SelectableTextBlock : TextBox
{
    private static WeakReference<SelectableTextBlock>? _selectionOwner;
    private static bool _isChangingSelectionOwner;

    private Point _mouseDownPosition;
    private int _selectionLengthOnMouseDown;
    private bool _isMouseDown;

    public static readonly DependencyProperty TextTrimmingProperty = DependencyProperty.Register(
        nameof(TextTrimming),
        typeof(TextTrimming),
        typeof(SelectableTextBlock),
        new FrameworkPropertyMetadata(TextTrimming.None));

    public TextTrimming TextTrimming
    {
        get => (TextTrimming)GetValue(TextTrimmingProperty);
        set => SetValue(TextTrimmingProperty, value);
    }

    static SelectableTextBlock()
    {
        TextProperty.OverrideMetadata(
            typeof(SelectableTextBlock),
            new FrameworkPropertyMetadata(string.Empty) { DefaultUpdateSourceTrigger = UpdateSourceTrigger.Explicit });
    }

    public SelectableTextBlock()
    {
        RenderOptions.SetClearTypeHint(this, ClearTypeHint.Enabled);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        TextOptions.SetTextHintingMode(this, TextHintingMode.Fixed);
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        ContextMenuOpening += OnContextMenuOpening;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && SelectionLength > 0)
        {
            Select(SelectionStart, 0);
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    protected override void OnSelectionChanged(RoutedEventArgs e)
    {
        base.OnSelectionChanged(e);

        if (_isChangingSelectionOwner)
        {
            return;
        }

        if (SelectionLength == 0)
        {
            if (_selectionOwner?.TryGetTarget(out var owner) == true && ReferenceEquals(owner, this))
            {
                _selectionOwner = null;
            }
            return;
        }

        if (_selectionOwner?.TryGetTarget(out var previousOwner) == true
            && !ReferenceEquals(previousOwner, this)
            && previousOwner.SelectionLength > 0)
        {
            _isChangingSelectionOwner = true;
            try
            {
                previousOwner.Select(previousOwner.SelectionStart, 0);
            }
            finally
            {
                _isChangingSelectionOwner = false;
            }
        }

        _selectionOwner = new WeakReference<SelectableTextBlock>(this);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _mouseDownPosition = e.GetPosition(this);
        _selectionLengthOnMouseDown = SelectionLength;
        _isMouseDown = true;
        base.OnMouseLeftButtonDown(e);
    }

    protected override void OnMouseDoubleClick(MouseButtonEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        Focus();
        SelectAll();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (!_isMouseDown)
        {
            return;
        }

        _isMouseDown = false;
        var mouseUpPosition = e.GetPosition(this);
        var isClick = Math.Abs(mouseUpPosition.X - _mouseDownPosition.X) <= SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(mouseUpPosition.Y - _mouseDownPosition.Y) <= SystemParameters.MinimumVerticalDragDistance;

        if (isClick
            && _selectionLengthOnMouseDown == 0
            && SelectionLength == 0
            && FindAncestor<ToggleButton>(this) is { } toggleButton)
        {
            toggleButton.IsChecked = toggleButton.IsChecked != true;
            e.Handled = true;
        }
    }

    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (SelectionLength == 0)
        {
            e.Handled = true;
        }
    }

    private static T? FindAncestor<T>(DependencyObject element) where T : DependencyObject
    {
        for (var parent = VisualTreeHelper.GetParent(element); parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is T match)
            {
                return match;
            }
        }

        return null;
    }
}
