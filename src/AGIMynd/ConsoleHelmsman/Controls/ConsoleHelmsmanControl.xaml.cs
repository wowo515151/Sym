//Copyright Warren Harding 2025.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

// Re: input clearing tracing

namespace ConsoleHelmsman.Controls;

public partial class ConsoleHelmsmanControl : UserControl
{
    public ConsoleHelmsmanControl()
    {
        InitializeComponent();
        var vm = new MainViewModel();
        DataContext = vm;
    }

    public void Shutdown()
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.Shutdown();
        }
    }

    public static readonly DependencyProperty HostIsAgiProperty = DependencyProperty.Register(
        "HostIsAgi", typeof(bool), typeof(ConsoleHelmsmanControl), new PropertyMetadata(false, OnHostIsAgiChanged));

    public bool HostIsAgi
    {
        get => (bool)GetValue(HostIsAgiProperty);
        set => SetValue(HostIsAgiProperty, value);
    }

    private static void OnHostIsAgiChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ConsoleHelmsmanControl ctl && ctl.DataContext is MainViewModel vm)
        {
            vm.IsEmbeddedHost = (bool)e.NewValue;
        }
    }

    private void ConsoleInputTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (DataContext is MainViewModel vm)
            {
                // Forward to VM send command for Ctrl+Enter
                vm.SendCommand.Execute(null);
            }
            e.Handled = true;
        }
    }
    

    private void CurrentDirectoryTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (DataContext is MainViewModel viewModel)
            {
                try
                {
                    var txt = CurrentDirectoryTextBox.Text?.Trim() ?? string.Empty;
                    viewModel.CurrentDirectory = txt;
                }
                catch { }
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
    
}
