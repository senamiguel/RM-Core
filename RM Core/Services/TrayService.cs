using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;
using Microsoft.Toolkit.Uwp.Notifications;

namespace RM_Core.Services
{
    /// <summary>
    /// Encapsulates all NotifyIcon (system tray) logic:
    /// icon, context menu items, balloon tips and related events.
    /// </summary>
    public class TrayService : IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly MainWindow _mainWindow;
        private bool _disposed;

        public TrayService(MainWindow mainWindow)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));

            _notifyIcon = new NotifyIcon
            {
                Text = "RM Core",
                Icon = GetAppIcon(),
                Visible = true
            };

            // Build context menu
            _notifyIcon.ContextMenuStrip = BuildContextMenu();

            // Double-click shows and activates the main window
            _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();
        }

        // ---------------------------------------------------------------
        // Public API
        // ---------------------------------------------------------------

        /// <summary>
        /// Hides the main window and keeps the tray icon visible.
        /// </summary>
        public void MinimizeToTray()
        {
            _mainWindow.Hide();
            _notifyIcon.Visible = true;
            _notifyIcon.ShowBalloonTip(
                timeout: 1500,
                tipTitle: "RM Core",
                tipText: "Minimizado para a bandeja. Clique duplo para restaurar.",
                tipIcon: ToolTipIcon.None);
        }

        /// <summary>Shows a toast notification (fallback to balloon if toast unavailable).</summary>
        public void ShowToast(string title, string text)
        {
            try
            {
                new ToastContentBuilder()
                    .AddText(title)
                    .AddText(text)
                    .Show();
            }
            catch
            {
                // Fallback: sem atalho no Menu Iniciar → usa balloon
                _notifyIcon.ShowBalloonTip(4000, title, text, ToolTipIcon.None);
            }
        }

        /// <summary>Shows a balloon tip (legacy, kept for minimize-to-tray).</summary>
        public void ShowBalloon(string title, string text, ToolTipIcon icon = ToolTipIcon.None)
        {
            _notifyIcon.ShowBalloonTip(2000, title, text, icon);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _notifyIcon.Visible = false;
            _notifyIcon.ContextMenuStrip?.Dispose();
            _notifyIcon.Dispose();
            _disposed = true;
        }

        // ---------------------------------------------------------------
        // Private helpers
        // ---------------------------------------------------------------

        private ContextMenuStrip BuildContextMenu()
        {
            var menu = new ContextMenuStrip();

            // Abrir RM Core
            var itemAbrir = new ToolStripMenuItem("Abrir RM Core");
            itemAbrir.Font = new Font(itemAbrir.Font, System.Drawing.FontStyle.Bold); // highlight default action
            itemAbrir.Click += (_, _) => ShowMainWindow();
            menu.Items.Add(itemAbrir);

            menu.Items.Add(new ToolStripSeparator());

            // Iniciar RM + Host
            var itemIniciar = new ToolStripMenuItem("Iniciar RM + Host");
            itemIniciar.Click += OnIniciarRMPlusHost;
            menu.Items.Add(itemIniciar);

            // Matar todos processos RM
            var itemMatar = new ToolStripMenuItem("Matar todos processos RM");
            itemMatar.Click += OnKillAllProcesses;
            menu.Items.Add(itemMatar);

            // Reiniciar IIS
            var itemIIS = new ToolStripMenuItem("Reiniciar IIS");
            itemIIS.Click += OnReiniciarIIS;
            menu.Items.Add(itemIIS);

            menu.Items.Add(new ToolStripSeparator());

            // Sair
            var itemSair = new ToolStripMenuItem("Sair");
            itemSair.Click += OnSair;
            menu.Items.Add(itemSair);

            return menu;
        }

        private void OnIniciarRMPlusHost(object? sender, EventArgs e)
        {
            _mainWindow.Dispatcher.BeginInvoke(new Action(async () =>
            {
                await _mainWindow.IniciarRMPlusHostAsync();
            }));
        }

        private void ShowMainWindow()
        {
            _mainWindow.Show();

            // Se a janela foi salva off-screen (monitor desconectado, etc),
            // traz de volta pro centro do monitor principal.
            if (!_mainWindow.IsPositionOnScreen(_mainWindow.Left, _mainWindow.Top,
                                                _mainWindow.Width, _mainWindow.Height))
            {
                var workArea = SystemParameters.WorkArea;
                _mainWindow.WindowStartupLocation = System.Windows.WindowStartupLocation.Manual;
                _mainWindow.Left = workArea.Left + (workArea.Width  - _mainWindow.Width)  / 2;
                _mainWindow.Top  = workArea.Top  + (workArea.Height - _mainWindow.Height) / 2;
            }

            if (_mainWindow.WindowState == System.Windows.WindowState.Minimized)
                _mainWindow.WindowState = System.Windows.WindowState.Normal;

            // Garante que vai pra frente mesmo se outra janela tiver foco
            _mainWindow.Topmost = true;
            _mainWindow.Activate();
            _mainWindow.Topmost = false;
            _mainWindow.Focus();
        }

        private void OnKillAllProcesses(object? sender, EventArgs e)
        {
            try
            {
                string[] processNames = { "RM", "RM.Host.ServiceManager", "RM.Host", "RM.Host.Service" };
                int killed = 0;
                foreach (string name in processNames)
                {
                    foreach (var proc in Process.GetProcessesByName(name))
                    {
                        try { proc.Kill(); killed++; } catch { /* ignore */ }
                    }
                }
                ShowBalloon("RM Core", $"{killed} processo(s) RM encerrado(s).", ToolTipIcon.None);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Erro ao matar processos: {ex.Message}", "RM Core",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnReiniciarIIS(object? sender, EventArgs e)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "iisreset.exe",
                    UseShellExecute = true,
                    Verb = "runas"
                };
                var proc = Process.Start(psi);
                proc?.WaitForExit();
                bool ok = proc?.ExitCode == 0;
                ShowBalloon("RM Core",
                    ok ? "IIS reiniciado com sucesso." : "Falha ao reiniciar o IIS.",
                    ok ? ToolTipIcon.None : ToolTipIcon.Error);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // UAC cancelled by user — silently ignore
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Erro ao reiniciar IIS: {ex.Message}", "RM Core",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnSair(object? sender, EventArgs e)
        {
            _mainWindow.IsExiting = true;
            Dispose();
            _mainWindow.Close();
            Application.Current.Shutdown();
        }

        private static Icon GetAppIcon()
        {
            // 1. Try to load from WPF Application Resources stream (RM_CORE.ico resource)
            try
            {
                var uri = new Uri("pack://application:,,,/RM_CORE.ico");
                var streamInfo = Application.GetResourceStream(uri);
                if (streamInfo != null)
                {
                    using (var stream = streamInfo.Stream)
                    {
                        return new Icon(stream);
                    }
                }
            }
            catch { /* ignore */ }

            // 2. Try using Environment.ProcessPath (.NET 6+)
            try
            {
                string? exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath) && System.IO.File.Exists(exePath))
                {
                    var extracted = Icon.ExtractAssociatedIcon(exePath);
                    if (extracted != null) return extracted;
                }
            }
            catch { /* ignore */ }

            // 3. Fallback: try to extract icon from the running executable
            try
            {
                string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                if (exePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    exePath = exePath.Substring(0, exePath.Length - 4) + ".exe";
                }
                if (!string.IsNullOrEmpty(exePath) && System.IO.File.Exists(exePath))
                {
                    var extracted = Icon.ExtractAssociatedIcon(exePath);
                    if (extracted != null) return extracted;
                }
            }
            catch { /* ignore */ }

            // 4. Last fallback: use a standard Windows application icon
            return SystemIcons.Application;
        }
    }
}
