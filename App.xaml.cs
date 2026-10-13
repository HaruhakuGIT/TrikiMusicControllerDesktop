namespace TrikiMusicController;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        Loc.LoadConfig();
        base.OnStartup(e);
    }
}
