using CommunityToolkit.Mvvm.ComponentModel;

namespace Pixelscreens.ViewModels;

/// <summary>One monitor on the physical layout canvas. Positions and sizes are millimetres.</summary>
public sealed partial class MonitorBox : ObservableObject
{
    private const double MmPerInch = 25.4;
    private bool _syncing;

    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public string Detail { get; init; } = "";
    public bool IsPrimary { get; init; }
    public int PixelWidth { get; init; }
    public int PixelHeight { get; init; }

    [ObservableProperty] private bool _isSelected;

    // Position of the visible panel (not counting bezels).
    [ObservableProperty] private double _xMm;
    [ObservableProperty] private double _yMm;

    // Visible panel size.
    [ObservableProperty] private double _widthMm;
    [ObservableProperty] private double _heightMm;
    [ObservableProperty] private double _diagonalInches;

    // Bezels around the panel.
    [ObservableProperty] private double _bezelTopMm;
    [ObservableProperty] private double _bezelRightMm;
    [ObservableProperty] private double _bezelBottomMm;
    [ObservableProperty] private double _bezelLeftMm;

    /// <summary>Set once by the owner after the fields are populated, so edits can be tracked.</summary>
    public event EventHandler? Edited;

    partial void OnWidthMmChanged(double value) => FromPanel();
    partial void OnHeightMmChanged(double value) => FromPanel();
    partial void OnDiagonalInchesChanged(double value) => FromDiagonal();
    partial void OnBezelTopMmChanged(double value) => Edited?.Invoke(this, EventArgs.Empty);
    partial void OnBezelRightMmChanged(double value) => Edited?.Invoke(this, EventArgs.Empty);
    partial void OnBezelBottomMmChanged(double value) => Edited?.Invoke(this, EventArgs.Empty);
    partial void OnBezelLeftMmChanged(double value) => Edited?.Invoke(this, EventArgs.Empty);

    /// <summary>Width and height changed: recompute the diagonal.</summary>
    private void FromPanel()
    {
        if (_syncing) return;
        _syncing = true;
        try
        {
            DiagonalInches = Math.Round(Math.Sqrt(WidthMm * WidthMm + HeightMm * HeightMm) / MmPerInch, 1);
        }
        finally
        {
            _syncing = false;
        }
        Edited?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Diagonal changed: keep the pixel aspect ratio and recompute width and height.</summary>
    private void FromDiagonal()
    {
        if (_syncing || DiagonalInches <= 0) return;
        var pw = PixelWidth > 0 ? PixelWidth : 16;
        var ph = PixelHeight > 0 ? PixelHeight : 9;
        var angle = Math.Atan2(ph, pw);
        var dMm = DiagonalInches * MmPerInch;
        _syncing = true;
        try
        {
            WidthMm = Math.Round(dMm * Math.Cos(angle), 1);
            HeightMm = Math.Round(dMm * Math.Sin(angle), 1);
        }
        finally
        {
            _syncing = false;
        }
        Edited?.Invoke(this, EventArgs.Empty);
    }

    // ---- Unit-aware views of the mm fields (inches when UseInches, else mm) ----
    private bool _useInches = true;
    public bool UseInches
    {
        get => _useInches;
        set
        {
            if (SetProperty(ref _useInches, value)) RaiseUnitViews();
        }
    }
    public string Unit => UseInches ? "in" : "mm";
    private double ToUnit(double mm) => UseInches ? Math.Round(mm / MmPerInch, 2) : Math.Round(mm, 1);
    private double FromUnit(double v) => UseInches ? v * MmPerInch : v;

    public double WidthU { get => ToUnit(WidthMm); set => WidthMm = FromUnit(value); }
    public double HeightU { get => ToUnit(HeightMm); set => HeightMm = FromUnit(value); }
    public double BezelTopU { get => ToUnit(BezelTopMm); set => BezelTopMm = FromUnit(value); }
    public double BezelRightU { get => ToUnit(BezelRightMm); set => BezelRightMm = FromUnit(value); }
    public double BezelBottomU { get => ToUnit(BezelBottomMm); set => BezelBottomMm = FromUnit(value); }
    public double BezelLeftU { get => ToUnit(BezelLeftMm); set => BezelLeftMm = FromUnit(value); }
    public string SizeText => UseInches ? $"{WidthMm / MmPerInch:0.0} x {HeightMm / MmPerInch:0.0} in" : $"{WidthMm:0} x {HeightMm:0} mm";

    private void RaiseUnitViews()
    {
        foreach (var n in new[] { nameof(Unit), nameof(WidthU), nameof(HeightU), nameof(BezelTopU), nameof(BezelRightU), nameof(BezelBottomU), nameof(BezelLeftU), nameof(SizeText) })
            OnPropertyChanged(n);
    }

    partial void OnWidthMmChanged(double oldValue, double newValue) { OnPropertyChanged(nameof(WidthU)); OnPropertyChanged(nameof(SizeText)); }
    partial void OnHeightMmChanged(double oldValue, double newValue) { OnPropertyChanged(nameof(HeightU)); OnPropertyChanged(nameof(SizeText)); }
    partial void OnBezelTopMmChanged(double oldValue, double newValue) => OnPropertyChanged(nameof(BezelTopU));
    partial void OnBezelRightMmChanged(double oldValue, double newValue) => OnPropertyChanged(nameof(BezelRightU));
    partial void OnBezelBottomMmChanged(double oldValue, double newValue) => OnPropertyChanged(nameof(BezelBottomU));
    partial void OnBezelLeftMmChanged(double oldValue, double newValue) => OnPropertyChanged(nameof(BezelLeftU));

    /// <summary>Scale the panel to a new width, keeping the pixel aspect ratio. Used by corner drags.</summary>
    public void ResizeToWidth(double widthMm)
    {
        widthMm = Math.Clamp(widthMm, 50, 3000);
        var aspect = PixelWidth > 0 && PixelHeight > 0 ? (double)PixelHeight / PixelWidth : HeightMm / Math.Max(1, WidthMm);
        _syncing = true;
        WidthMm = Math.Round(widthMm, 1);
        HeightMm = Math.Round(widthMm * aspect, 1);
        _syncing = false;
        FromPanel();
    }

    public void SeedDiagonal()
    {
        _syncing = true;
        DiagonalInches = Math.Round(Math.Sqrt(WidthMm * WidthMm + HeightMm * HeightMm) / MmPerInch, 1);
        _syncing = false;
    }
}
