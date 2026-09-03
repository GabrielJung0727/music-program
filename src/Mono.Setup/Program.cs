using System.Diagnostics;
using System.Reflection;

namespace Mono.Setup;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        TryClearMarkOfTheWeb();
        var silent = args.Any(a => string.Equals(a, "--silent", StringComparison.OrdinalIgnoreCase));
        Application.Run(new WizardForm(silent));
    }

    private static void TryClearMarkOfTheWeb()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(path)) return;
            File.Delete(path + ":Zone.Identifier");
        }
        catch { /* ignore */ }
    }
}
