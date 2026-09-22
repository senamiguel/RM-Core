using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

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

            AllowUipiMessages();

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
            _notifyIcon.MouseDoubleClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) ShowMainWindow();
            };
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ChangeWindowMessageFilter(uint message, uint dwFlag);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern uint RegisterWindowMessage(string lpString);

        private const uint MSGFLT_ADD = 1;

        private static void AllowUipiMessages()
        {
            try
            {
                uint[] messages = {
                    0x004A, // WM_COPYDATA
                    0x0111, // WM_COMMAND
                    0x0200, // WM_MOUSEMOVE
                    0x0201, // WM_LBUTTONDOWN
                    0x0202, // WM_LBUTTONUP
                    0x0203, // WM_LBUTTONDBLCLK
                    0x0204, // WM_RBUTTONDOWN
                    0x0205, // WM_RBUTTONUP
                    0x0400  // WM_USER
                };

                foreach (var msg in messages)
                {
                    ChangeWindowMessageFilter(msg, MSGFLT_ADD);
                }

                uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
                if (taskbarCreated != 0)
                {
                    ChangeWindowMessageFilter(taskbarCreated, MSGFLT_ADD);
                }
            }
            catch { }
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
            ShowToast("RM Core", "Minimizado para a bandeja. Clique duplo para restaurar.");
        }

        /// <summary>Shows a custom toast popup with the app icon.</summary>
        public void ShowToast(string title, string text)
        {
            try
            {
                _mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var toast = new ToastPopup(title, text);
                    toast.Show();
                }));
            }
            catch
            {
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

        private class DarkMenuColorTable : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Color.FromArgb(32, 32, 35);
            public override Color ImageMarginGradientBegin => Color.FromArgb(32, 32, 35);
            public override Color ImageMarginGradientMiddle => Color.FromArgb(32, 32, 35);
            public override Color ImageMarginGradientEnd => Color.FromArgb(32, 32, 35);
            public override Color MenuBorder => Color.FromArgb(60, 60, 65);
            public override Color MenuItemBorder => Color.FromArgb(0, 120, 212);
            public override Color MenuItemSelected => Color.FromArgb(48, 48, 54);
            public override Color MenuStripGradientBegin => Color.FromArgb(32, 32, 35);
            public override Color MenuStripGradientEnd => Color.FromArgb(32, 32, 35);
            public override Color MenuItemSelectedGradientBegin => Color.FromArgb(48, 48, 54);
            public override Color MenuItemSelectedGradientEnd => Color.FromArgb(48, 48, 54);
            public override Color MenuItemPressedGradientBegin => Color.FromArgb(40, 40, 45);
            public override Color MenuItemPressedGradientEnd => Color.FromArgb(40, 40, 45);
            public override Color SeparatorDark => Color.FromArgb(60, 60, 65);
            public override Color SeparatorLight => Color.FromArgb(45, 45, 50);
        }

        private ContextMenuStrip BuildContextMenu()
        {
            var menu = new ContextMenuStrip
            {
                Renderer = new ToolStripProfessionalRenderer(new DarkMenuColorTable()),
                ShowImageMargin = false
            };

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

            // Abrir Clientes
            var itemClientes = new ToolStripMenuItem("Abrir Clientes");
            itemClientes.Click += (_, _) => _mainWindow.Dispatcher.BeginInvoke(new Action(() => _mainWindow.NavigateToClientes()));
            menu.Items.Add(itemClientes);

            // Abrir Gerenciador de Bases
            var itemBases = new ToolStripMenuItem("Abrir Gerenciador de Bases");
            itemBases.Click += (_, _) => _mainWindow.Dispatcher.BeginInvoke(new Action(() => _mainWindow.NavigateToBases()));
            menu.Items.Add(itemBases);

            // Verificar Atualizações
            var itemUpdate = new ToolStripMenuItem("Verificar Atualizações");
            itemUpdate.Click += (_, _) => _mainWindow.Dispatcher.BeginInvoke(new Action(() => _mainWindow.TriggerUpdateCheck()));
            menu.Items.Add(itemUpdate);

            menu.Items.Add(new ToolStripSeparator());

            // Sair
            var itemSair = new ToolStripMenuItem("Sair");
            itemSair.Click += OnSair;
            menu.Items.Add(itemSair);

            foreach (ToolStripItem item in menu.Items)
            {
                item.ForeColor = Color.White;
            }

            return menu;
        }

        private void OnIniciarRMPlusHost(object? sender, EventArgs e)
        {
            _mainWindow.Dispatcher.BeginInvoke(new Action(async () =>
            {
                await _mainWindow.IniciarRMPlusHostAsync();
            }));
        }

        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        public void ShowMainWindow()
        {
            _mainWindow.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    _mainWindow.Show();
                    _mainWindow.Visibility = Visibility.Visible;
                    _mainWindow.ShowInTaskbar = true;
                    _mainWindow.WindowState = WindowState.Normal;

                    var helper = new System.Windows.Interop.WindowInteropHelper(_mainWindow);
                    IntPtr hwnd = helper.EnsureHandle();
                    if (hwnd != IntPtr.Zero)
                    {
                        ShowWindow(hwnd, SW_RESTORE);
                        SetForegroundWindow(hwnd);
                    }

                    if (!_mainWindow.IsPositionOnScreen(_mainWindow.Left, _mainWindow.Top,
                                                        _mainWindow.Width, _mainWindow.Height) 
                        || double.IsNaN(_mainWindow.Left) || double.IsNaN(_mainWindow.Top) 
                        || _mainWindow.Left <= -10000 || _mainWindow.Top <= -10000)
                    {
                        var workArea = SystemParameters.WorkArea;
                        _mainWindow.WindowStartupLocation = WindowStartupLocation.Manual;
                        _mainWindow.Left = workArea.Left + Math.Max(0, (workArea.Width  - _mainWindow.Width)  / 2);
                        _mainWindow.Top  = workArea.Top  + Math.Max(0, (workArea.Height - _mainWindow.Height) / 2);
                    }

                    _mainWindow.Topmost = true;
                    _mainWindow.Activate();
                    _mainWindow.Topmost = false;
                    _mainWindow.Focus();
                }
                catch { }
            }));
        }

        private void OnKillAllProcesses(object? sender, EventArgs e)
        {
            try
            {
                string[] processNames = { "RM", "RM.Host.ServiceManager", "RM.Host", "RM.Host1", "RM.Host.Service", "RM.ProcessPool.Process", "RM.Host.JobServer" };
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
            try
            {
                _mainWindow.IsExiting = true;
                Dispose();
            }
            catch { }

            try
            {
                _mainWindow.Dispatcher.Invoke(() =>
                {
                    try { _mainWindow.Close(); } catch { }
                    try { Application.Current?.Shutdown(); } catch { }
                }, System.Windows.Threading.DispatcherPriority.Send, TimeSpan.FromMilliseconds(300));
            }
            catch { }

            try
            {
                Process.GetCurrentProcess().Kill();
            }
            catch
            {
                Environment.Exit(0);
            }
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
