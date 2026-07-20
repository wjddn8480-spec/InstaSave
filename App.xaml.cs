using System.Windows;
using InstaSave.Services;

namespace InstaSave;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        var settings = new SettingsService().Load();
        LocalizationService.SetLanguageMode(settings.LanguageMode);

        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            LocalizedMessageBox.Show(
                $"예상하지 못한 오류가 발생했습니다.\n\n{args.Exception.Message}",
                "InstaSave 오류",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
}
