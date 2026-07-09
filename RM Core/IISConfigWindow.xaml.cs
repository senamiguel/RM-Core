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

                var psi = new ProcessStartInfo
                {
                    FileName = appCmdPath,
                    Arguments = "list sites /xml",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return;

                string xmlOutput = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();

                if (string.IsNullOrEmpty(xmlOutput)) return;

                var doc = XDocument.Parse(xmlOutput);
                foreach (var siteEl in doc.Descendants("site"))
                {
                    var site = new SiteInfo
                    {
                        Name = siteEl.Attribute("name")?.Value ?? "(sem nome)",
                        Id = siteEl.Attribute("id")?.Value ?? "0",
                    };

                    // Get the physical path for the root application (or first app)
                    var appEl = siteEl.Descendants("application").FirstOrDefault();
                    if (appEl != null)
                    {
                        var vDirEl = appEl.Descendants("virtualDirectory").FirstOrDefault();
                        if (vDirEl != null)
                        {
                            site.PhysicalPath = vDirEl.Attribute("physicalPath")?.Value ?? "";
                            site.AppName = appEl.Attribute("path")?.Value ?? "/";
                        }
                    }

                    // Determine web.config directory - use the physical path
                    string path = site.PhysicalPath;
                    if (!string.IsNullOrEmpty(path))
                    {
                        // Remove environment variables like %SystemDrive%
                        path = Environment.ExpandEnvironmentVariables(path);
                        site.WebConfigDir = path;
                    }

                    _sites.Add(site);
                }

                lstSites.Items.Clear();
                foreach (var s in _sites)
                {
                    lstSites.Items.Add(s.Name);
                }

                if (_sites.Count > 0)
                    lstSites.SelectedIndex = 0;

                AddLog($"{_sites.Count} site(s) carregado(s).");
            }
            catch (Exception ex)
            {
                AddLog($"Erro ao carregar sites: {ex.Message}");
            }
        }

        private void lstSites_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstSites.SelectedIndex < 0 || lstSites.SelectedIndex >= _sites.Count) return;
            _selectedSite = _sites[lstSites.SelectedIndex];

            txtSiteName.Text = _selectedSite.Name;
            txtPhysicalPath.Text = _selectedSite.PhysicalPath;

            LoadUrlRewrite();
        }

        private void LoadUrlRewrite()
        {
            txtUrlRewrite.Text = string.Empty;
            if (_selectedSite == null) return;

            try
            {
                string dir = _selectedSite.WebConfigDir;
                if (string.IsNullOrEmpty(dir)) return;

                string expanded = Environment.ExpandEnvironmentVariables(dir);
                string webConfigPath = Path.Combine(expanded, "web.config");

                if (!File.Exists(webConfigPath)) return;

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
                    Arguments = $"/c \"\"{appCmdPath}\" set vdir \"{_selectedSite.Name}/\"" + " /physicalPath:\"" + newPath + "\"",
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

        private void btnSalvarRewrite_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedSite == null) return;

            try
            {
                string dir = _selectedSite.WebConfigDir;
                if (string.IsNullOrEmpty(dir)) return;

                string expanded = Environment.ExpandEnvironmentVariables(dir);
                string webConfigPath = Path.Combine(expanded, "web.config");

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
