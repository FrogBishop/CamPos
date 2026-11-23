using CamPos.Core;

namespace CamPos.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            MessageBox.Show(e.Exception.ToString(), BuildInfo.Name + " 未处理异常",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        var startDemo = args.Any(a => a.Equals("--demo", StringComparison.OrdinalIgnoreCase));
        var startInjected = args.Any(a => a.Equals("--injected", StringComparison.OrdinalIgnoreCase));
        Application.Run(new MainForm(startDemo, startInjected));
    }
}
