using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace RM_Core
{
    public partial class WizardWindow : Window
    {
        // Resultado final do wizard (consumido pela MainWindow)
        public string SelectedInstallPath { get; private set; } = string.Empty;
        public string ClientName { get; private set; } = string.Empty;
        public string ClientVersion { get; private set; } = "12.1.2602";
        public string BaseName { get; private set; } = "";
        public string BaseServer { get; private set; } = string.Empty;
        public string BaseDbUser { get; private set; } = string.Empty;
        public string BaseDbPass { get; private set; } = string.Empty;
        public string BaseProvider { get; private set; } = "sql"; // "sql" | "oracle"
        public bool CloseToTray { get; private set; } = true;
        public bool StartWithWindows { get; private set; } = false;
        public bool StartMinimized { get; private set; } = false;
        public bool WizardCompleted { get; private set; } = false;

        private int _currentPage = 0; // 0..5
        private readonly Grid[] _pages;
        private readonly (string title, string subtitle)[] _steps = new[]
        {
            ("Bem-vindo",                "Vamos configurar o RM Core em alguns passos"),
            ("Local de instalação",      "Onde está instalado o RM na sua máquina"),
            ("Cliente padrão",           "Crie o primeiro cliente"),
            ("Base padrão",              "Configure a primeira conexão de banco"),
            ("Comportamento",            "Como o app deve se comportar"),
            ("Pronto",                   "Revise e conclua"),
        };

        public WizardWindow()
        {
            InitializeComponent();

            _pages = new[] { pageWelcome, pageInstall, pageClient, pageBase, pageBehavior, pageDone };

            DetectInstallPaths();
            LoadVersionsForClient();
            ShowPage(0);
        }

        // ---------------------------------------------------------------
        // Navegação
        // ---------------------------------------------------------------

        private void ShowPage(int index)
        {
            if (index < 0) index = 0;
            if (index >= _pages.Length) index = _pages.Length - 1;
            _currentPage = index;

            for (int i = 0; i < _pages.Length; i++)
            {
                _pages[i].Visibility = (i == index) ? Visibility.Visible : Visibility.Collapsed;
            }

            txtStepTitle.Text    = _steps[index].title;
            txtStepSubtitle.Text = _steps[index].subtitle;
            txtStepCounter.Text  = $"{index + 1}/{_pages.Length}";

            btnBack.IsEnabled = index > 0 && index < _pages.Length - 1;
            btnSkip.Visibility = (index == 0) ? Visibility.Visible : Visibility.Collapsed;

            if (index == _pages.Length - 1)
            {
                btnNext.Content = "Concluir";
                UpdateSummary();
            }
            else
            {
                btnNext.Content = "Avançar";
            }
        }

        private void btnNext_Click(object sender, RoutedEventArgs e)
        {
            // Coleta dados ANTES de validar, pra validacao usar valores atuais
            CollectCurrentPageData();

            if (_currentPage < _pages.Length - 1)
            {
                if (!ValidateCurrentPage()) return;
                ShowPage(_currentPage + 1);
            }
            else
            {
                // Concluir
                WizardCompleted = true;
                DialogResult = true;
                Close();
            }
        }

        private void btnBack_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 0) ShowPage(_currentPage - 1);
        }

        private void btnSkip_Click(object sender, RoutedEventArgs e)
        {
            var r = MessageBox.Show(
                "Tem certeza que quer pular? O wizard vai aparecer de novo no próximo login até ser concluído.",
                "Pular configuração",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (r == MessageBoxResult.Yes)
            {
                WizardCompleted = false;
                DialogResult = false;
                Close();
            }
        }

        // ---------------------------------------------------------------
        // Validação + coleta de dados
        // ---------------------------------------------------------------

        private bool ValidateCurrentPage()
        {
            switch (_currentPage)
            {
                case 1: // Install
                    if (string.IsNullOrWhiteSpace(SelectedInstallPath))
                    {
                        MessageBox.Show("Selecione (ou procure) a pasta de instalação do RM.", "Atenção", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                    if (!Directory.Exists(SelectedInstallPath))
                    {
                        MessageBox.Show($"A pasta selecionada não existe:\n{SelectedInstallPath}", "Atenção", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                    return true;

                case 2: // Client
                    if (string.IsNullOrWhiteSpace(txtClientName.Text))
                    {
                        MessageBox.Show("Digite um nome para o cliente.", "Atenção", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                    return true;

                case 3: // Base
                    if (string.IsNullOrWhiteSpace(txtBaseServer.Text))
                    {
                        MessageBox.Show("Digite o servidor (host:porta).", "Atenção", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                    return true;

                default:
                    return true;
            }
        }

        private void CollectCurrentPageData()
        {
            switch (_currentPage)
            {
                case 1: // Install
                    SelectedInstallPath = txtSelectedPath.Tag as string ?? string.Empty;
                    break;

                case 2: // Client
                    ClientName    = txtClientName.Text.Trim();
                    ClientVersion = (cbClientVersion.SelectedItem?.ToString() ?? cbClientVersion.Text ?? "12.1.2602").Trim();
                    break;

                case 3: // Base
                    BaseName     = (txtBaseName.Text ?? "CorporeRM").Trim();
                    BaseServer   = txtBaseServer.Text.Trim();
                    BaseDbUser   = txtBaseDbUser.Text.Trim();
                    BaseDbPass   = pbBaseDbPass.Password;
                    BaseProvider = (rbOracleWizard.IsChecked == true) ? "oracle" : "sql";
                    break;

                case 4: // Behavior
                    CloseToTray       = tsWizCloseToTray.IsOn;
                    StartWithWindows  = tsWizStartWithWindows.IsOn;
                    StartMinimized    = tsWizStartMinimized.IsOn;
                    break;
            }
        }

        // ---------------------------------------------------------------
        // Página 2: detecção automática de paths
        // ---------------------------------------------------------------

        private void DetectInstallPaths()
        {
            lstInstallPaths.Items.Clear();

            // 1) C:\RM\Legado\*\Bin
            try
            {
                string legado = @"C:\RM\Legado";
                if (Directory.Exists(legado))
                {
                    foreach (var dir in Directory.GetDirectories(legado))
                    {
                        string bin = System.IO.Path.Combine(dir, "Bin");
                        if (Directory.Exists(bin))
                        {
                            lstInstallPaths.Items.Add(bin);
                        }
                    }
                }
            }
            catch { /* ignore */ }

            // 2) C:\totvs\CorporeRM\RM.Net (legado)
            string corpPath = @"C:\totvs\CorporeRM\RM.Net";
            if (Directory.Exists(corpPath))
            {
                if (!lstInstallPaths.Items.Contains(corpPath))
                    lstInstallPaths.Items.Add(corpPath);
            }

            if (lstInstallPaths.Items.Count > 0)
            {
                lstInstallPaths.SelectedIndex = 0;
            }
        }

        private void lstInstallPaths_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstInstallPaths.SelectedItem != null)
            {
                string path = lstInstallPaths.SelectedItem.ToString() ?? string.Empty;
                txtSelectedPath.Text = path;
                txtSelectedPath.Tag = path;
            }
        }

        private void btnBrowseInstall_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog
            {
                Title = "Selecione a pasta de instalação (ex: 12.1.2602\\Bin ou RM.Net)"
            };
            if (dlg.ShowDialog(this) == true)
            {
                string path = dlg.FolderName;
                if (!lstInstallPaths.Items.Contains(path))
                    lstInstallPaths.Items.Add(path);
                lstInstallPaths.SelectedItem = path;
            }
        }

        // ---------------------------------------------------------------
        // Página 3: versões disponíveis
        // ---------------------------------------------------------------

        private void LoadVersionsForClient()
        {
            var versions = new List<string>();
            try
            {
                string legado = @"C:\RM\Legado";
                if (Directory.Exists(legado))
                {
                    foreach (var dir in Directory.GetDirectories(legado))
                    {
                        string name = new DirectoryInfo(dir).Name;
                        if (!name.Equals("bin", StringComparison.OrdinalIgnoreCase))
                            versions.Add(name);
                    }
                }
            }
            catch { /* ignore */ }

            if (versions.Count == 0)
            {
                versions.AddRange(new[] { "12.1.2402", "12.1.2406", "12.1.2502", "12.1.2602" });
            }
            else
            {
                versions.Sort();
            }

            cbClientVersion.Items.Clear();
            foreach (var v in versions) cbClientVersion.Items.Add(v);
            cbClientVersion.SelectedIndex = 0;
            cbClientVersion.Text = cbClientVersion.SelectedItem?.ToString() ?? "12.1.2602";
        }

        // ---------------------------------------------------------------
        // Página 4: provider radio toggle
        // ---------------------------------------------------------------

        private void rbDbProviderWizard_Checked(object sender, RoutedEventArgs e)
        {
            // Hook vazio por enquanto — lblDbBaseName do MainWindow não está aqui
        }

        // ---------------------------------------------------------------
        // Página 6: resumo
        // ---------------------------------------------------------------

        private void UpdateSummary()
        {
            CollectCurrentPageData();
            var lines = new List<string>
            {
                $"• Instalação: {SelectedInstallPath}",
                $"• Cliente: {ClientName} (v{ClientVersion})",
                $"• Base: {BaseName} @ {BaseServer} [{(BaseProvider == "oracle" ? "Oracle" : "SQL Server")}]",
            };
            txtSummary.Text = string.Join("\n", lines);
        }

        // Bloqueia fechar pelo X — usuário tem que usar Voltar/Pular/Concluir
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!WizardCompleted)
            {
                var r = MessageBox.Show(
                    "Sair sem concluir? O wizard vai aparecer de novo no próximo login.",
                    "Sair do wizard",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (r == MessageBoxResult.No)
                {
                    e.Cancel = true;
                }
                else
                {
                    DialogResult = false;
                }
            }
            base.OnClosing(e);
        }
    }
}
