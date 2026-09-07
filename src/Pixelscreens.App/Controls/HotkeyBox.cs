using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Pixelscreens.Services;

namespace Pixelscreens.Controls;

/// <summary>
/// A small box that records a key gesture when focused. Backspace or Delete clears it.
/// Bound value is the raw gesture string ("Ctrl+Alt+D1"); the box shows the pretty form.
/// </summary>
public sealed class HotkeyBox : Border
{
    public static readonly StyledProperty<string?> GestureProperty =
        AvaloniaProperty.Register<HotkeyBox, string?>(nameof(Gesture), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public string? Gesture { get => GetValue(GestureProperty); set => SetValue(GestureProperty, value); }

    private readonly TextBlock _text = new() { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };

    public HotkeyBox()
    {
        Focusable = true;
        MinWidth = 120;
        MinHeight = 28;
        Padding = new Thickness(8, 4);
        BorderThickness = new Thickness(1);
        Cursor = new Cursor(StandardCursorType.Hand);
        Child = _text;
        GotFocus += (_, _) => Render();
        LostFocus += (_, _) => Render();
        Render();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == GestureProperty) Render();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key;
        if (key is Key.Back or Key.Delete)
        {
            Gesture = null;
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return; // wait for the actual key
        if (e.KeyModifiers == KeyModifiers.None && key is not (>= Key.F1 and <= Key.F24))
            return; // require a modifier for anything but F-keys
        var gesture = HotkeyService.Format(e.KeyModifiers, key);
        if (HotkeyService.TryParse(gesture, out _, out _)) Gesture = gesture;
    }

    private void Render()
    {
        var focused = IsFocused;
        var g = Gesture;
        _text.Text = string.IsNullOrEmpty(g) ? (focused ? "press keys…" : "none") : HotkeyService.Pretty(g);
        _text.Foreground = string.IsNullOrEmpty(g) ? new SolidColorBrush(Color.Parse("#5A5A5A")) : new SolidColorBrush(Color.Parse("#F2F2F2"));
        BorderBrush = new SolidColorBrush(Color.Parse(focused ? "#F2F2F2" : "#303030"));
        Background = new SolidColorBrush(Color.Parse("#0A0A0A"));
    }
}
