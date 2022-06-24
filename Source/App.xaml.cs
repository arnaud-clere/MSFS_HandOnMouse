using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Navigation;

namespace HandOnMouse
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            EventManager.RegisterClassHandler(
                typeof(Hyperlink),
                Hyperlink.RequestNavigateEvent,
                new RequestNavigateEventHandler((sender, en) => Process.Start(new ProcessStartInfo(en.Uri.ToString()) { UseShellExecute = true }))
            );
            base.OnStartup(e);
        }
    }
}
