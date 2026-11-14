using System.Windows;
using System.Windows.Threading;

namespace TabMan.App;

public partial class App : Application
{
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        _controller = new AppController();
        if (!_controller.Start())
        {
            if (_controller.StartupFailure is { } failure)
            {
                MessageBox.Show(
                    "TabMan could not start (" + failure + "). Your windows were not changed.",
                    "TabMan",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            Shutdown(0);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _controller = null;
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _controller?.LogFault(e.Exception);
        e.Handled = true;
        MessageBox.Show(
            "TabMan hit a problem and kept running. Window titles are not written to the log.",
            "TabMan",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }
}
