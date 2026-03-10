using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace Audiomatic;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        this.InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        SetCurrentProcessExplicitAppUserModelID("Audiomatic.App");
        _window = new MainWindow();
        _window.Activate();
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string AppID);
}
