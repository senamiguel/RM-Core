using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace RM_Core
{
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Forçar tema escuro (Dark Theme) exclusivo em toda a aplicação, ignorando o tema do Windows
            iNKORE.UI.WPF.Modern.ThemeManager.Current.ApplicationTheme = iNKORE.UI.WPF.Modern.ApplicationTheme.Dark;

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                LogCrash("AppDomain.UnhandledException", args.ExceptionObject as Exception);
            };

            DispatcherUnhandledException += (s, args) =>
            {
                LogCrash("DispatcherUnhandledException", args.Exception);
                args.Handled = false;
            };

            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                LogCrash("TaskScheduler.UnobservedTaskException", args.Exception);
            };

            try
            {
                var mainWindow = new MainWindow();
                MainWindow = mainWindow;
                mainWindow.Show();
            }
            catch (Exception ex)
            {
                LogCrash("Startup Exception", ex);
                MessageBox.Show(
                    $"Erro ao iniciar o RM Core:\n\n{ex.Message}\n\nDetalhes gravados em %LocalAppData%\\RM_Core\\crash.log",
                    "RM Core - Erro de Inicialização",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Environment.Exit(1);
            }
        }

        private static void LogCrash(string source, Exception? ex)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RM_Core");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "crash.log");
                string msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}]\n{ex}\n\n";
                File.AppendAllText(file, msg);
            }
            catch { }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);
            try { System.Diagnostics.Process.GetCurrentProcess().Kill(); } catch { System.Environment.Exit(0); }
        }
    }
}
