using Avalonia.Controls;
using Pixelscreens.ViewModels;

namespace Pixelscreens.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        LayoutCanvas.LayoutChanged += async (_, _) =>
        {
            if (DataContext is MainViewModel vm) await vm.Layout.CommitDragAsync();
        };
        LayoutCanvas.MonitorOpened += (_, box) =>
        {
            if (DataContext is MainViewModel vm) vm.Layout.Open(box);
        };
        LayoutCanvas.MonitorClicked += (_, box) =>
        {
            if (DataContext is MainViewModel vm) vm.Layout.Select(box);
        };
    }
}
