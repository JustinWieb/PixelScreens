using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Pixelscreens.ViewModels;
using Pixelscreens.Views;

namespace Pixelscreens;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm };
            var args = Environment.GetCommandLineArgs();
            if (args.Contains("--tab=layout")) vm.SelectedTab = 1;
            if (args.Contains("--tab=settings")) vm.SelectedTab = 2;
            window.Opened += async (_, _) =>
            {
                await vm.InitialiseAsync();
                if (args.Contains("--start-hook")) await vm.Layout.StartCommand.ExecuteAsync(null);
            };
            window.Closing += (_, _) => vm.Shutdown();
            desktop.Exit += (_, _) => vm.Dispose();
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
