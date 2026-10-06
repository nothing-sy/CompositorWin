using System.Windows;

namespace CompositorWin;

public partial class App : Application
{
    public static string[] LaunchArgs { get; private set; } = [];

    protected override void OnStartup(StartupEventArgs e)
    {
        LaunchArgs = e.Args;
        base.OnStartup(e);
    }
}
