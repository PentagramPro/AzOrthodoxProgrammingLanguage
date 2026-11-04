using System.Windows;

namespace Skriptorij;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, ex) =>
        {
            MessageBox.Show(ex.Exception.ToString(), "Скрипторїй: бѣда", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };
        var args = e.Args.ToList();
        string? snapshot = null, project = null, open = null;
        bool runFirst = false;
        for (int i = 0; i < args.Count; i++)
        {
            if (args[i] is "--снимок" or "--snapshot" && i + 1 < args.Count) snapshot = args[++i];
            else if (args[i] is "--открыть" or "--open" && i + 1 < args.Count) open = args[++i];
            else if (args[i] is "--пуск" or "--run") runFirst = true;
            else project ??= args[i];
        }
        var w = new MainWindow(project, open, snapshot, runFirst);
        w.Show();
    }
}
