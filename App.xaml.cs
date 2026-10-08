using Microsoft.UI.Xaml;
namespace FluentLegionToolbar;
public partial class App : Application
{
    private MainWindow? window;
    public App() {
        DebugSettings.XamlResourceReferenceFailed += (_,e) => Log(new Exception("Resource: "+e.Message));
        UnhandledException += (_,e) => { Log(new Exception("XAML event: "+e.Message)); Log(e.Exception); };
        AppDomain.CurrentDomain.UnhandledException += (_,e) => Log(e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));
        try { InitializeComponent(); } catch(Exception e) { Log(e); throw; }
    }
    static void Log(Exception e) { try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory,"launch-error.txt"),e.ToString()+"\n"); } catch { } }
    protected override void OnLaunched(LaunchActivatedEventArgs args) { try { window = new MainWindow(); window.Activate(); } catch(Exception e) { Log(e); throw; } }
}
