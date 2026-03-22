//Copyright Warren Harding 2025.
using System.Windows;

namespace AGIHelmsman;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            ConsoleTabControl?.Shutdown();
        }
        catch { }

        try
        {
            AGIMyndTabControl?.Shutdown();
        }
        catch { }

        base.OnClosing(e);
    }

    private void StartBoth_Click(object sender, RoutedEventArgs e)
    {
        // Controls initialize themselves when constructed; no-op placeholder
    }

    private void StopBoth_Click(object sender, RoutedEventArgs e)
    {
        // Provide a simple shutdown: call shutdown on the console control and leave AGI control as-is
        try
        {
            ConsoleTabControl?.Shutdown();
        }
        catch
        {
        }
    }
}
