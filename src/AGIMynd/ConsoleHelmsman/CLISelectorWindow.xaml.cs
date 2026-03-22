//Copyright Warren Harding 2025.
using System;
using System.Windows;

namespace ConsoleHelmsman;

public partial class CLISelectorWindow : Window
{
    private readonly ConsoleAppBases _config;
    private readonly ConsoleConfigService _service;

    public CLISelectorWindow(ConsoleAppBases config, ConsoleConfigService service)
    {
        InitializeComponent();
        _config = config;
        _service = service;
        DataContext = _config;
    }

    private void Item_Checked(object sender, RoutedEventArgs e)
    {
        TrySaveConfig();
    }

    private void Item_Unchecked(object sender, RoutedEventArgs e)
    {
        TrySaveConfig();
    }

    private void TrySaveConfig()
    {
        try
        {
            _service.Save(_config);
        }
        catch
        {
            // Ignore save failures triggered by rapid UI changes.
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        // Signal the caller that the dialog closed and selections should be reloaded.
        try
        {
            DialogResult = true;
        }
        catch
        {
            // If DialogResult cannot be set (non-modal), fall back to Close.
            Close();
        }
    }
}
