//Copyright Warren Harding 2025.
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ConsoleHelmsman;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closing += MainWindow_OnClosing;
    }

    private void ConsoleInputTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.SendCommand.Execute(null);
            }

            e.Handled = true;
        }
    }

    private void ConsoleDisplayTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.ScrollToEnd();
        }
    }

    private void MainWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        HostedControl?.Shutdown();
    }
}
