using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using Application = System.Windows.Application;

namespace DeskTemp;

public partial class App : Application
{
    private Mutex? _mutex;

    public static bool IsAdministrator { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        bool preview = e.Args.Any(a => a.Equals("--preview", StringComparison.OrdinalIgnoreCase));
        bool noElevate = e.Args.Any(a => a.Equals("--no-elevate", StringComparison.OrdinalIgnoreCase));
        IsAdministrator = CheckAdministrator();

        // CPU temperature needs the low-level driver, which needs administrator rights.
        // Relaunch ourselves elevated once; if the user declines, keep running with GPU-only data.
        if (!preview && !noElevate && !IsAdministrator && IsPublishedExe())
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = Environment.ProcessPath!,
                    UseShellExecute = true,
                    Verb = "RunAs",
                    WorkingDirectory = AppContext.BaseDirectory,
                };
                foreach (string arg in e.Args)
                    psi.ArgumentList.Add(arg);

                Process.Start(psi);
                Shutdown();
                return;
            }
            catch (Win32Exception)
            {
                // UAC was cancelled - continue in limited mode.
            }
        }

        // Preview is a development mode; let it run alongside the installed widget for side-by-side checks.
        string mutexName = preview ? @"Local\DeskTemp.Preview" : @"Local\DeskTemp.SingleInstance";
        _mutex = new Mutex(initiallyOwned: true, mutexName, out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("温度监控已经在运行了。", "DeskTemp",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var window = new MainWindow(preview, IsAdministrator);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_mutex != null)
        {
            try { _mutex.ReleaseMutex(); } catch { }
            _mutex.Dispose();
        }
        base.OnExit(e);
    }

    private static bool CheckAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPublishedExe()
    {
        string? path = Environment.ProcessPath;
        return path != null &&
               path.EndsWith("DeskTemp.exe", StringComparison.OrdinalIgnoreCase);
    }
}
