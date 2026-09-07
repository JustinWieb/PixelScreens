using CommunityToolkit.Mvvm.ComponentModel;

namespace Pixelscreens.ViewModels;

/// <summary>One monitor on the physical layout canvas. Positions and sizes are millimetres.</summary>
public sealed partial class MonitorBox : ObservableObject
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public string Detail { get; init; } = "";
    public bool IsPrimary { get; init; }

    [ObservableProperty] private double _xMm;
    [ObservableProperty] private double _yMm;
    [ObservableProperty] private double _widthMm;
    [ObservableProperty] private double _heightMm;

    public int PixelWidth { get; init; }
    public int PixelHeight { get; init; }
    public double Scale { get; init; } = 1;
}
