// Copyright Warren Harding 2026
using System.Windows;
using ConsoleHelmsman.Models;
using ConsoleHelmsman.Services;

namespace ConsoleHelmsmanWPF;

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
        try
        {
            DialogResult = true;
        }
        catch
        {
            Close();
        }
    }
}
