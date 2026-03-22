using System.ComponentModel;

namespace ConsoleHelmsmanWPF;

public partial class MainWindow
{
    public MainWindow()
    {
        InitializeComponent();
        Closing += MainWindow_OnClosing;
    }

    private void MainWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        HostedControl?.Shutdown();
    }
}
