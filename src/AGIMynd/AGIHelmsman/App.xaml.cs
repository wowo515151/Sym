//Copyright Warren Harding 2025.
using System.Windows;

namespace AGIHelmsman;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Ensure MainWindow is created if not already (e.g. by test harness)
        if (MainWindow == null)
        {
            var window = new MainWindow();
            window.Show();
        }
    }
}
