using Avalonia.Controls;
using HdrDoctor.App.ViewModels;

namespace HdrDoctor.App.Views;

public partial class MainWindow : Window
{
    private bool _askingToQuit;
    private bool _quitConfirmed;

    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Holds the close while an operation runs and asks first.
    /// </summary>
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        if (e.Cancel || _quitConfirmed || DataContext is not MainViewModel { IsBusy: true } viewModel)
        {
            return;
        }

        e.Cancel = true;
        
        if (_askingToQuit)
        {
            return;
        }

        _askingToQuit = true;

        try
        {
            _quitConfirmed = await viewModel.ConfirmQuitAsync();
        }
        finally
        {
            _askingToQuit = false;
        }

        if (_quitConfirmed)
        {
            Close();
        }
    }
}
