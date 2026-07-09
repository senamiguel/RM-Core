using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;

namespace RM_Core
{
    public partial class IISConfigWindow : Window
    {
        private class SiteInfo
        {
            public string Name { get; set; } = string.Empty;
            public string Id { get; set; } = string.Empty;
            public string PhysicalPath { get; set; } = string.Empty;
            public string AppName { get; set; } = string.Empty;
            public string WebConfigDir { get; set; } = string.Empty;
        }

        private readonly List<SiteInfo> _sites = new();
        private readonly List<string> _webConfigPaths = new();
        private SiteInfo? _selectedSite;

        public IISConfigWindow()
        {
            InitializeComponent();
            LoadSites();
        }

        private void LoadSites()
        {
            try
            {
                string windir = Environment.GetEnvironmentVariable("windir") ?? @"C:\Windows";
                string appCmdPath = Path.Combine(windir, @"system32\inetsrv\appcmd.exe");

                if (!File.Exists(appCmdPath))
                {
                    AddLog("appcmd.exe não encontrado. IIS pode não estar instalado.");
                    return;
                }

                // 1) Pega todos os apps: APP.NAME, SITE.NAME, path
                var appsXml = RunAppCmd(appCmdPath, "list apps /xml");
                var appsDoc = XDocument.Parse(appsXml);

                // 2) Pega todos os vdirs: APP.NAME, physicalPath
                var vdirsXml = RunAppCmd(appCmdPath, "list vdirs /xml");
                var vdirsDoc = XDocument.Parse(vdirsXml);

                // Mapa APP.NAME -> physicalPath
                var physPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var vdir in vdirsDoc.Descendants().Where(e => e.Name.LocalName.Equals("VDIR", StringComparison.OrdinalIgnoreCase)))
                {
                    string appName = GetAttr(vdir, "APP.NAME");
                    string phys    = GetAttr(vdir, "physicalPath");
                    if (!string.IsNullOrEmpty(appName) && phys != null)
                        physPaths[appName] = phys;
                }

                // Itera apps e junta com physicalPath do vdir correspondente
                foreach (var appEl in appsDoc.Descendants().Where(e => e.Name.LocalName.Equals("APP", StringComparison.OrdinalIgnoreCase)))
                {
                    string appName  = GetAttr(appEl, "APP.NAME");
                    string siteName = GetAttr(appEl, "SITE.NAME");
                    string appPath  = GetAttr(appEl, "path") ?? "/";

                    // Pula o site raiz (path "/") — só mostra apps filhas
                    if (appPath == "/") continue;

                    string phys = physPaths.TryGetValue(appName ?? "", out var p) ? p : "";
                    string expanded = Environment.ExpandEnvironmentVariables(phys);

                    _sites.Add(new SiteInfo
                    {
                        Name         = appPath.TrimStart('/'),
                        AppName      = appName ?? "",
                        PhysicalPath = expanded,
                        WebConfigDir = expanded,
                    });
                }

                lstSites.Items.Clear();
                foreach (var s in _sites)
                    lstSites.Items.Add(s.Name);

                if (_sites.Count > 0)
                    lstSites.SelectedIndex = 0;

                AddLog($"{_sites.Count} app(s) carregado(s).");
            }
            catch (Exception ex)
            {
                AddLog($"Erro ao carregar sites: {ex.Message}");
            }
        }

        private static string RunAppCmd(string appCmdPath, string args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = appCmdPath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return "";
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();
            return output;
        }

        private static string? GetAttr(XElement el, string name)
        {
            return el.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;
        }

        private void lstSites_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstSites.SelectedIndex < 0 || lstSites.SelectedIndex >= _sites.Count) return;
            _selectedSite = _sites[lstSites.SelectedIndex];

            txtSiteName.Text = _selectedSite.Name;
            txtPhysicalPath.Text = _selectedSite.PhysicalPath;

            ScanWebConfigs(_selectedSite.PhysicalPath);
            if (lstWebConfig.Items.Count > 0)
                lstWebConfig.SelectedIndex = 0;
            else
                LoadUrlRewrite();
        }

        private void lstWebConfig_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            LoadUrlRewrite();
        }

        private void ScanWebConfigs(string rootPath)
        {
            _webConfigPaths.Clear();
            lstWebConfig.Items.Clear();
            if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath)) return;

            string expanded = Environment.ExpandEnvironmentVariables(rootPath);
            ScanRecursive(expanded, expanded, 0, 10);

            foreach (var p in _webConfigPaths)
                lstWebConfig.Items.Add(p);
        }

        private void ScanRecursive(string root, string dir, int depth, int maxDepth)
        {
            if (depth > maxDepth) return;
            try
            {
                string webConfig = Path.Combine(dir, "web.config");
                if (File.Exists(webConfig))
                {
                    string rel = Path.GetRelativePath(root, webConfig).Replace('\\', '/');
                    _webConfigPaths.Add($"{rel}|{webConfig}");
                }

                foreach (var sub in Directory.GetDirectories(dir))
                {
                    string name = Path.GetFileName(sub).ToLower();
                    if (name is "bin" or "obj" or ".git" or "node_modules") continue;
                    ScanRecursive(root, sub, depth + 1, maxDepth);
                }
            }
            catch { }
        }

        private string? GetSelectedWebConfigPath()
        {
            if (lstWebConfig.SelectedItem == null) return null;
            string sel = lstWebConfig.SelectedItem.ToString() ?? "";
            int sep = sel.IndexOf('|');
            if (sep < 0 || sep >= sel.Length - 1) return null;
            return sel.Substring(sep + 1);
        }

        private void LoadUrlRewrite()
        {
            txtUrlRewrite.Text = string.Empty;
            if (_selectedSite == null) return;

            string? webConfigPath = GetSelectedWebConfigPath();
            if (string.IsNullOrEmpty(webConfigPath)) return;
            if (!File.Exists(webConfigPath)) return;

            try
            {
                var doc = XDocument.Load(webConfigPath);
                var rewrite = doc.Descendants("rewrite").FirstOrDefault();
                if (rewrite != null)
                {
                    txtUrlRewrite.Text = rewrite.ToString();
                }
            }
            catch (Exception ex)
            {
                AddLog($"Erro ao ler URL Rewrite: {ex.Message}");
            }
        }

        private void btnSalvarPath_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedSite == null) return;
            string newPath = txtPhysicalPath.Text.Trim();
            if (string.IsNullOrEmpty(newPath))
            {
                MessageBox.Show("O caminho não pode estar vazio.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                string windir = Environment.GetEnvironmentVariable("windir") ?? @"C:\Windows";
                string appCmdPath = Path.Combine(windir, @"system32\inetsrv\appcmd.exe");

                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"\"{appCmdPath}\" set vdir \"{_selectedSite.AppName}/\"" + " /physicalPath:\"" + newPath + "\"",
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                var proc = Process.Start(psi);
                proc?.WaitForExit();

                if (proc != null && proc.ExitCode == 0)
                {
                    _selectedSite.PhysicalPath = newPath;
                    _selectedSite.WebConfigDir = newPath;
                    AddLog($"Path atualizado para: {newPath}");
                    MessageBox.Show("Caminho físico atualizado com sucesso!", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    AddLog("Falha ao atualizar path.");
                    MessageBox.Show("Falha ao atualizar o caminho físico.", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                AddLog("Operação cancelada pelo usuário (UAC).");
            }
            catch (Exception ex)
            {
                AddLog($"Erro: {ex.Message}");
                MessageBox.Show($"Erro ao salvar path: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnAbrirIISManager_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "inetmgr.exe",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao abrir o Gerenciador IIS: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnSalvarRewrite_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedSite == null) return;

            string? webConfigPath = GetSelectedWebConfigPath();
            if (string.IsNullOrEmpty(webConfigPath))
            {
                MessageBox.Show("Selecione um web.config na lista", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (!File.Exists(webConfigPath))
                {
                    MessageBox.Show("web.config não encontrado neste caminho.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Read current web.config
                var doc = XDocument.Load(webConfigPath);
                var existingRewrite = doc.Descendants("rewrite").FirstOrDefault();

                // Parse the new rewrite XML from the textbox
                string newRewriteXml = txtUrlRewrite.Text.Trim();
                if (string.IsNullOrEmpty(newRewriteXml))
                {
                    // Remove rewrite section
                    existingRewrite?.Remove();
                }
                else
                {
                    var newRewrite = XElement.Parse(newRewriteXml);
                    if (existingRewrite != null)
                    {
                        existingRewrite.ReplaceWith(newRewrite);
                    }
                    else
                    {
                        // Find system.webServer and add rewrite under it
                        var sysWebServer = doc.Descendants("system.webServer").FirstOrDefault();
                        if (sysWebServer != null)
                        {
                            sysWebServer.Add(newRewrite);
                        }
                        else
                        {
                            MessageBox.Show("Seção <system.webServer> não encontrada no web.config.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                    }
                }

                doc.Save(webConfigPath);
                AddLog("URL Rewrite salvo com sucesso.");
                MessageBox.Show("URL Rewrite atualizado com sucesso!", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AddLog($"Erro ao salvar URL Rewrite: {ex.Message}");
                MessageBox.Show($"Erro ao salvar URL Rewrite: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddLog(string message)
        {
            // Simple diagnostic output
            Debug.WriteLine($"[IISConfig] {message}");
        }
    }
}
