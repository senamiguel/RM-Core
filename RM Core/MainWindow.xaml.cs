using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Linq;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using RM_Core.Data;
using RM_Core.Data.Models;
using RM_Core.Services;

namespace RM_Core
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private System.Collections.ObjectModel.ObservableCollection<LogEntry> logs = new System.Collections.ObjectModel.ObservableCollection<LogEntry>();

        private System.Collections.Generic.Dictionary<string, ProfileSettings> profiles = new System.Collections.Generic.Dictionary<string, ProfileSettings>();
        private string profilesFilePath = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "RM_Core", "profiles.json");
        private string aliasesFilePath = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "RM_Core", "aliases.json");
        private System.Collections.ObjectModel.ObservableCollection<AliasConfig> aliases = new System.Collections.ObjectModel.ObservableCollection<AliasConfig>();
        private System.Collections.ObjectModel.ObservableCollection<AliasConfig> filteredAliases = new System.Collections.ObjectModel.ObservableCollection<AliasConfig>();
        private bool _isSyncing = false;
        private bool _isOperationRunning = false;
        private bool _sortBasesAsc = true;
        private string _logSearchTerm = "";

        // TrayService — system tray / lifecycle management
        private TrayService _trayService = null!;

        // Pending update info (set by background check on startup)
        private UpdateInfo? _pendingUpdate;
        private bool _updateCheckDone = false;
        private bool _updateCheckFailed = false;

        // Window state persistence path
        private readonly string _windowSettingsPath = System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "RM_Core", "window_settings.json");

        // App settings (toggle persistido em JSON)
        private readonly string _appSettingsPath = System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "RM_Core", "app_settings.json");
        private AppSettings _appSettings = new AppSettings();

        public bool IsExiting { get; set; } = false;

        public MainWindow()
        {
            InitializeComponent();
            listLogs.ItemsSource = logs;
            logs.CollectionChanged += Logs_CollectionChanged;
            InitializeSelectors();
            LoadAliases();
            InitializeBasesTab();
            LoadProfiles();

            // Initialize tray service (must come after InitializeComponent)
            _trayService = new TrayService(this);

            // Background update check — never blocks the UI thread
            _ = Task.Run(CheckForUpdatesAsync);

            // Restore window position/size from persisted settings
            Loaded += MainWindow_Loaded;

            // Populate service status on startup
            AtualizarStatusServicos();

            // Set version text dynamically to reference the control
            txtVersaoApp.Text = "Versão 1.0.0";
        }

        private void InitializeSelectors()
        {
            // Populate RM Versions
            cbVersaoRM.Items.Clear();
            var versions = GetRmVersions();
            foreach (var v in versions)
            {
                cbVersaoRM.Items.Add(v);
            }
        }

        private System.Collections.Generic.List<string> GetRmVersions()
        {
            var versions = new System.Collections.Generic.List<string>();
            string path = @"C:\RM\Legado";
            try
            {
                if (System.IO.Directory.Exists(path))
                {
                    var dirs = System.IO.Directory.GetDirectories(path);
                    foreach (var dir in dirs)
                    {
                        string name = System.IO.Path.GetFileName(dir);
                        if (!name.Equals("bin", StringComparison.OrdinalIgnoreCase))
                        {
                            versions.Add(name);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog("error", $"Erro ao listar versões do RM Legado: {ex.Message}");
            }

            if (versions.Count == 0)
            {
                versions.Add("12.1.2402");
                versions.Add("12.1.2406");
                versions.Add("12.1.2502");
                versions.Add("12.1.2602");
            }
            else
            {
                versions.Sort();
            }

            return versions;
        }

        private void LoadAliases()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    db.Database.EnsureCreated();
                    var dbAliases = db.Aliases.Include(a => a.Ambiente).ToList();
                    
                    aliases.Clear();
                    foreach (var dbAl in dbAliases)
                    {
                        string clientName = dbAl.Ambiente?.Nome ?? "Importado";
                        aliases.Add(new AliasConfig
                        {
                            id = dbAl.Id.ToString(),
                            name = dbAl.Nome,
                            Base = dbAl.BaseName,
                            client = clientName,
                            server = dbAl.Servidor,
                            dbType = dbAl.DbType,
                            dbUser = dbAl.DbUser,
                            dbPass = dbAl.DbPass,
                            rmUser = dbAl.Usuario,
                            rmPass = dbAl.Senha,
                            runService = dbAl.RunService,
                            jobProcessing = dbAl.JobServerEnabled,
                            localOnly = dbAl.JobServerLocalOnly,
                            processPool = dbAl.JobServerProcessPoolEnabled,
                            maxThreads = dbAl.JobServerMaxThreads,
                            dbVersion = dbAl.Sgbd
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog("error", $"Erro ao carregar aliases: {ex.Message}");
            }

            // Fallback mock aliases if empty
            if (aliases.Count == 0)
            {
                aliases.Add(new AliasConfig 
                { 
                    id = "1782763422741", 
                    name = "Desenvolvimento", 
                    Base = "CorporeRM", 
                    client = "Cliente Padrão",
                    server = "localhost", 
                    dbType = "sql", 
                    dbUser = "sa", 
                    dbPass = "sa", 
                    rmUser = "mestre", 
                    rmPass = "totvs",
                    dbVersion = "12.1.2602"
                });
                aliases.Add(new AliasConfig 
                { 
                    id = "1782763422742", 
                    name = "Produção", 
                    Base = "CorporeRM", 
                    client = "Cliente Padrão",
                    server = "localhost", 
                    dbType = "sql", 
                    dbUser = "sa", 
                    dbPass = "sa", 
                    rmUser = "mestre", 
                    rmPass = "totvs",
                    dbVersion = "12.1.2602"
                });
                aliases.Add(new AliasConfig 
                { 
                    id = "1782763422743", 
                    name = "Homologação", 
                    Base = "CorporeRM", 
                    client = "Desenvolvimento Local",
                    server = "localhost", 
                    dbType = "sql", 
                    dbUser = "sa", 
                    dbPass = "totvs", 
                    rmUser = "mestre", 
                    rmPass = "totvs",
                    dbVersion = "12.1.2402"
                });
                SaveAliases();
            }
        }

        private void SaveAliases()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    db.Database.EnsureCreated();
                    
                    var dbAliases = db.Aliases.ToList();
                    var dbAmbientes = db.Ambientes.ToList();
                    
                    // Delete aliases in db that are no longer in the list
                    foreach (var dbAl in dbAliases)
                    {
                        if (!aliases.Any(a => a.id == dbAl.Id.ToString()))
                        {
                            db.Aliases.Remove(dbAl);
                        }
                    }
                    
                    // Add/update aliases
                    foreach (var alias in aliases)
                    {
                        // find associated Ambiente by name
                        var amb = dbAmbientes.FirstOrDefault(a => a.Nome == alias.client);
                        if (amb == null)
                        {
                            amb = new Ambiente
                            {
                                Nome = alias.client,
                                FullName = alias.client,
                                Unidade = @"C:\totvs\CorporeRM\RM.Net",
                                RmVersion = alias.dbVersion
                            };
                            db.Ambientes.Add(amb);
                            db.SaveChanges(); // get Id
                            dbAmbientes.Add(amb);
                        }
                        
                        int aliasId = 0;
                        if (alias.id != null && alias.id.Length < 10)
                        {
                            int.TryParse(alias.id, out aliasId);
                        }
                        
                        var existing = dbAliases.FirstOrDefault(a => a.Id == aliasId && aliasId != 0);
                        if (existing != null)
                        {
                            existing.Nome = alias.name;
                            existing.BaseName = alias.Base;
                            existing.AmbienteId = amb.Id;
                            existing.Servidor = alias.server;
                            existing.DbServer = alias.server;
                            existing.DbName = alias.Base;
                            existing.DbType = alias.dbType;
                            existing.DbUser = alias.dbUser;
                            existing.DbPass = alias.dbPass;
                            existing.Usuario = alias.rmUser;
                            existing.Senha = alias.rmPass;
                            existing.RunService = alias.runService;
                            existing.JobServerEnabled = alias.jobProcessing;
                            existing.JobServerLocalOnly = alias.localOnly;
                            existing.JobServerProcessPoolEnabled = alias.processPool;
                            existing.JobServerMaxThreads = alias.maxThreads;
                            existing.Sgbd = alias.dbVersion;
                        }
                        else
                        {
                            var newAlias = new AliasModel
                            {
                                Nome = alias.name,
                                BaseName = alias.Base,
                                AmbienteId = amb.Id,
                                Servidor = alias.server,
                                DbServer = alias.server,
                                DbName = alias.Base,
                                DbType = alias.dbType,
                                DbUser = alias.dbUser,
                                DbPass = alias.dbPass,
                                Usuario = alias.rmUser,
                                Senha = alias.rmPass,
                                RunService = alias.runService,
                                JobServerEnabled = alias.jobProcessing,
                                JobServerLocalOnly = alias.localOnly,
                                JobServerProcessPoolEnabled = alias.processPool,
                                JobServerMaxThreads = alias.maxThreads,
                                Sgbd = alias.dbVersion
                            };
                            db.Aliases.Add(newAlias);
                            db.SaveChanges();
                            alias.id = newAlias.Id.ToString();
                        }
                    }
                    db.SaveChanges();
                }
            }
            catch (Exception ex)
            {
                AddLog("error", $"Erro ao salvar aliases: {ex.Message}");
            }
        }

        private void UpdateAliasesUI(string clientName)
        {
            cbAliasDB.SelectionChanged -= cbAliasDB_SelectionChanged;
            cbBase.SelectionChanged -= cbBase_SelectionChanged;

            cbAliasDB.Items.Clear();
            cbBase.Items.Clear();

            string targetVersion = string.Empty;
            if (profiles.TryGetValue(clientName, out var profile))
            {
                targetVersion = profile.RmVersion;
            }

            var matched = new List<AliasConfig>();
            var unmatched = new List<AliasConfig>();

            foreach (var alias in aliases)
            {
                if (alias.client.Equals(clientName, StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(targetVersion) || alias.dbVersion == targetVersion)
                        matched.Add(alias);
                    else
                        unmatched.Add(alias);
                }
            }

            // Se versao definida, SÓ mostra bases dessa versao (sem fallback pra unmatched)
            var items = matched;
            foreach (var alias in items)
            {
                cbAliasDB.Items.Add(alias.name);
                cbBase.Items.Add(alias);
            }

            cbAliasDB_SelectionChanged(sender: null!, e: null!);
            cbBase_SelectionChanged(sender: null!, e: null!);

            cbAliasDB.SelectionChanged += cbAliasDB_SelectionChanged;
            cbBase.SelectionChanged += cbBase_SelectionChanged;

            // Refresh color dots after a short delay to allow layout to complete
            Dispatcher.BeginInvoke(new Action(() => RefreshCbBaseColors()));
        }

        private void cbVersaoRM_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncing) return;
            
            string selectedVersion = cbVersaoRM.SelectedItem?.ToString() ?? string.Empty;
            string activeClient = cbPerfis.SelectedItem?.ToString() ?? cbClienteAtivo.SelectedItem?.ToString() ?? string.Empty;
            if (string.IsNullOrEmpty(activeClient)) return;

            cbAliasDB.SelectionChanged -= cbAliasDB_SelectionChanged;
            cbBase.SelectionChanged -= cbBase_SelectionChanged;

            cbAliasDB.Items.Clear();
            cbBase.Items.Clear();
            var matched = new List<AliasConfig>();
            var unmatched = new List<AliasConfig>();
            foreach (var alias in aliases)
            {
                if (alias.client.Equals(activeClient, StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(selectedVersion) || alias.dbVersion == selectedVersion)
                        matched.Add(alias);
                    else
                        unmatched.Add(alias);
                }
            }

            var items = matched;
            foreach (var alias in items)
            {
                cbAliasDB.Items.Add(alias.name);
                cbBase.Items.Add(alias);
            }



            cbAliasDB.SelectionChanged += cbAliasDB_SelectionChanged;
            cbBase.SelectionChanged += cbBase_SelectionChanged;

            Dispatcher.BeginInvoke(new Action(() => RefreshCbBaseColors()));
        }

        private void LoadProfiles()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    db.Database.EnsureCreated();
                    var ambientes = db.Ambientes.ToList();
                    var configs = db.AmbienteConfigs.ToList();
                    
                    profiles.Clear();
                    foreach (var amb in ambientes)
                    {
                        var cfg = configs.FirstOrDefault(c => c.AmbienteId == amb.Id) ?? new AmbienteConfig();
                        profiles[amb.Nome] = new ProfileSettings
                        {
                            Name = amb.Nome,
                            RmVersion = amb.RmVersion ?? "12.1.2402",
                            Alias = cfg.DefaultDB ?? "CorporeRM",
                            AutoLogin = amb.AutoLogin,
                            DelBroker = cfg.DelBroker,
                            VerboseLogs = cfg.VerboseLogs,
                            ApagarHost = cfg.ApagarHost,
                            NormalizePath = cfg.NormalizePath,
                            EnableProcessIsolation = cfg.EnableProcessIsolation,
                            JobServer3Camadas = cfg.JobServer3Camadas,
                            EnableCompression = cfg.EnableCompression
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog("error", $"Erro ao carregar clientes: {ex.Message}");
            }
            
            UpdateFilteredAliasesList();

            // Só cria clientes padrão se o wizard já rodou (senão o wizard cria)
            if (profiles.Count == 0 && _appSettings.FirstRunComplete)
            {
                profiles["Cliente Padrão"] = new ProfileSettings 
                { 
                    Name = "Cliente Padrão", 
                    RmVersion = "12.1.2602", 
                    Alias = "Desenvolvimento", 
                    AutoLogin = true, 
                    DelBroker = false, 
                    VerboseLogs = true, 
                    ApagarHost = false 
                };
                profiles["Desenvolvimento Local"] = new ProfileSettings 
                { 
                    Name = "Desenvolvimento Local", 
                    RmVersion = "12.1.2402", 
                    Alias = "Homologação", 
                    AutoLogin = false, 
                    DelBroker = true, 
                    VerboseLogs = true, 
                    ApagarHost = false 
                };
                SaveProfiles();
            }

            UpdateProfilesUI();
        }

        private void SaveProfiles()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    db.Database.EnsureCreated();
                    
                    var dbAmbientes = db.Ambientes.ToList();
                    var dbConfigs = db.AmbienteConfigs.ToList();
                    
                    // Delete environments in db that are no longer in 'profiles'
                    foreach (var dbAmb in dbAmbientes)
                    {
                        if (!profiles.ContainsKey(dbAmb.Nome))
                        {
                            db.Ambientes.Remove(dbAmb);
                        }
                    }
                    
                    // Add/update environments
                    foreach (var pair in profiles)
                    {
                        var profile = pair.Value;
                        var existing = dbAmbientes.FirstOrDefault(a => a.Nome == profile.Name);
                        if (existing != null)
                        {
                            existing.RmVersion = profile.RmVersion;
                            existing.AutoLogin = profile.AutoLogin;
                            
                            var cfg = dbConfigs.FirstOrDefault(c => c.AmbienteId == existing.Id);
                            if (cfg != null)
                            {
                                cfg.DefaultDB = profile.Alias;
                                cfg.DelBroker = profile.DelBroker;
                                cfg.VerboseLogs = profile.VerboseLogs;
                                cfg.ApagarHost = profile.ApagarHost;
                                cfg.NormalizePath = profile.NormalizePath;
                                cfg.EnableProcessIsolation = profile.EnableProcessIsolation;
                                cfg.JobServer3Camadas = profile.JobServer3Camadas;
                                cfg.EnableCompression = profile.EnableCompression;
                            }
                            else
                            {
                                db.AmbienteConfigs.Add(new AmbienteConfig
                                {
                                    AmbienteId = existing.Id,
                                    DefaultDB = profile.Alias,
                                    DelBroker = profile.DelBroker,
                                    VerboseLogs = profile.VerboseLogs,
                                    ApagarHost = profile.ApagarHost,
                                    NormalizePath = profile.NormalizePath,
                                    EnableProcessIsolation = profile.EnableProcessIsolation,
                                    JobServer3Camadas = profile.JobServer3Camadas,
                                    EnableCompression = profile.EnableCompression
                                });
                            }
                        }
                        else
                        {
                            var newAmb = new Ambiente
                            {
                                Nome = profile.Name,
                                FullName = profile.Name,
                                RmVersion = profile.RmVersion,
                                AutoLogin = profile.AutoLogin,
                                Unidade = @"C:\totvs\CorporeRM\RM.Net"
                            };
                            db.Ambientes.Add(newAmb);
                            db.SaveChanges(); // to get newAmb.Id
                            
                            db.AmbienteConfigs.Add(new AmbienteConfig
                            {
                                AmbienteId = newAmb.Id,
                                DefaultDB = profile.Alias,
                                DelBroker = profile.DelBroker,
                                VerboseLogs = profile.VerboseLogs,
                                ApagarHost = profile.ApagarHost,
                                NormalizePath = profile.NormalizePath,
                                EnableProcessIsolation = profile.EnableProcessIsolation,
                                JobServer3Camadas = profile.JobServer3Camadas,
                                EnableCompression = profile.EnableCompression
                            });
                        }
                    }
                    
                    db.SaveChanges();
                }
            }
            catch (Exception ex)
            {
                AddLog("error", $"Erro ao salvar perfis: {ex.Message}");
            }
        }

        private void UpdateProfilesUI()
        {
            cbPerfis.SelectionChanged -= cbPerfis_SelectionChanged;
            cbClienteAtivo.SelectionChanged -= cbClienteAtivo_SelectionChanged;

            cbPerfis.Items.Clear();
            cbClienteAtivo.Items.Clear();
            cbClienteAssociado.Items.Clear();

            foreach (var key in profiles.Keys)
            {
                cbPerfis.Items.Add(key);
                cbClienteAtivo.Items.Add(key);
                cbClienteAssociado.Items.Add(key);
            }

            cbPerfis.SelectionChanged += cbPerfis_SelectionChanged;
            cbClienteAtivo.SelectionChanged += cbClienteAtivo_SelectionChanged;

            UpdateDefaultIconOnSelectedClient();
            ApplyDefaultOrLast();
            RefreshDefaultSelectors();
        }

        private void LoadProfileToUI(ProfileSettings profile)
        {
            if (_isSyncing) return;
            _isSyncing = true;

            try
            {
                txtNomePerfil.Text = profile.Name;
                cbVersaoRM.SelectedItem = profile.RmVersion;

                tsAutoLogin.IsOn = profile.AutoLogin;

                tsDeletarBroker.IsOn = profile.DelBroker;
                tsVerboseLogs.IsOn = profile.VerboseLogs;
                tsApagarHost.IsOn = profile.ApagarHost;
                tsNormalizePath.IsOn = profile.NormalizePath;
                tsEnableProcessIsolation.IsOn = profile.EnableProcessIsolation;
                tsJobServer3Camadas.IsOn = profile.JobServer3Camadas;
                tsEnableCompression.IsOn = profile.EnableCompression;

                // Sync to Home tab
                cbClienteAtivo.SelectedItem = profile.Name;
                tsLimparBrokers.IsOn = profile.ApagarHost;
                tsLogsDetalhados.IsOn = profile.VerboseLogs;
                tsDeletarBrokerDat.IsOn = profile.DelBroker;

                // Dynamically update and filter bases for this client!
                UpdateAliasesUI(profile.Name);

                // Sync default icon
                UpdateFavoritoIcon(!string.IsNullOrEmpty(_appSettings.DefaultClient) && _appSettings.DefaultClient == profile.Name);

                // Set selected base
                cbAliasDB.SelectedItem = profile.Alias;
                var matchBase = aliases.FirstOrDefault(a => a.name == profile.Alias && a.client.Equals(profile.Name, StringComparison.OrdinalIgnoreCase));
                cbBase.SelectedItem = matchBase;
            }
            finally
            {
                _isSyncing = false;
            }
        }

        private void cbClienteAtivo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncing || cbClienteAtivo.SelectedItem == null) return;
            string selectedName = cbClienteAtivo.SelectedItem?.ToString() ?? string.Empty;
            if (profiles.TryGetValue(selectedName, out var profile))
            {
                cbPerfis.SelectedItem = selectedName;
                LoadProfileToUI(profile);
                UpdateFilteredAliasesList();

                AddLog("info", $"Cliente \"{selectedName}\" carregado via Início.");
            }
        }

        private void cbPerfis_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncing || cbPerfis.SelectedItem == null) return;
            string selectedName = cbPerfis.SelectedItem?.ToString() ?? string.Empty;
            if (profiles.TryGetValue(selectedName, out var profile))
            {
                cbClienteAtivo.SelectedItem = selectedName;
                LoadProfileToUI(profile);
                UpdateFilteredAliasesList();
                UpdateDefaultIconOnSelectedClient();

                if (_appSettings.LastClient != selectedName)
                {
                    _appSettings.LastClient = selectedName;
                    SaveAppSettings();
                }

                AddLog("info", $"Cliente \"{selectedName}\" selecionado.");
            }
        }

        private void cbBase_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncing || cbBase.SelectedItem == null) return;
            _isSyncing = true;
            try
            {
                string selectedBase = (cbBase.SelectedItem is AliasConfig aliasCfg) ? aliasCfg.name : (cbBase.SelectedItem?.ToString() ?? string.Empty);
                cbAliasDB.SelectedItem = selectedBase;

                if (cbBase.SelectedItem is AliasConfig alias && alias.id != _appSettings.LastBaseId)
                {
                    _appSettings.LastBaseId = alias.id;
                    SaveAppSettings();
                }

                // Update active client model settings
                if (cbClienteAtivo.SelectedItem != null)
                {
                    string activeClient = cbClienteAtivo.SelectedItem?.ToString() ?? string.Empty;
                    if (profiles.TryGetValue(activeClient, out var profile))
                    {
                        profile.Alias = selectedBase;
                        SaveProfiles();
                    }
                }
            }
            finally
            {
                _isSyncing = false;
            }
        }

        private void cbAliasDB_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncing || cbAliasDB.SelectedItem == null) return;
            _isSyncing = true;
            try
            {
                string selectedBase = cbAliasDB.SelectedItem?.ToString() ?? string.Empty;
                cbBase.SelectedItem = selectedBase;
                
                // Update active client model settings
                if (cbPerfis.SelectedItem != null)
                {
                    string activeClient = cbPerfis.SelectedItem?.ToString() ?? string.Empty;
                    if (profiles.TryGetValue(activeClient, out var profile))
                    {
                        profile.Alias = selectedBase;
                        SaveProfiles();
                    }
                }
            }
            finally
            {
                _isSyncing = false;
            }
        }

        private void btnSalvarPerfil_Click(object sender, RoutedEventArgs e)
        {
            string name = txtNomePerfil.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Por favor, digite um nome para o perfil.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var profile = new ProfileSettings
            {
                Name = name,
                RmVersion = cbVersaoRM.SelectedItem?.ToString() ?? "12.1.2402",
                Alias = cbAliasDB.SelectedItem?.ToString() ?? "CorporeRM",
                AutoLogin = tsAutoLogin.IsOn,
                DelBroker = tsDeletarBroker.IsOn,
                VerboseLogs = tsVerboseLogs.IsOn,
                ApagarHost = tsApagarHost.IsOn,
                NormalizePath = tsNormalizePath.IsOn,
                EnableProcessIsolation = tsEnableProcessIsolation.IsOn,
                JobServer3Camadas = tsJobServer3Camadas.IsOn,
                EnableCompression = tsEnableCompression.IsOn
            };

            profiles[name] = profile;
            SaveProfiles();
            
            cbPerfis.SelectionChanged -= cbPerfis_SelectionChanged;
            cbClienteAtivo.SelectionChanged -= cbClienteAtivo_SelectionChanged;
            
            cbPerfis.Items.Clear();
            cbClienteAtivo.Items.Clear();
            foreach (var key in profiles.Keys)
            {
                cbPerfis.Items.Add(key);
                cbClienteAtivo.Items.Add(key);
            }
            
            cbPerfis.SelectedItem = name;
            cbClienteAtivo.SelectedItem = name;
            
            cbPerfis.SelectionChanged += cbPerfis_SelectionChanged;
            cbClienteAtivo.SelectionChanged += cbClienteAtivo_SelectionChanged;

            UpdateFilteredAliasesList();


            AddLog("info", $"Cliente \"{name}\" salvo com sucesso.");
            MessageBox.Show($"Cliente \"{name}\" salvo com sucesso!", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
            txtNomePerfil.Focus();
        }

        private void btnNovoPerfil_Click(object sender, RoutedEventArgs e)
        {
            _isSyncing = true;
            try
            {
                cbPerfis.SelectionChanged -= cbPerfis_SelectionChanged;
                cbClienteAtivo.SelectionChanged -= cbClienteAtivo_SelectionChanged;
                try
                {
                    cbPerfis.SelectedItem = null;
                    cbClienteAtivo.SelectedItem = null;
                }
                finally
                {
                    cbPerfis.SelectionChanged += cbPerfis_SelectionChanged;
                    cbClienteAtivo.SelectionChanged += cbClienteAtivo_SelectionChanged;
                }

                txtNomePerfil.Text = string.Empty;
                txtNomePerfil.Focus();
                if (cbVersaoRM.Items.Count > 0) cbVersaoRM.SelectedIndex = 0;
                cbAliasDB.SelectionChanged -= cbAliasDB_SelectionChanged;
                cbBase.SelectionChanged -= cbBase_SelectionChanged;
                try
                {
                    cbAliasDB.Items.Clear();
                    cbBase.Items.Clear();
                    cbAliasDB.SelectedItem = null;
                    cbBase.SelectedItem = null;
                }
                finally
                {
                    cbAliasDB.SelectionChanged += cbAliasDB_SelectionChanged;
                    cbBase.SelectionChanged += cbBase_SelectionChanged;
                }
                tsAutoLogin.IsOn = true;
                tsDeletarBroker.IsOn = false;
                tsVerboseLogs.IsOn = true;
                tsApagarHost.IsOn = false;
                tsNormalizePath.IsOn = false;
                tsEnableProcessIsolation.IsOn = false;
                tsJobServer3Camadas.IsOn = false;
                tsEnableCompression.IsOn = false;

                tsLimparBrokers.IsOn = false;
                tsLogsDetalhados.IsOn = true;
                tsDeletarBrokerDat.IsOn = false;

                UpdateFavoritoIcon(false);

                AddLog("info", "Preparado para cadastrar novo cliente. Digite o nome e clique em Salvar.");
            }
            finally
            {
                _isSyncing = false;
            }
        }

        private void btnDeletarPerfil_Click(object sender, RoutedEventArgs e)
        {
            if (cbPerfis.SelectedItem == null) return;
            string selectedName = cbPerfis.SelectedItem?.ToString() ?? string.Empty;
            
            var result = MessageBox.Show($"Deseja realmente excluir o cliente \"{selectedName}\"?", "Confirmar Exclusão", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.No) return;

            profiles.Remove(selectedName);
            SaveProfiles();
            
            AddLog("info", $"Cliente \"{selectedName}\" removido.");

            UpdateProfilesUI();
            UpdateFilteredAliasesList();

            
            cbClienteAtivo.SelectionChanged -= cbClienteAtivo_SelectionChanged;
            cbClienteAtivo.Items.Clear();
            foreach (var key in profiles.Keys)
            {
                cbClienteAtivo.Items.Add(key);
            }
            if (cbClienteAtivo.Items.Count > 0) cbClienteAtivo.SelectedIndex = 0;
            cbClienteAtivo.SelectionChanged += cbClienteAtivo_SelectionChanged;
            
            if (cbPerfis.SelectedItem != null)
            {
                string newSelected = cbPerfis.SelectedItem?.ToString() ?? string.Empty;
                if (profiles.TryGetValue(newSelected, out var profile))
                {
                    LoadProfileToUI(profile);
                }
            }
            
            MessageBox.Show($"Cliente \"{selectedName}\" excluído.", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Logs_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            txtNenhumLog.Visibility = logs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            scrollLogs.Visibility = logs.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (logs.Count > 0)
            {
                Dispatcher.BeginInvoke(new Action(() => scrollLogs.ScrollToEnd()));
            }
        }

        private void AddLog(string type, string message)
        {
            // Logs Detalhados OFF → só mostra erros/avisos (ignora info/stdout)
            if (tsVerboseLogs != null && !tsVerboseLogs.IsOn && type != "error" && type != "warn" && type != "stderr")
                return;

            logs.Add(new LogEntry
            {
                Time = DateTime.Now,
                Type = type,
                Message = message
            });
            if (logs.Count > 1000)
            {
                logs.RemoveAt(0);
            }

            // Persist to file — silently ignore any I/O errors
            try
            {
                string logDir  = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RM_Core");
                Directory.CreateDirectory(logDir);
                string logPath = Path.Combine(logDir, "logs.txt");
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{type}] {message}{Environment.NewLine}");
            }
            catch { /* silently ignore */ }
        }

        private void btnCopiarLogs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (logs == null || logs.Count == 0)
                {
                    AddLog("warn", "Não há logs para copiar.");
                    return;
                }
                var text = string.Join(Environment.NewLine, logs.Select(l => $"[{l.Time:yyyy-MM-dd HH:mm:ss}] [{l.Type.ToUpper()}] {l.Message}"));
                Clipboard.SetText(text);
                AddLog("info", "Logs copiados para a área de transferência.");
            }
            catch (Exception ex)
            {
                AddLog("error", $"Erro ao copiar logs: {ex.Message}");
            }
        }

        private void btnLimparLogs_Click(object sender, RoutedEventArgs e)
        {
            logs.Clear();
            AddLog("info", "Logs limpos.");
        }

        private void txtFiltrarLogs_TextChanged(object sender, TextChangedEventArgs e)
        {
            _logSearchTerm = txtFiltrarLogs.Text.Trim().ToLower();
            btnLimparFiltroLogs.Visibility = string.IsNullOrEmpty(_logSearchTerm) ? Visibility.Collapsed : Visibility.Visible;
            AplicarFiltroLogs();
        }

        private void AplicarFiltroLogs()
        {
            listLogs.ItemsSource = null;
            if (string.IsNullOrEmpty(_logSearchTerm))
            {
                listLogs.ItemsSource = logs;
            }
            else
            {
                listLogs.ItemsSource = logs.Where(l =>
                    l.Message.ToLower().Contains(_logSearchTerm) ||
                    l.Type.ToLower().Contains(_logSearchTerm)).ToList();
            }
        }

        private void btnLimparFiltroLogs_Click(object sender, RoutedEventArgs e)
        {
            txtFiltrarLogs.Text = "";
        }

        private async void btnIniciarCompleto_Click(object sender, RoutedEventArgs e)
        {
            await IniciarRMPlusHostAsync();
        }

        public async Task IniciarRMPlusHostAsync()
        {
            if (_isOperationRunning) return;
            SetLoadingState(true);
            try
            {
                AddLog("info", "Iniciando RM + Host Principal...");

                StartHostPrincipal();
                string binDir = GetBinDirectory();
                int hostPort = GetHostPortFromConfig(binDir);
                AddLog("info", "Host Principal iniciado.");
                await WaitForHostPortAsync(hostPort, 15000);
                await WaitForAuthenticationAsync(binDir, hostPort, 30000);
                await StartRMAsync(checkHost: false);
                AddLog("info", "Processo RM iniciado.");
            }
            catch (Exception ex)
            {
                AddLog("error", $"Erro ao iniciar ambiente: {ex.Message}");
                MessageBox.Show($"Erro ao iniciar ambiente: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetLoadingState(false);
                AtualizarStatusServicos();
            }
        }

        private void btnIniciarDropdown_Click(object sender, RoutedEventArgs e)
        {
            if (btnIniciarDropdown.ContextMenu != null)
            {
                btnIniciarDropdown.ContextMenu.PlacementTarget = btnIniciarDropdown;
                btnIniciarDropdown.ContextMenu.IsOpen = true;
            }
        }

        private async void menuIniciarRM_Click(object sender, RoutedEventArgs e)
        {
            if (_isOperationRunning) return;
            SetLoadingState(true);
            try
            {
                AddLog("info", "Iniciando RM App...");
                await StartRMAsync(checkHost: true);
                AddLog("info", "Processo RM iniciado.");
            }
            catch (Exception ex)
            {
                AddLog("error", $"Erro ao iniciar RM: {ex.Message}");
                MessageBox.Show($"Erro ao iniciar RM: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetLoadingState(false);
                AtualizarStatusServicos();
            }
        }

        private void menuIniciarHostPrincipal_Click(object sender, RoutedEventArgs e)
        {
            if (_isOperationRunning) return;
            SetLoadingState(true);
            try
            {
                AddLog("info", "Iniciando Host Principal...");
                StartHostPrincipal();
                AddLog("info", "Host Principal iniciado.");
            }
            catch (Exception ex)
            {
                AddLog("error", $"Erro ao iniciar Host Principal: {ex.Message}");
                MessageBox.Show($"Erro ao iniciar Host Principal: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetLoadingState(false);
                AtualizarStatusServicos();
            }
        }

        private void menuIniciarHost2_Click(object sender, RoutedEventArgs e)
        {
            if (_isOperationRunning) return;
            SetLoadingState(true);
            try
            {
                AddLog("info", "Iniciando Host 2...");
                StartHost2();
                AddLog("info", "Host 2 iniciado.");
            }
            catch (Exception ex)
            {
                AddLog("error", $"Erro ao iniciar Host 2: {ex.Message}");
                MessageBox.Show($"Erro ao iniciar Host 2: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetLoadingState(false);
                AtualizarStatusServicos();
            }
        }

        private void menuIniciarPortalAluno_Click(object sender, RoutedEventArgs e)
        {
            if (_isOperationRunning) return;
            SetLoadingState(true);
            try
            {
                AddLog("info", "Abrindo Portal do Aluno...");
                StartPortalAluno();
                AddLog("info", "Portal do Aluno aberto.");
            }
            catch (Exception ex)
            {
                AddLog("error", $"Erro ao abrir Portal do Aluno: {ex.Message}");
                MessageBox.Show($"Erro ao abrir Portal do Aluno: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetLoadingState(false);
                AtualizarStatusServicos();
            }
        }

        private void btnDerrubarTudo_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show("Deseja realmente derrubar todos os processos relacionados ao RM em execução?", "Confirmar Operação", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm == MessageBoxResult.No) return;

            try
            {
                AddLog("info", "Finalizando todos os processos...");
                // Stop services
                StopHostPrincipal();
                AddLog("info", "Serviço Host Principal finalizado.");
                StopHost2();
                AddLog("info", "Serviço Host 2 finalizado.");
                StopPortalAluno();

                // Force kill all related processes
                KillAllProcesses();
                AddLog("info", "Todos os processos relacionados foram finalizados.");
                
                MessageBox.Show("Todos os processos foram finalizados com sucesso.", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AddLog("error", $"Erro ao derrubar processos: {ex.Message}");
                MessageBox.Show($"Erro ao derrubar processos: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                AtualizarStatusServicos();
            }
        }

        private void btnBin_Click(object sender, RoutedEventArgs e)
        {
            string binDir = GetBinDirectory();
            if (string.IsNullOrEmpty(binDir))
            {
                AddLog("error", "Pasta de instalação do RM não configurada. Rode o wizard pela aba Sobre.");
                MessageBox.Show("A pasta de instalação do RM não foi encontrada.\n\nAbra a aba Sobre e clique em 'Reconfigurar' (wizard) para apontar a pasta correta.", "RM não encontrado", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            AddLog("info", $"Abrindo pasta BIN ({binDir})...");
            OpenFolder(binDir);
        }

        private void btnCustom_Click(object sender, RoutedEventArgs e)
        {
            string binDir = GetBinDirectory();
            if (string.IsNullOrEmpty(binDir))
            {
                AddLog("error", "Pasta de instalação do RM não configurada. Rode o wizard pela aba Sobre.");
                MessageBox.Show("A pasta de instalação do RM não foi encontrada.\n\nAbra a aba Sobre e clique em 'Reconfigurar' (wizard) para apontar a pasta correta.", "RM não encontrado", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            string customPath = Path.Combine(binDir, "Custom");
            AddLog("info", $"Abrindo pasta Custom ({customPath})...");
            OpenFolder(customPath);
        }

        private void ts_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isSyncing) return;
            if (cbClienteAtivo == null || cbPerfis == null || tsLimparBrokers == null || tsLogsDetalhados == null || tsApagarHost == null || tsVerboseLogs == null || tsDeletarBrokerDat == null) return;

            string activeClient = cbClienteAtivo.SelectedItem?.ToString() ?? cbPerfis.SelectedItem?.ToString() ?? string.Empty;
            if (string.IsNullOrEmpty(activeClient)) return;

            if (profiles.TryGetValue(activeClient, out var profile))
            {
                profile.ApagarHost = tsLimparBrokers.IsOn;
                profile.VerboseLogs = tsLogsDetalhados.IsOn;
                profile.DelBroker  = tsDeletarBrokerDat.IsOn;

                _isSyncing = true;
                try
                {
                    tsApagarHost.IsOn     = profile.ApagarHost;
                    tsVerboseLogs.IsOn    = profile.VerboseLogs;
                    tsDeletarBroker.IsOn  = profile.DelBroker;
                }
                finally
                {
                    _isSyncing = false;
                }

                SaveProfiles();
                AddLog("info", $"Configurações rápidas salvas para o perfil \"{activeClient}\".");
            }
        }

        private void btnReconfigurar_Click(object sender, RoutedEventArgs e)
        {
            RunFirstRunWizard(fromButton: true);
        }

        private void tsAppSetting_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isSyncing) return;
            if (tsCloseMinimizesToTray == null || tsStartWithWindows == null || tsStartMinimized == null) return;

            _appSettings.CloseMinimizesToTray = tsCloseMinimizesToTray.IsOn;
            _appSettings.StartWithWindows     = tsStartWithWindows.IsOn;
            _appSettings.StartMinimized       = tsStartMinimized.IsOn;

            SaveAppSettings();
            ApplyStartWithWindowsSetting(_appSettings.StartWithWindows);
            AddLog("info", $"Configurações atualizadas. (Fechar→Tray: {_appSettings.CloseMinimizesToTray}, Iniciar com Windows: {_appSettings.StartWithWindows}, Minimizado: {_appSettings.StartMinimized})");
        }

        private void ApplyStartWithWindowsSetting(bool enable)
        {
            try
            {
                const string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
                const string valueName = "RMCore";
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(runKey, writable: true);
                if (key == null) return;
                if (enable)
                {
                    string exePath = Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
                    if (exePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        exePath = exePath.Substring(0, exePath.Length - 4) + ".exe";
                    key.SetValue(valueName, $"\"{exePath}\"");
                }
                else
                {
                    if (key.GetValue(valueName) != null) key.DeleteValue(valueName);
                }
            }
            catch (Exception ex)
            {
                AddLog("error", $"Falha ao aplicar 'Iniciar com Windows': {ex.Message}");
            }
        }

        private void btnPortalAluno_Click(object sender, RoutedEventArgs e)
        {
            if (_isOperationRunning) return;
            SetLoadingState(true);
            try
            {
                AddLog("info", "Abrindo Portal do Aluno...");
                StartPortalAluno();
                AddLog("info", "Portal do Aluno aberto.");
            }
            catch (Exception ex)
            {
                AddLog("error", $"Erro ao abrir Portal do Aluno: {ex.Message}");
                MessageBox.Show($"Erro ao abrir Portal do Aluno: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetLoadingState(false);
                AtualizarStatusServicos();
            }
        }

        // ---------------------------------------------------------------
        // Public navigation methods (called from TrayService)
        // ---------------------------------------------------------------
        public void NavigateToClientes()
        {
            rbTabPerfil.IsChecked = true;
            Tab_Click(rbTabPerfil, new RoutedEventArgs());
        }

        public void NavigateToBases()
        {
            NavigateToClientes();
            gridClientSettingsForm.Visibility = Visibility.Collapsed;
            gridAliasManagerForm.Visibility = Visibility.Visible;
        }

        public void TriggerUpdateCheck()
        {
            _ = CheckForUpdatesAsync();
        }

        private void btnAliases_Click(object sender, RoutedEventArgs e)
        {
            AddLog("info", "Abrindo gerenciador de bases...");
            // Navega para a aba Clientes e abre o gerenciador de bases
            rbTabPerfil.IsChecked = true;
            Tab_Click(rbTabPerfil, new RoutedEventArgs());
            // Seleciona o cliente ativo da home
            string activeClient = cbClienteAtivo.SelectedItem?.ToString() ?? string.Empty;
            if (!string.IsNullOrEmpty(activeClient) && cbPerfis.Items.Contains(activeClient))
            {
                cbPerfis.SelectedItem = activeClient;
            }
            UpdateFilteredAliasesList();
            gridClientSettingsForm.Visibility = Visibility.Collapsed;
            gridAliasManagerForm.Visibility = Visibility.Visible;
        }

        private void btnEditarBaseHome_Click(object sender, RoutedEventArgs e)
        {
            string baseSelecionada = cbBase.SelectedItem?.ToString() ?? string.Empty;
            if (string.IsNullOrEmpty(baseSelecionada))
            {
                AddLog("warn", "Selecione uma base primeiro.");
                return;
            }

            // Navega pra aba Clientes > Gerenciar Bases
            rbTabPerfil.IsChecked = true;
            Tab_Click(rbTabPerfil, new RoutedEventArgs());
            string activeClient = cbClienteAtivo.SelectedItem?.ToString() ?? string.Empty;
            if (!string.IsNullOrEmpty(activeClient) && cbPerfis.Items.Contains(activeClient))
                cbPerfis.SelectedItem = activeClient;
            UpdateFilteredAliasesList();
            gridClientSettingsForm.Visibility = Visibility.Collapsed;
            gridAliasManagerForm.Visibility = Visibility.Visible;

            // Seleciona a base atual na lista
            var alias = filteredAliases.FirstOrDefault(a => a.name == baseSelecionada);
            if (alias != null)
                lstBases.SelectedItem = alias;
        }

        private void btnDelDll_Click(object sender, RoutedEventArgs e)
        {
            if (_isOperationRunning) return;
            SetLoadingState(true);
            try
            {
                string customPath = Path.Combine(GetBinDirectory(), "Custom");
                if (!Directory.Exists(customPath))
                {
                    AddLog("error", $"Pasta Custom não encontrada em: {customPath}");
                    MessageBox.Show($"Pasta Custom não encontrada em: {customPath}", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var confirm = MessageBox.Show("Deseja realmente excluir todas as DLLs da pasta Custom?", "Confirmar Exclusão", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirm == MessageBoxResult.No) return;

                AddLog("info", "Iniciando exclusão de DLLs da pasta Custom...");
                int deletedCount = 0;
                int failedCount = 0;

                foreach (var file in Directory.GetFiles(customPath, "*.dll"))
                {
                    try
                    {
                        File.Delete(file);
                        deletedCount++;
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        AddLog("error", $"Falha ao deletar {Path.GetFileName(file)}: {ex.Message}");
                    }
                }

                string message = $"Exclusão concluída!\n\n• DLLs removidas: {deletedCount}";
                AddLog("info", $"Exclusão de DLLs concluída. Removidas: {deletedCount}. Falhas: {failedCount}.");
                if (failedCount > 0)
                {
                    message += $"\n• DLLs em uso (não removidas): {failedCount}";
                }
                MessageBox.Show(message, "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AddLog("error", $"Erro ao excluir DLLs: {ex.Message}");
                MessageBox.Show($"Erro ao excluir DLLs: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetLoadingState(false);
            }
        }


        private void btnHost2_Click(object sender, RoutedEventArgs e)
        {
            if (_isOperationRunning) return;
            SetLoadingState(true);
            try
            {
                AddLog("info", "Iniciando Host 2...");
                StartHost2();
                AddLog("info", "Host 2 iniciado.");
            }
            catch (Exception ex)
            {
                AddLog("error", $"Erro ao iniciar Host 2: {ex.Message}");
                MessageBox.Show($"Erro ao iniciar Host 2: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetLoadingState(false);
                AtualizarStatusServicos();
            }
        }

        private void btnReiniciarIIS_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AddLog("info", "Executando iisreset.exe...");
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "iisreset.exe",
                    UseShellExecute = true,
                    Verb = "runas" // Requires admin
                };
                var proc = Process.Start(psi);
                proc?.WaitForExit();
                if (proc != null && proc.ExitCode == 0)
                {
                    AddLog("info", "IIS reiniciado com sucesso.");
                    MessageBox.Show("IIS reiniciado com sucesso!", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else if (proc != null)
                {
                    AddLog("error", $"Erro ao reiniciar o IIS (Exit Code: {proc.ExitCode}).");
                    MessageBox.Show($"Ocorreu um erro ao reiniciar o IIS (Exit Code: {proc.ExitCode}).", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                AddLog("error", "Operação cancelada pelo usuário (UAC).");
                MessageBox.Show("Operação cancelada pelo usuário (requer privilégios de administrador).", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                AddLog("error", $"Erro ao reiniciar IIS: {ex.Message}");
                MessageBox.Show($"Erro ao reiniciar IIS: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnConfigIIS_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AddLog("info", "Abrindo Configuração IIS...");
                var win = new IISConfigWindow { Owner = this };
                win.ShowDialog();
                AddLog("info", "Configuração IIS fechada.");
            }
            catch (Exception ex)
            {
                AddLog("error", $"Erro ao abrir Configuração IIS: {ex.Message}");
                MessageBox.Show($"Erro ao abrir Configuração IIS: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnAbrirSSMS_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var alias = GetActiveAlias();
                if (alias == null || string.IsNullOrWhiteSpace(alias.server))
                {
                    MessageBox.Show("Selecione uma base com servidor configurado.", "Abrir SSMS", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string ssmsPath = null;
                string[] possiblePaths = {
                    @"C:\Program Files (x86)\Microsoft SQL Server Management Studio 20\Common7\IDE\Ssms.exe",
                    @"C:\Program Files (x86)\Microsoft SQL Server Management Studio 19\Common7\IDE\Ssms.exe",
                    @"C:\Program Files (x86)\Microsoft SQL Server Management Studio 18\Common7\IDE\Ssms.exe",
                };
                foreach (var p in possiblePaths)
                {
                    if (File.Exists(p)) { ssmsPath = p; break; }
                }

                if (ssmsPath == null)
                {
                    MessageBox.Show("SQL Server Management Studio não encontrado.\nVerifique se está instalado.", "SSMS não encontrado", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string args = $"-S \"{alias.server}\"";
                if (!string.IsNullOrWhiteSpace(alias.Base))
                    args += $" -d \"{alias.Base}\"";

                Process.Start(new ProcessStartInfo
                {
                    FileName = ssmsPath,
                    Arguments = args,
                    UseShellExecute = true
                });
                AddLog("info", $"SSMS aberto: servidor {alias.server}, base {alias.Base}");
            }
            catch (Exception ex)
            {
                AddLog("error", $"Erro ao abrir SSMS: {ex.Message}");
            }
        }

        private void btnReciclarAppPool_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string windir = Environment.GetEnvironmentVariable("windir") ?? @"C:\Windows";
                string appCmdPath = Path.Combine(windir, @"system32\inetsrv\appcmd.exe");
                
                if (!File.Exists(appCmdPath))
                {
                    AddLog("error", "Reciclar AppPool falhou: appcmd.exe não encontrado.");
                    MessageBox.Show("O IIS Express ou IIS Completo não foi detectado neste computador (appcmd.exe não encontrado).", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                AddLog("info", "Reciclando pools de aplicativos em execução...");
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"\"{appCmdPath}\" list apppools /state:Started /xml | \"{appCmdPath}\" recycle apppools /in\"",
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                
                var proc = Process.Start(psi);
                proc?.WaitForExit();
                if (proc != null && proc.ExitCode == 0)
                {
                    AddLog("info", "AppPools reciclados com sucesso.");
                    MessageBox.Show("Pools de aplicativos (AppPools) iniciados foram reciclados com sucesso!", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else if (proc != null)
                {
                    AddLog("error", $"Erro ao reciclar os AppPools (Exit Code: {proc.ExitCode}).");
                    MessageBox.Show($"Ocorreu um erro ao reciclar os AppPools (Exit Code: {proc.ExitCode}).", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                AddLog("error", "Operação cancelada pelo usuário (UAC).");
                MessageBox.Show("Operação cancelada pelo usuário (requer privilégios de administrador).", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                AddLog("error", $"Erro ao reciclar AppPools: {ex.Message}");
                MessageBox.Show($"Erro ao reciclar AppPools: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnLimparTemp_Click(object sender, RoutedEventArgs e)
        {
            if (_isOperationRunning) return;
            SetLoadingState(true);
            try
            {
                AddLog("info", "Iniciando limpeza de arquivos temporários...");
                long bytesFreed = 0;
                int filesDeleted = 0;
                int filesFailed = 0;

                string windir = Environment.GetEnvironmentVariable("windir") ?? @"C:\Windows";
                string[] pathsToClean = new string[]
                {
                    Path.GetTempPath(),
                    Path.Combine(windir, "Temp"),
                    Path.Combine(windir, @"Microsoft.NET\Framework64\v4.0.30319\Temporary ASP.NET Files"),
                    Path.Combine(windir, @"Microsoft.NET\Framework\v4.0.30319\Temporary ASP.NET Files")
                };

                foreach (var path in pathsToClean)
                {
                    if (Directory.Exists(path))
                    {
                        CleanDirectory(new DirectoryInfo(path), ref bytesFreed, ref filesDeleted, ref filesFailed);
                    }
                }

                double mbFreed = (double)bytesFreed / (1024 * 1024);
                string message = $"Limpeza de temporários concluída!\n\n" +
                                 $"• Arquivos removidos: {filesDeleted}\n" +
                                 $"• Espaço liberado: {mbFreed:F2} MB\n";
                AddLog("info", $"Limpeza concluída. Removidos: {filesDeleted} arquivos ({mbFreed:F2} MB liberados). Falhas: {filesFailed}.");
                if (filesFailed > 0)
                {
                    message += $"• Arquivos em uso (ignorados): {filesFailed}";
                }

                MessageBox.Show(message, "Limpeza de Temporários", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AddLog("error", $"Erro durante a limpeza: {ex.Message}");
                MessageBox.Show($"Erro durante a limpeza: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private void CleanDirectory(DirectoryInfo directory, ref long bytesFreed, ref int filesDeleted, ref int filesFailed)
        {
            // Delete files
            try
            {
                foreach (FileInfo file in directory.GetFiles())
                {
                    try
                    {
                        long size = file.Length;
                        file.Delete();
                        bytesFreed += size;
                        filesDeleted++;
                    }
                    catch
                    {
                        filesFailed++;
                    }
                }
            }
            catch
            {
                // Access denied to files
            }

            // Delete subdirectories
            try
            {
                foreach (DirectoryInfo subDir in directory.GetDirectories())
                {
                    CleanDirectory(subDir, ref bytesFreed, ref filesDeleted, ref filesFailed);
                    try
                    {
                        if (subDir.GetFiles().Length == 0 && subDir.GetDirectories().Length == 0)
                        {
                            subDir.Delete();
                        }
                    }
                    catch
                    {
                        // Subdirectory in use or access denied
                    }
                }
            }
            catch
            {
                // Access denied to subdirectories
            }
        }

        // --- Helper Methods ---

        private string GetBinDirectory()
        {
            // 1) Preferência: caminho salvo pelo wizard na 1ª execução
            if (!string.IsNullOrEmpty(_appSettings.RmInstallPath) && Directory.Exists(_appSettings.RmInstallPath))
            {
                return _appSettings.RmInstallPath;
            }

            // 2) Versão selecionada na aba Clientes
            string selectedVersion = cbVersaoRM?.SelectedItem?.ToString() ?? string.Empty;
            if (!string.IsNullOrEmpty(selectedVersion))
            {
                string legacyPath = $@"C:\RM\Legado\{selectedVersion}\Bin";
                if (Directory.Exists(legacyPath)) return legacyPath;
            }

            // 3) Qualquer Bin dentro de C:\RM\Legado (pega o primeiro)
            try
            {
                if (Directory.Exists(@"C:\RM\Legado"))
                {
                    foreach (var dir in Directory.GetDirectories(@"C:\RM\Legado"))
                    {
                        string bin = Path.Combine(dir, "Bin");
                        if (Directory.Exists(bin)) return bin;
                    }
                }
            }
            catch { /* ignore */ }

            // 4) Última opção: c:\totvs (legado), só se existir
            string corpPath = @"C:\totvs\CorporeRM\RM.Net";
            if (Directory.Exists(corpPath)) return corpPath;

            // Nada encontrado — caller vai mostrar erro
            return string.Empty;
        }

        public AliasConfig GetActiveAlias()
        {
            string activeClient = cbClienteAtivo.SelectedItem?.ToString() ?? cbPerfis.SelectedItem?.ToString() ?? string.Empty;
            if (cbBase.SelectedItem != null)
            {
                string selectedName = cbBase.SelectedItem?.ToString() ?? string.Empty;
                var found = aliases.FirstOrDefault(a => a.name == selectedName && (string.IsNullOrEmpty(activeClient) || a.client.Equals(activeClient, StringComparison.OrdinalIgnoreCase)));
                return found!;
            }
            return null!;
        }

        private void CreateAliasDat(string binDir, AliasConfig alias)
        {
            try
            {
                bool isSql = alias.dbType.Equals("sql", StringComparison.OrdinalIgnoreCase);
                string dbType = isSql ? "SqlServer" : "Oracle";
                string dbProvider = isSql ? "SqlClient" : "OracleClient";
                string dbNameTag = isSql ? $"<DbName>{alias.Base}</DbName>" : "<DbName/>";

                // Read new toggle values (must be accessed on the UI thread — already on dispatcher here)
                string normalizePath        = tsNormalizePath.IsOn.ToString().ToLower();
                string enableProcIsolation  = tsEnableProcessIsolation.IsOn.ToString().ToLower();
                string jobServer3Camadas    = tsJobServer3Camadas.IsOn.ToString().ToLower();
                string enableCompression    = tsEnableCompression.IsOn.ToString().ToLower();

                string xml = $@"<?xml version=""1.0"" standalone=""yes""?>
<RMSAliasData xmlns=""http://tempuri.org/RMSAliasData.xsd"">
  <DbConfig>
    <Alias>CorporeRM</Alias>
    <DbType>{dbType}</DbType>
    <DbProvider>{dbProvider}</DbProvider>
    <DbServer>{alias.server}</DbServer>
    {dbNameTag}
    <UserName>{alias.dbUser}</UserName>
    <Password>{alias.dbPass}</Password>
    <RunService>{alias.runService.ToString().ToLower()}</RunService>
    <JobServerEnabled>{alias.jobProcessing.ToString().ToLower()}</JobServerEnabled>
    <JobServerMaxThreads>{alias.maxThreads}</JobServerMaxThreads>
    <JobServerLocalOnly>{alias.localOnly.ToString().ToLower()}</JobServerLocalOnly>
    <JobServerPollingInterval>10</JobServerPollingInterval>
    <ChartAlertEnabled>false</ChartAlertEnabled>
    <ChartAlertPollingInterval>20</ChartAlertPollingInterval>
    <ChartHistoryEnabled>false</ChartHistoryEnabled>
    <ChartHistoryPollingInterval>20</ChartHistoryPollingInterval>
    <RSSReaderMailEnabled>false</RSSReaderMailEnabled>
    <RSSReaderMailPollingInterval>10</RSSReaderMailPollingInterval>
    <JobServerProcessPoolEnabled>{alias.processPool.ToString().ToLower()}</JobServerProcessPoolEnabled>
    <NormalizePath>{normalizePath}</NormalizePath>
    <EnableProcessIsolation>{enableProcIsolation}</EnableProcessIsolation>
    <IsolateProcess>false</IsolateProcess>
    <JobServer3Camadas>{jobServer3Camadas}</JobServer3Camadas>
    <DefaultDB>CorporeRM</DefaultDB>
    <EnableCompression>{enableCompression}</EnableCompression>
  </DbConfig>
</RMSAliasData>";

                string datPath = System.IO.Path.Combine(binDir, "Alias.dat");
                if (System.IO.File.Exists(datPath))
                {
                    System.IO.File.Delete(datPath);
                }
                System.IO.File.WriteAllText(datPath, xml);
                AddLog("info", $"[Alias.dat] Gerado com sucesso em: {datPath} (Alias: CorporeRM, Base: {alias.Base})");
            }
            catch (Exception ex)
            {
                AddLog("error", $"[Alias.dat] Erro ao criar Alias.dat: {ex.Message}");
            }
        }

        private void PrepareAlias()
        {
            var activeAlias = GetActiveAlias();
            if (activeAlias != null)
            {
                string binDir = GetBinDirectory();
                CreateAliasDat(binDir, activeAlias);
            }
            else
            {
                AddLog("error", "Nenhum Alias ativo selecionado para gerar Alias.dat.");
            }
        }

        private void StartHostPrincipal()
        {
            PrepareAlias();

            string binDir = GetBinDirectory();
            string path = Path.Combine(binDir, "RM.Host.exe");
            if (!File.Exists(path))
                path = Path.Combine(binDir, "RM.Host.ServiceManager.exe");

            StartProcess(path, "Host Principal");
        }

        private void StopHostPrincipal()
        {
            KillProcessByName("RM.Host.ServiceManager");
            KillProcessByName("RM.Host");
        }

        private void StartHost2()
        {
            PrepareAlias();

            string binDir = GetBinDirectory();
            string path = Path.Combine(binDir, "RM.Host.exe"); 
            StartProcess(path, "Host 2");
        }

        private void StopHost2()
        {
            KillProcessByName("RM.Host");
        }

        private void StartPortalAluno()
        {
            string url = "http://localhost/FrameHTML/web/app/Edu/portaleducacional";
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao abrir o Portal do Aluno: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StopPortalAluno()
        {
            // Web portal doesn't require termination
        }

        private async Task StartRMAsync(bool checkHost = true)
        {
            PrepareAlias();

            string binDir = GetBinDirectory();
            string path = Path.Combine(binDir, "RM.exe");

            if (checkHost)
            {
                int hostPort = GetHostPortFromConfig(binDir);
                bool isHostListening = IsPortListening("127.0.0.1", hostPort);

                if (!isHostListening)
                {
                    var result = MessageBox.Show(
                        $"O serviço do Host local (porta {hostPort}) não foi detectado em execução.\n\nDeseja iniciar o Host local antes de abrir o RM?",
                        "Aviso - Host não iniciado",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Cancel)
                    {
                        AddLog("info", $"Abertura do RM cancelada pelo usuário (Host inativo na porta {hostPort}).");
                        return;
                    }
                    else if (result == MessageBoxResult.Yes)
                    {
                        AddLog("info", "Iniciando Host Principal antes de abrir o RM...");
                        StartHostPrincipal();
                        await WaitForHostPortAsync(hostPort, 15000);
                        await WaitForAuthenticationAsync(binDir, hostPort, 30000);
                    }
                }
            }

            // Delete _broker.dat if the active profile has DelBroker enabled
            try
            {
                string activeClientName = cbClienteAtivo.SelectedItem?.ToString() ?? string.Empty;
                if (!string.IsNullOrEmpty(activeClientName) &&
                    profiles.TryGetValue(activeClientName, out var activeProfile) &&
                    activeProfile.DelBroker)
                {
                    string brokerPath = Path.Combine(binDir, "_broker.dat");
                    if (File.Exists(brokerPath))
                    {
                        File.Delete(brokerPath);
                        AddLog("info", "[DelBroker] _broker.dat excluído com sucesso.");
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog("error", $"[DelBroker] Falha ao excluir _broker.dat: {ex.Message}");
            }

            if (File.Exists(path))
            {
                try
                {
                    bool autoLogin = tsAutoLogin.IsOn;
                    var activeAlias = GetActiveAlias();



                    if (autoLogin && activeAlias != null)
                    {
                        string user = string.IsNullOrWhiteSpace(activeAlias.rmUser) ? "mestre" : activeAlias.rmUser;
                        string pass = string.IsNullOrWhiteSpace(activeAlias.rmPass) ? "totvs" : activeAlias.rmPass;

                        string args = $"multi=true alias=CorporeRM user={user} password={pass} #objetos_gerenciais";

                        Process.Start(new ProcessStartInfo
                        {
                            FileName = path,
                            Arguments = args,
                            UseShellExecute = true,
                            WorkingDirectory = binDir
                        });
                        AddLog("info", $"RM.exe iniciado com AutoLogin (Usuário: {user}).");
                    }
                    else
                    {

                        Process.Start(new ProcessStartInfo
                        {
                            FileName = path,
                            UseShellExecute = true,
                            WorkingDirectory = binDir
                        });
                        AddLog("info", "RM.exe iniciado sem AutoLogin.");
                    }
                }
                catch (Exception ex)
                {
                    AddLog("error", $"Erro ao iniciar RM App: {ex.Message}");
                    MessageBox.Show($"Erro ao iniciar RM App: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show($"RM App não encontrado em: {path}", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private int GetHostPortFromConfig(string binDir)
        {
            try
            {
                string[] configFiles = { "RM.Host.exe.config", "RM.Host.Service.exe.config" };
                foreach (var configFile in configFiles)
                {
                    string path = Path.Combine(binDir, configFile);
                    if (File.Exists(path))
                    {
                        var doc = XDocument.Load(path);
                        var portElement = doc.Descendants("add")
                            .FirstOrDefault(el => el.Attribute("key")?.Value.Equals("Port", StringComparison.OrdinalIgnoreCase) == true);
                        
                        if (portElement != null && int.TryParse(portElement.Attribute("value")?.Value, out int port))
                        {
                            return port;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog("warning", $"Não foi possível ler a porta do Host do config: {ex.Message}. Usando a padrão 8050.");
            }
            return 8050; // Default fallback
        }

        private int? GetHostHttpPortFromConfig(string binDir)
        {
            try
            {
                string[] configFiles = { "RM.Host.exe.config", "RM.Host.Service.exe.config" };
                foreach (var configFile in configFiles)
                {
                    string path = Path.Combine(binDir, configFile);
                    if (File.Exists(path))
                    {
                        var doc = XDocument.Load(path);
                        var portElement = doc.Descendants("add")
                            .FirstOrDefault(el => el.Attribute("key")?.Value.Equals("HttpPort", StringComparison.OrdinalIgnoreCase) == true);
                        
                        if (portElement != null && int.TryParse(portElement.Attribute("value")?.Value, out int port))
                        {
                            return port;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog("warning", $"Não foi possível ler a porta HttpPort do Host do config: {ex.Message}.");
            }
            return null;
        }

        private async Task<bool> WaitForHostHttpAsync(int httpPort, int timeoutMs)
        {
            using (var client = new System.Net.Http.HttpClient())
            {
                client.Timeout = TimeSpan.FromMilliseconds(500);
                string url = $"http://127.0.0.1:{httpPort}/";
                int elapsed = 0;
                int delay = 500;
                while (elapsed < timeoutMs)
                {
                    try
                    {
                        var response = await client.GetAsync(url);
                        AddLog("info", $"Serviço do Host HTTP respondendo (Status: {(int)response.StatusCode}).");
                        return true;
                    }
                    catch (System.Net.Http.HttpRequestException)
                    {
                        // Port closed or server not responding
                    }
                    catch (TaskCanceledException)
                    {
                        // Timeout
                    }
                    catch (Exception)
                    {
                        // General network/socket errors
                    }
                    await Task.Delay(delay);
                    elapsed += delay;
                }
            }
            return false;
        }

        private async Task<bool> WaitForHostPortAsync(int port, int timeoutMs)
        {
            if (IsPortListening("127.0.0.1", port))
            {
                await TryHttpCheckAsync();
                return true;
            }

            AddLog("info", $"Aguardando serviço do Host na porta {port} (timeout: {timeoutMs / 1000}s)...");

            int elapsed = 0;
            int interval = 500;
            while (elapsed < timeoutMs)
            {
                await Task.Delay(interval);
                elapsed += interval;

                if (IsPortListening("127.0.0.1", port))
                {
                    AddLog("info", $"Host detectado na porta {port} após {elapsed / 1000.0:F1}s.");
                    await TryHttpCheckAsync();
                    return true;
                }
            }

            AddLog("warning", $"Host não respondeu na porta {port} dentro de {timeoutMs / 1000}s.");
            return false;
        }

        private async Task TryHttpCheckAsync()
        {
            try
            {
                string binDir = GetBinDirectory();
                int? httpPort = GetHostHttpPortFromConfig(binDir);
                if (httpPort.HasValue)
                {
                    bool httpReady = await WaitForHostHttpAsync(httpPort.Value, 10000);
                    if (httpReady)
                        AddLog("info", "Stack HTTP do Host confirmado.");
                    else
                        AddLog("warning", "Porta TCP aberta mas HTTP não respondeu (pode estar em inicialização).");
                }
            }
            catch { /* HTTP check is bonus — never fail because of it */ }
        }

        private string? FindHostCheckExe()
        {
            try
            {
                string appDir = Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
                var dir = new DirectoryInfo(appDir);
                while (dir != null)
                {
                    string local = Path.Combine(dir.FullName, "RM.HostCheck.exe");
                    if (File.Exists(local))
                        return local;
                    string inTools = Path.Combine(dir.FullName, "tools", "RM.HostCheck.exe");
                    if (File.Exists(inTools))
                        return inTools;
                    dir = dir.Parent;
                }
            }
            catch { }
            return null;
        }

        private int ReadHostClientPortFromConfig(string binDir, int hostPort)
        {
            try
            {
                foreach (var configFile in new[] { "RM.Host.exe.config", "RM.Host.Service.exe.config" })
                {
                    string path = Path.Combine(binDir, configFile);
                    if (File.Exists(path))
                    {
                        var doc = XDocument.Load(path);
                        var el = doc.Descendants("add")
                            .FirstOrDefault(a => string.Equals(a.Attribute("key")?.Value, "HostClientPort", StringComparison.OrdinalIgnoreCase));
                        if (el != null && int.TryParse(el.Attribute("value")?.Value, out int port))
                            return port;
                    }
                }
            }
            catch { }
            return hostPort + 500;
        }

        private async Task<bool> WaitForAuthenticationAsync(string binDir, int hostPort, int timeoutMs)
        {
            string? hostCheckExe = FindHostCheckExe();
            if (hostCheckExe == null)
            {
                AddLog("warning", "RM.HostCheck.exe não encontrado — pulando verificação de ícone verde.");
                return false;
            }

            int hcPort = ReadHostClientPortFromConfig(binDir, hostPort);

            try
            {
                AddLog("info", $"Aguardando ícone verde (autenticação) na porta {hcPort}...");

                var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = hostCheckExe,
                    Arguments = $"\"{binDir}\" {hcPort} {timeoutMs}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                });

                if (proc == null)
                {
                    AddLog("error", "Falha ao iniciar RM.HostCheck.exe.");
                    return false;
                }

                await proc.WaitForExitAsync();

                AddLog("info", proc.ExitCode switch
                {
                    0 => "Ícone verde confirmado (autenticado).",
                    1 => "Host Client rodando mas não autenticou no tempo limite.",
                    _ => $"HostCheck retornou código {proc.ExitCode}. Prosseguindo..."
                });

                return proc.ExitCode == 0;
            }
            catch (Exception ex)
            {
                AddLog("warning", $"HostCheck falhou: {ex.Message}");
                return false;
            }
        }

        private static bool IsPortListening(string host, int port)
        {
            try
            {
                using (var tcpClient = new System.Net.Sockets.TcpClient())
                {
                    var result = tcpClient.BeginConnect(host, port, null, null);
                    bool success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(300));
                    if (success)
                    {
                        tcpClient.EndConnect(result);
                        return true;
                    }
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        private void StartProcess(string path, string displayName)
        {
            if (File.Exists(path))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true,
                        WorkingDirectory = Path.GetDirectoryName(path)
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erro ao iniciar {displayName}: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show($"{displayName} não encontrado em: {path}", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OpenFolder(string path)
        {
            if (Directory.Exists(path))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erro ao abrir pasta: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show($"Pasta não encontrada em: {path}", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void KillProcessByName(string name)
        {
            try
            {
                foreach (var proc in Process.GetProcessesByName(name))
                {
                    proc.Kill();
                }
            }
            catch
            {
                // Ignore if not running or permission denied
            }
        }

        private void KillAllProcesses()
        {
            KillProcessByName("RM");
            KillProcessByName("RM.Host.ServiceManager");
            KillProcessByName("RM.Host");
            KillProcessByName("RM.Host.Service");
        }

        // ---------------------------------------------------------------
        // Window lifecycle — OnStateChanged, OnClosing
        // ---------------------------------------------------------------

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            // Nota: minimize via Win+D, botão de minimizar da titlebar, etc
            // NÃO devem esconder pra tray. Só o X (OnClosing) faz isso.
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (IsExiting)
            {
                SaveWindowSettings();
                SaveAppSettings();
                return;
            }

            if (_appSettings.CloseMinimizesToTray)
            {
                // Cancela o fechamento do OS; esconde pra bandeja.
                // Saída real só pelo menu "Sair" do tray ou se o toggle estiver off.
                e.Cancel = true;
                SaveWindowSettings(); // persiste posição antes de esconder
                _trayService?.MinimizeToTray();
            }
            else
            {
                // Toggle off → X fecha o app de verdade
                IsExiting = true;
                SaveWindowSettings();
                SaveAppSettings();
            }
        }

        // ---------------------------------------------------------------
        // Window position / size persistence
        // ---------------------------------------------------------------

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            RestoreWindowSettings();
            LoadAppSettings();
            ApplyStartWithWindowsSetting(_appSettings.StartWithWindows);

            // Se for a primeira vez, mostra o wizard de configuração.
            // O user pode fechar/pular; nesse caso FirstRunComplete continua false
            // e o wizard volta a aparecer no próximo login.
            if (!_appSettings.FirstRunComplete)
            {
                // janela precisa estar visível pra ser Owner do wizard
                Show();
                RunFirstRunWizard();
            }
        }

        private void RunFirstRunWizard(bool fromButton = false)
        {
            try
            {
                var wiz = new WizardWindow { Owner = this };
                bool? result = wiz.ShowDialog();

                if (wiz.WizardCompleted)
                {
                    ApplyWizardResults(wiz);
                    _appSettings.FirstRunComplete = true;
                    SaveAppSettings();
                    ApplyStartWithWindowsSetting(_appSettings.StartWithWindows);
                    AddLog("info", "Configuração inicial concluída via wizard.");
                }
                else if (fromButton)
                {
                    AddLog("info", "Wizard cancelado pelo usuário.");
                }
            }
            catch (Exception ex)
            {
                AddLog("error", $"Erro no wizard: {ex.Message}");
            }
        }

        private void ApplyWizardResults(WizardWindow wiz)
        {
            // 1) Settings de comportamento
            _appSettings.CloseMinimizesToTray = wiz.CloseToTray;
            _appSettings.StartWithWindows     = wiz.StartWithWindows;
            _appSettings.StartMinimized       = wiz.StartMinimized;
            _appSettings.RmInstallPath        = wiz.SelectedInstallPath;

            // sincroniza os toggles da aba Sobre
            _isSyncing = true;
            try
            {
                if (tsCloseMinimizesToTray != null) tsCloseMinimizesToTray.IsOn = wiz.CloseToTray;
                if (tsStartWithWindows     != null) tsStartWithWindows.IsOn     = wiz.StartWithWindows;
                if (tsStartMinimized       != null) tsStartMinimized.IsOn       = wiz.StartMinimized;
            }
            finally
            {
                _isSyncing = false;
            }

            // 2) Cria o cliente padrão
            if (!string.IsNullOrWhiteSpace(wiz.ClientName))
            {
                var profile = new ProfileSettings
                {
                    Name            = wiz.ClientName,
                    RmVersion       = string.IsNullOrWhiteSpace(wiz.ClientVersion) ? "12.1.2602" : wiz.ClientVersion,
                    Alias           = string.IsNullOrWhiteSpace(wiz.BaseName) ? "CorporeRM" : wiz.BaseName,
                    AutoLogin       = false,
                    DelBroker       = false,
                    VerboseLogs     = true,
                    ApagarHost      = false,
                };
                profiles[profile.Name] = profile;
            }

            // 3) Cria a base padrão associada a esse cliente
            if (!string.IsNullOrWhiteSpace(wiz.BaseName) && !string.IsNullOrWhiteSpace(wiz.BaseServer))
            {
                var alias = new AliasConfig
                {
                    id          = DateTime.Now.Ticks.ToString(),
                    name        = wiz.BaseName,
                    Base        = wiz.BaseName,
                    client      = wiz.ClientName,
                    server      = wiz.BaseServer,
                    dbType      = wiz.BaseProvider,
                    dbUser      = wiz.BaseDbUser,
                    dbPass      = wiz.BaseDbPass,
                    rmUser      = "mestre",
                    rmPass      = "totvs",
                    runService  = true,
                    dbVersion   = string.IsNullOrWhiteSpace(wiz.ClientVersion) ? "12.1.2602" : wiz.ClientVersion,
                };
                aliases.Add(alias);
            }

            // 4) Persiste tudo
            SaveProfiles();
            SaveAliases();
            UpdateProfilesUI();
            UpdateFilteredAliasesList();


            if (profiles.TryGetValue(wiz.ClientName, out var p))
            {
                LoadProfileToUI(p);
            }
        }

        private void SaveWindowSettings()
        {
            try
            {
                double left = Left;
                double top = Top;
                double width = Width;
                double height = Height;

                if (WindowState == WindowState.Maximized || WindowState == WindowState.Minimized)
                {
                    left = RestoreBounds.Left;
                    top = RestoreBounds.Top;
                    width = RestoreBounds.Width;
                    height = RestoreBounds.Height;
                }

                // Fallback to default sizes if width or height are too small or invalid
                if (width < 450) width = 450;
                if (height < 710) height = 710;

                var settings = new WindowSettings
                {
                    Left   = left,
                    Top    = top,
                    Width  = width,
                    Height = height
                };
                Directory.CreateDirectory(Path.GetDirectoryName(_windowSettingsPath)!);
                File.WriteAllText(_windowSettingsPath, JsonSerializer.Serialize(settings));
            }
            catch { /* non-critical — ignore */ }
        }

        private void RestoreWindowSettings()
        {
            try
            {
                if (!File.Exists(_windowSettingsPath)) return;
                var settings = JsonSerializer.Deserialize<WindowSettings>(
                    File.ReadAllText(_windowSettingsPath));
                if (settings == null) return;

                // Validate that the window size is reasonable (not collapsed/minimized)
                double minWidth = Math.Max(MinWidth, 450);
                double minHeight = Math.Max(MinHeight, 710);

                if (settings.Width >= minWidth && settings.Height >= minHeight)
                {
                    Width  = settings.Width;
                    Height = settings.Height;
                }
                if (IsPositionOnScreen(settings.Left, settings.Top, settings.Width, settings.Height))
                {
                    Left = settings.Left;
                    Top  = settings.Top;
                    WindowStartupLocation = WindowStartupLocation.Manual;
                }
            }
            catch { /* non-critical — ignore */ }
        }

        /// <summary>
        /// Verifica se uma janela com a posição/tamanho dados está visível em
        /// alguma tela (considera o bounding box do virtual screen).
        /// </summary>
        public bool IsPositionOnScreen(double left, double top, double width, double height)
        {
            double virtLeft   = SystemParameters.VirtualScreenLeft;
            double virtTop    = SystemParameters.VirtualScreenTop;
            double virtRight  = virtLeft + SystemParameters.VirtualScreenWidth;
            double virtBottom = virtTop  + SystemParameters.VirtualScreenHeight;

            // Exige interseção mínima: pelo menos 100x50 px da janela dentro do virtual screen
            const double minVisible = 100;
            const double minVisibleH = 50;

            double winRight  = left + width;
            double winBottom = top  + height;

            double visibleW = Math.Max(0, Math.Min(winRight, virtRight)  - Math.Max(left, virtLeft));
            double visibleH = Math.Max(0, Math.Min(winBottom, virtBottom) - Math.Max(top,  virtTop));

            return visibleW >= minVisible && visibleH >= minVisibleH;
        }

        // ---------------------------------------------------------------
        // App settings (CloseMinimizesToTray, StartWithWindows, etc)
        // ---------------------------------------------------------------

        private void LoadAppSettings()
        {
            try
            {
                if (!File.Exists(_appSettingsPath)) return;
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_appSettingsPath));
                if (loaded != null) _appSettings = loaded;
            }
            catch { /* keep defaults */ }

            // Apply favorites and tag colors to in-memory objects
            foreach (var kv in profiles)
            {
                kv.Value.IsFavorite = _appSettings.FavoriteClientNames.Contains(kv.Key);
            }
            foreach (var alias in aliases)
            {
                alias.IsFavorite = _appSettings.BaseFavoriteIds.Contains(alias.id);
                if (_appSettings.BaseTagColors.TryGetValue(alias.id, out var color))
                    alias.TagColor = color;
            }

            // Migrate legacy favorites (first one) to the new single-default system
            if (string.IsNullOrEmpty(_appSettings.DefaultClient) && _appSettings.FavoriteClientNames.Count > 0)
                _appSettings.DefaultClient = _appSettings.FavoriteClientNames[0];
            if (string.IsNullOrEmpty(_appSettings.DefaultBaseId) && _appSettings.BaseFavoriteIds.Count > 0)
                _appSettings.DefaultBaseId = _appSettings.BaseFavoriteIds[0];

            // Sincroniza os toggles da aba Sobre com o que está persistido
            _isSyncing = true;
            try
            {
                if (tsCloseMinimizesToTray != null) tsCloseMinimizesToTray.IsOn = _appSettings.CloseMinimizesToTray;
                if (tsStartWithWindows     != null) tsStartWithWindows.IsOn     = _appSettings.StartWithWindows;
                if (tsStartMinimized       != null) tsStartMinimized.IsOn       = _appSettings.StartMinimized;
            }
            finally
            {
                _isSyncing = false;
            }
        }

        private void SaveAppSettings()
        {
            try
            {
                _appSettings.FavoriteClientNames = profiles.Values.Where(p => p.IsFavorite).Select(p => p.Name).ToList();
                _appSettings.BaseFavoriteIds = aliases.Where(a => a.IsFavorite).Select(a => a.id).ToList();
                _appSettings.BaseTagColors = aliases.Where(a => !string.IsNullOrEmpty(a.TagColor)).ToDictionary(a => a.id, a => a.TagColor);
                Directory.CreateDirectory(Path.GetDirectoryName(_appSettingsPath)!);
                File.WriteAllText(_appSettingsPath, JsonSerializer.Serialize(_appSettings));
            }
            catch { /* non-critical — ignore */ }
        }

        // ---------------------------------------------------------------
        // Service status panel
        // ---------------------------------------------------------------

        private void SetLoadingState(bool loading)
        {
            _isOperationRunning = loading;
            
            // Disable all interactive UI elements to prevent spam clicks
            btnIniciarCompleto.IsEnabled = !loading;
            btnIniciarDropdown.IsEnabled = !loading;
            btnDerrubarTudo.IsEnabled = !loading;
            btnCustom.IsEnabled = !loading;
            btnAliases.IsEnabled = !loading;
            btnBin.IsEnabled = !loading;
            btnDelDll.IsEnabled = !loading;
            btnInstalarDualHost.IsEnabled = !loading;
            btnValidarDLLs.IsEnabled = !loading;
            btnReiniciarIIS.IsEnabled = !loading;
            btnConfigIIS.IsEnabled = !loading;
            btnReciclarAppPool.IsEnabled = !loading;
            btnLimparTemp.IsEnabled = !loading;
            btnSalvarPerfil.IsEnabled = !loading;
            btnDeletarPerfil.IsEnabled = !loading;
            // btnImportarAmbientes and btnImportarAliases removed
            btnGerenciarAliases.IsEnabled = !loading;
            btnVoltarCliente.IsEnabled = !loading;
            btnNovaBase.IsEnabled = !loading;
            btnSalvarBase.IsEnabled = !loading;
            btnTestarConexao.IsEnabled = !loading;
            btnDeletarBase.IsEnabled = !loading;
            btnDuplicarBase.IsEnabled = !loading;
            btnBaixarUpdate.IsEnabled = !loading;
            btnLimparLogs.IsEnabled = !loading;
            btnAbrirSSMS.IsEnabled = !loading;
            btnAtualizarServicos.IsEnabled = !loading;
            
            menuIniciarSeparado.IsEnabled = !loading;

            if (loading)
            {
                System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            }
            else
            {
                System.Windows.Input.Mouse.OverrideCursor = null;
            }
        }

        public void AtualizarStatusServicos()
        {
            var processNames = new[] { "RM", "RM.Host", "RM.Host.ServiceManager", "RM.Host.Service" };
            var entries = new List<string>();

            foreach (string name in processNames)
            {
                try
                {
                    var procs = Process.GetProcessesByName(name);
                    foreach (var p in procs)
                    {
                        string cpu = string.Empty;
                        try { cpu = $" (PID {p.Id})";
                        } catch { /* ignore */ }
                        entries.Add($"● {name}{cpu} — EM EXECUÇÃO");
                    }
                }
                catch { /* ignore access errors */ }
            }

            lstServicos.Items.Clear();
            if (entries.Count == 0)
            {
                txtSemServicos.Visibility = Visibility.Visible;
            }
            else
            {
                txtSemServicos.Visibility = Visibility.Collapsed;
                foreach (var entry in entries)
                    lstServicos.Items.Add(entry);
            }
        }

        private void btnAtualizarServicos_Click(object sender, RoutedEventArgs e)
        {
            AtualizarStatusServicos();
        }

        // ---------------------------------------------------------------
        // Tab navigation
        // ---------------------------------------------------------------

        private void Tab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb)
            {
                gridTabInicio.Visibility = rb == rbTabInicio ? Visibility.Visible : Visibility.Collapsed;
                gridTabPerfil.Visibility = rb == rbTabPerfil ? Visibility.Visible : Visibility.Collapsed;
                gridTabLogs.Visibility = rb == rbTabLogs ? Visibility.Visible : Visibility.Collapsed;
                gridTabSobre.Visibility = rb == rbTabSobre ? Visibility.Visible : Visibility.Collapsed;

                if (rb == rbTabPerfil)
                {
                    gridClientSettingsForm.Visibility = Visibility.Visible;
                    gridAliasManagerForm.Visibility = Visibility.Collapsed;
                    UpdateFilteredAliasesList();
                }

                // Refresh service status when navigating back to Início
                if (rb == rbTabInicio)
                {
                    AtualizarStatusServicos();
                }

                // Atualiza painel de versão ao navegar pra Sobre
                if (rb == rbTabSobre)
                {
                    AtualizarPanelVersao();
                }
            }
        }

        private void btnGerenciarAliases_Click(object sender, RoutedEventArgs e)
        {
            UpdateFilteredAliasesList();
            gridClientSettingsForm.Visibility = Visibility.Collapsed;
            gridAliasManagerForm.Visibility = Visibility.Visible;
        }

        private void btnVoltarCliente_Click(object sender, RoutedEventArgs e)
        {
            gridAliasManagerForm.Visibility = Visibility.Collapsed;
            gridClientSettingsForm.Visibility = Visibility.Visible;
        }

        private void InitializeBasesTab()
        {
            cbClienteAssociado.Items.Clear();
            foreach (var key in profiles.Keys)
            {
                cbClienteAssociado.Items.Add(key);
            }

            cbDbVersion.Items.Clear();
            var versions = GetRmVersions();
            foreach (var v in versions)
            {
                cbDbVersion.Items.Add(v);
            }

            lstBases.ItemsSource = filteredAliases;
            UpdateFilteredAliasesList();
        }

        private void UpdateFilteredAliasesList(string searchTerm = "")
        {
            if (filteredAliases == null) return;
            string activeClient = cbPerfis.SelectedItem?.ToString() ?? cbClienteAtivo.SelectedItem?.ToString() ?? string.Empty;
            filteredAliases.Clear();
            foreach (var alias in aliases)
            {
                if (alias.client.Equals(activeClient, StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(searchTerm) || alias.name.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0 || (alias.Base?.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        filteredAliases.Add(alias);
                    }
                }
            }
            RefreshBaseListColorDots();
        }

        // ---------------------------------------------------------------
        // Feature 3: Search filter on bases
        // ---------------------------------------------------------------
        private void txtSearchBase_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateFilteredAliasesList(txtSearchBase.Text.Trim());
        }

        // ---------------------------------------------------------------
        // Feature 2: Color tag selector
        // ---------------------------------------------------------------
        private void colorTagSelector_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is Border border && lstBases.SelectedItem is AliasConfig selectedAlias)
            {
                string tag = border.Tag?.ToString() ?? "";
                selectedAlias.TagColor = tag;
                UpdateColorTagSelectorUI(tag);
                RefreshBaseListColorDots();
                lstBases.Items.Refresh();
                RefreshCbBaseColors();
                SaveAppSettings();
            }
        }

        private void UpdateColorTagSelectorUI(string selectedColor)
        {
            var borders = new[] { colorTagGreen, colorTagYellow, colorTagRed };
            foreach (var b in borders)
            {
                b.BorderBrush = (b.Tag?.ToString() == selectedColor) ? new SolidColorBrush(System.Windows.Media.Colors.White) : new SolidColorBrush(System.Windows.Media.Colors.Transparent);
            }
        }

        // ---------------------------------------------------------------
        // Refresh color dots on lstBases and cbBase
        // ---------------------------------------------------------------
        private void RefreshBaseListColorDots()
        {
            if (lstBases.Items.Count == 0) return;
            for (int i = 0; i < lstBases.Items.Count; i++)
            {
                var item = lstBases.ItemContainerGenerator.ContainerFromIndex(i) as System.Windows.Controls.ListBoxItem;
                if (item == null) continue;
                var alias = item.DataContext as AliasConfig;
                if (alias == null) continue;
                var ellipse = FindVisualChild<System.Windows.Shapes.Ellipse>(item);
                if (ellipse != null)
                {
                    ellipse.Fill = GetColorBrush(alias.TagColor);
                }
            }
        }

        private void RefreshCbBaseColors()
        {
            if (cbBase.Items.Count == 0) return;
            for (int i = 0; i < cbBase.Items.Count; i++)
            {
                var item = cbBase.ItemContainerGenerator.ContainerFromIndex(i) as System.Windows.Controls.ComboBoxItem;
                if (item == null) continue;
                var alias = item.DataContext as AliasConfig;
                if (alias == null) continue;
                var ellipse = FindVisualChild<System.Windows.Shapes.Ellipse>(item);
                if (ellipse != null)
                {
                    ellipse.Fill = GetColorBrush(alias.TagColor);
                }
            }
        }

        private System.Windows.Media.Brush GetColorBrush(string tagColor)
        {
            return tagColor switch
            {
                "green" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80)),
                "yellow" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 193, 7)),
                "red" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(244, 67, 54)),
                _ => new SolidColorBrush(System.Windows.Media.Colors.Transparent)
            };
        }

        private T? FindVisualChild<T>(System.Windows.DependencyObject parent) where T : System.Windows.DependencyObject
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        // ---------------------------------------------------------------
        // Feature 5: Toggle password visibility
        // ---------------------------------------------------------------
        private void btnTogglePass_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                string target = btn.Tag?.ToString() ?? "";
                if (target == "rm")
                {
                    if (pbRmPass.Visibility == Visibility.Visible)
                    {
                        txtRmPassVisible.Text = pbRmPass.Password;
                        pbRmPass.Visibility = Visibility.Collapsed;
                        txtRmPassVisible.Visibility = Visibility.Visible;
                        iconToggleRmPass.Text = "\uED1A";
                    }
                    else
                    {
                        pbRmPass.Password = txtRmPassVisible.Text;
                        pbRmPass.Visibility = Visibility.Visible;
                        txtRmPassVisible.Visibility = Visibility.Collapsed;
                        iconToggleRmPass.Text = "\uE7B3";
                    }
                }
                else if (target == "db")
                {
                    if (pbDbPass.Visibility == Visibility.Visible)
                    {
                        txtDbPassVisible.Text = pbDbPass.Password;
                        pbDbPass.Visibility = Visibility.Collapsed;
                        txtDbPassVisible.Visibility = Visibility.Visible;
                        iconToggleDbPass.Text = "\uED1A";
                    }
                    else
                    {
                        pbDbPass.Password = txtDbPassVisible.Text;
                        pbDbPass.Visibility = Visibility.Visible;
                        txtDbPassVisible.Visibility = Visibility.Collapsed;
                        iconToggleDbPass.Text = "\uE7B3";
                    }
                }
            }
        }

        // ---------------------------------------------------------------
        // Default client / default base (single, persisted)
        // ---------------------------------------------------------------
        private void btnToggleFavorito_Click(object sender, RoutedEventArgs e)
        {
            string name = txtNomePerfil.Text.Trim();
            if (string.IsNullOrEmpty(name)) return;
            if (!profiles.TryGetValue(name, out var profile))
            {
                string selected = cbPerfis.SelectedItem?.ToString() ?? string.Empty;
                if (!profiles.TryGetValue(selected, out profile)) return;
            }

            if (_appSettings.DefaultClient == profile.Name)
            {
                _appSettings.DefaultClient = string.Empty;
                AddLog("info", $"Cliente \"{profile.Name}\" removido do padrão.");
            }
            else
            {
                _appSettings.DefaultClient = profile.Name;
                AddLog("info", $"Cliente \"{profile.Name}\" definido como padrão.");
            }
            UpdateFavoritoIcon(!string.IsNullOrEmpty(_appSettings.DefaultClient) && _appSettings.DefaultClient == profile.Name);
            SaveAppSettings();
        }

        private void btnToggleBaseFavorito_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AliasConfig alias)
            {
                if (_appSettings.DefaultBaseId == alias.id)
                {
                    _appSettings.DefaultBaseId = string.Empty;
                    AddLog("info", $"Base \"{alias.name}\" removida do padrão.");
                }
                else
                {
                    _appSettings.DefaultBaseId = alias.id;
                    AddLog("info", $"Base \"{alias.name}\" definida como padrão.");
                }
                SaveAppSettings();
                lstBases.Items.Refresh();
                RefreshCbBaseColors();
            }
        }

        private void UpdateFavoritoIcon(bool isDefault)
        {
            if (isDefault)
            {
                iconFavorito.Text = "\uE735";
                iconFavorito.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 193, 7));
                iconFavorito.Opacity = 1.0;
            }
            else
            {
                iconFavorito.Text = "\uE734";
                iconFavorito.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(128, 128, 128));
                iconFavorito.Opacity = 0.7;
            }
        }

        private void UpdateDefaultIconOnSelectedClient()
        {
            string selected = cbPerfis.SelectedItem?.ToString() ?? string.Empty;
            UpdateFavoritoIcon(!string.IsNullOrEmpty(selected) && selected == _appSettings.DefaultClient);
        }

        private void RefreshDefaultSelectors()
        {
            if (cbDefaultClient == null || cbDefaultBase == null) return;

            _isSyncing = true;
            try
            {
                cbDefaultClient.SelectionChanged -= cbDefaultClient_SelectionChanged;
                cbDefaultBase.SelectionChanged -= cbDefaultBase_SelectionChanged;
                try
                {
                    cbDefaultClient.Items.Clear();
                    foreach (var key in profiles.Keys) cbDefaultClient.Items.Add(key);

                    string clientForBase = !string.IsNullOrEmpty(_appSettings.DefaultClient) && profiles.ContainsKey(_appSettings.DefaultClient)
                        ? _appSettings.DefaultClient
                        : string.Empty;

                    PopulateDefaultBase(clientForBase, _appSettings.DefaultBaseId);

                    if (!string.IsNullOrEmpty(_appSettings.DefaultClient) && profiles.ContainsKey(_appSettings.DefaultClient))
                        cbDefaultClient.SelectedItem = _appSettings.DefaultClient;
                }
                finally
                {
                    cbDefaultClient.SelectionChanged += cbDefaultClient_SelectionChanged;
                    cbDefaultBase.SelectionChanged += cbDefaultBase_SelectionChanged;
                }
            }
            finally
            {
                _isSyncing = false;
            }
        }

        private void PopulateDefaultBase(string clientName, string preselectId)
        {
            cbDefaultBase.Items.Clear();

            if (string.IsNullOrEmpty(clientName))
            {
                cbDefaultBase.IsEnabled = false;
                cbDefaultBase.SelectedItem = null;
                return;
            }

            var clientAliases = aliases.Where(a => a.client.Equals(clientName, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var a in clientAliases) cbDefaultBase.Items.Add(a);

            cbDefaultBase.IsEnabled = clientAliases.Count > 0;

            if (!string.IsNullOrEmpty(preselectId))
            {
                var defAlias = clientAliases.FirstOrDefault(a => a.id == preselectId);
                if (defAlias != null) cbDefaultBase.SelectedItem = defAlias;
            }
        }

        private void cbDefaultClient_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncing) return;
            string selected = cbDefaultClient.SelectedItem?.ToString() ?? string.Empty;
            string prevBaseId = _appSettings.DefaultBaseId;
            _appSettings.DefaultClient = selected;

            if (string.IsNullOrEmpty(selected))
            {
                _appSettings.DefaultBaseId = string.Empty;
            }
            else
            {
                var existing = !string.IsNullOrEmpty(prevBaseId) ? aliases.FirstOrDefault(a => a.id == prevBaseId) : null;
                if (existing == null || !existing.client.Equals(selected, StringComparison.OrdinalIgnoreCase))
                    _appSettings.DefaultBaseId = string.Empty;
            }

            PopulateDefaultBase(selected, _appSettings.DefaultBaseId);
            SaveAppSettings();
            UpdateDefaultIconOnSelectedClient();
            AddLog("info", string.IsNullOrEmpty(selected) ? "Cliente padrão removido." : $"Cliente padrão: {selected}");
        }

        private void cbDefaultBase_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncing) return;
            if (cbDefaultBase.SelectedItem is AliasConfig alias)
            {
                _appSettings.DefaultBaseId = alias.id;
                SaveAppSettings();
                AddLog("info", $"Base padrão: {alias.name}");
            }
        }

        private void btnLimparPadrao_Click(object sender, RoutedEventArgs e)
        {
            _appSettings.DefaultClient = string.Empty;
            _appSettings.DefaultBaseId = string.Empty;
            SaveAppSettings();
            RefreshDefaultSelectors();
            UpdateDefaultIconOnSelectedClient();
            AddLog("info", "Padrão removido. O app vai abrir no último usado.");
        }

        // ---------------------------------------------------------------
        // Apply Default (or Last) client+base on startup
        // ---------------------------------------------------------------
        private void ApplyDefaultOrLast()
        {
            if (profiles.Count == 0) return;

            string clientToLoad = !string.IsNullOrEmpty(_appSettings.DefaultClient) && profiles.ContainsKey(_appSettings.DefaultClient)
                ? _appSettings.DefaultClient
                : (!string.IsNullOrEmpty(_appSettings.LastClient) && profiles.ContainsKey(_appSettings.LastClient)
                    ? _appSettings.LastClient
                    : profiles.Keys.FirstOrDefault() ?? string.Empty);

            if (string.IsNullOrEmpty(clientToLoad)) return;

            cbPerfis.SelectionChanged -= cbPerfis_SelectionChanged;
            cbClienteAtivo.SelectionChanged -= cbClienteAtivo_SelectionChanged;
            try
            {
                cbPerfis.SelectedItem = clientToLoad;
                cbClienteAtivo.SelectedItem = clientToLoad;
            }
            finally
            {
                cbPerfis.SelectionChanged += cbPerfis_SelectionChanged;
                cbClienteAtivo.SelectionChanged += cbClienteAtivo_SelectionChanged;
            }

            if (profiles.TryGetValue(clientToLoad, out var profile))
            {
                LoadProfileToUI(profile);
            }

            // Apply base: default > last > profile.Alias
            string baseIdToLoad = !string.IsNullOrEmpty(_appSettings.DefaultBaseId) && aliases.Any(a => a.id == _appSettings.DefaultBaseId)
                ? _appSettings.DefaultBaseId
                : (!string.IsNullOrEmpty(_appSettings.LastBaseId) && aliases.Any(a => a.id == _appSettings.LastBaseId)
                    ? _appSettings.LastBaseId
                    : string.Empty);

            if (!string.IsNullOrEmpty(baseIdToLoad))
            {
                var alias = aliases.FirstOrDefault(a => a.id == baseIdToLoad);
                if (alias != null)
                {
                    cbBase.SelectionChanged -= cbBase_SelectionChanged;
                    cbAliasDB.SelectionChanged -= cbAliasDB_SelectionChanged;
                    try
                    {
                        cbBase.SelectedItem = alias;
                        cbAliasDB.SelectedItem = alias.name;
                    }
                    finally
                    {
                        cbBase.SelectionChanged += cbBase_SelectionChanged;
                        cbAliasDB.SelectionChanged += cbAliasDB_SelectionChanged;
                    }
                }
            }
        }

        // ---------------------------------------------------------------
        // Feature 6: Export specific client
        // ---------------------------------------------------------------
        private void btnExportarCliente_Click(object sender, RoutedEventArgs e)
        {
            string selectedName = cbPerfis.SelectedItem?.ToString() ?? string.Empty;
            if (string.IsNullOrEmpty(selectedName) || !profiles.TryGetValue(selectedName, out var profile))
            {
                MessageBox.Show("Selecione um cliente para exportar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = $"Exportar Cliente - {selectedName}",
                    Filter = "JSON|*.json",
                    FileName = $"{selectedName}.json"
                };
                if (dlg.ShowDialog(this) != true) return;

                var clientAliases = aliases.Where(a => a.client.Equals(selectedName, StringComparison.OrdinalIgnoreCase)).ToList();

                var data = new ExportData
                {
                    Profiles = new List<ProfileSettings> { profile },
                    Aliases = clientAliases
                };
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(dlg.FileName, json);
                AddLog("info", $"Cliente \"{selectedName}\" exportado: {dlg.FileName}");
                MessageBox.Show($"Cliente \"{selectedName}\" exportado com sucesso!", "Exportar", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AddLog("error", $"Erro ao exportar cliente: {ex.Message}");
                MessageBox.Show($"Erro ao exportar: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void lstBases_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstBases.SelectedItem is AliasConfig selectedAlias)
            {
                stackDetailsEditor.Visibility = Visibility.Visible;
                borderActionBar.Visibility = Visibility.Visible;

                txtDbAliasName.Text = selectedAlias.name;
                if (selectedAlias.dbType.Equals("oracle", StringComparison.OrdinalIgnoreCase))
                {
                    rbOracle.IsChecked = true;
                }
                else
                {
                    rbSql.IsChecked = true;
                }

                // Force visibility toggle logic
                UpdateDbBaseNameVisibility();

                cbClienteAssociado.SelectedItem = selectedAlias.client;
                cbDbVersion.SelectedItem = selectedAlias.dbVersion;

                txtDbBaseName.Text = selectedAlias.Base;
                txtDbServer.Text = selectedAlias.server;
                txtDbUser.Text = selectedAlias.dbUser;
                pbDbPass.Password = selectedAlias.dbPass;
                txtRmUser.Text = selectedAlias.rmUser;
                pbRmPass.Password = selectedAlias.rmPass;

                chkRunService.IsChecked = selectedAlias.runService;
                chkJobProcessing.IsChecked = selectedAlias.jobProcessing;
                chkLocalOnly.IsChecked = selectedAlias.localOnly;
                chkProcessPool.IsChecked = selectedAlias.processPool;
                txtMaxThreads.Text = selectedAlias.maxThreads.ToString();

                // Update color tag selector
                UpdateColorTagSelectorUI(selectedAlias.TagColor);
                RefreshBaseListColorDots();
            }
            else
            {
                stackDetailsEditor.Visibility = Visibility.Collapsed;
                borderActionBar.Visibility = Visibility.Collapsed;
            }
        }

        private void btnOrdenarBases_Click(object sender, RoutedEventArgs e)
        {
            _sortBasesAsc = !_sortBasesAsc;
            txtSortIcon.Text = _sortBasesAsc ? "A-Z" : "Z-A";

            var sorted = _sortBasesAsc
                ? filteredAliases.OrderBy(a => a.name).ToList()
                : filteredAliases.OrderByDescending(a => a.name).ToList();

            filteredAliases.Clear();
            foreach (var a in sorted)
                filteredAliases.Add(a);
        }

        private void btnNovaBase_Click(object sender, RoutedEventArgs e)
        {
            string activeClient = cbPerfis.SelectedItem?.ToString() ?? cbClienteAtivo.SelectedItem?.ToString() ?? "Cliente Padrão";
            var newAlias = new AliasConfig
            {
                id = DateTime.Now.Ticks.ToString(),
                name = "Nova Conexão",
                Base = "CorporeRM",
                client = activeClient,
                server = "localhost",
                dbType = "sql",
                dbUser = "sa",
                dbPass = "totvs",
                rmUser = "mestre",
                rmPass = "totvs"
            };

            aliases.Add(newAlias);
            SaveAliases();
            UpdateFilteredAliasesList();
            lstBases.SelectedItem = newAlias;
            
            if (cbClienteAtivo.SelectedItem != null)
            {
                UpdateAliasesUI(cbClienteAtivo.SelectedItem.ToString()!);
            }
            AddLog("info", $"Novo Alias adicionado para o cliente \"{activeClient}\".");
        }

        private void btnSalvarBase_Click(object sender, RoutedEventArgs e)
        {
            if (lstBases.SelectedItem is AliasConfig selectedAlias)
            {
                string name = txtDbAliasName.Text.Trim();
                if (string.IsNullOrEmpty(name))
                {
                    MessageBox.Show("Por favor, digite um nome para o alias.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                selectedAlias.name = name;
                selectedAlias.dbType = rbOracle.IsChecked == true ? "oracle" : "sql";
                selectedAlias.Base = txtDbBaseName.Text.Trim();
                selectedAlias.server = txtDbServer.Text.Trim();
                selectedAlias.dbUser = txtDbUser.Text.Trim();
                selectedAlias.dbPass = pbDbPass.Password.Trim();
                selectedAlias.rmUser = txtRmUser.Text.Trim();
                selectedAlias.rmPass = pbRmPass.Password.Trim();
                selectedAlias.client = cbClienteAssociado.SelectedItem?.ToString() ?? selectedAlias.client;
                selectedAlias.dbVersion = cbDbVersion.SelectedItem?.ToString() ?? selectedAlias.dbVersion;

                selectedAlias.runService = chkRunService.IsChecked == true;
                selectedAlias.jobProcessing = chkJobProcessing.IsChecked == true;
                // Save TagColor from color selector
                selectedAlias.localOnly = chkLocalOnly.IsChecked == true;
                selectedAlias.processPool = chkProcessPool.IsChecked == true;

                int maxThreads = 0;
                int.TryParse(txtMaxThreads.Text.Trim(), out maxThreads);
                selectedAlias.maxThreads = maxThreads;

                SaveAliases();
                
                // Refresh list display
                lstBases.Items.Refresh();

                if (cbClienteAtivo.SelectedItem != null)
                {
                    string activeClient = cbClienteAtivo.SelectedItem.ToString()!;
                    UpdateAliasesUI(activeClient);
                }

                UpdateFilteredAliasesList();

                AddLog("info", $"Alias \"{name}\" atualizado com sucesso.");
                MessageBox.Show($"Alias \"{name}\" atualizado com sucesso!", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void btnDuplicarBase_Click(object sender, RoutedEventArgs e)
        {
            if (lstBases.SelectedItem is AliasConfig selectedAlias)
            {
                var newAlias = new AliasConfig
                {
                    id = DateTime.Now.Ticks.ToString(),
                    name = selectedAlias.name + " (cópia)",
                    Base = selectedAlias.Base,
                    client = selectedAlias.client,
                    server = selectedAlias.server,
                    dbType = selectedAlias.dbType,
                    dbUser = selectedAlias.dbUser,
                    dbPass = selectedAlias.dbPass,
                    rmUser = selectedAlias.rmUser,
                    rmPass = selectedAlias.rmPass,
                    runService = selectedAlias.runService,
                    jobProcessing = selectedAlias.jobProcessing,
                    localOnly = selectedAlias.localOnly,
                    processPool = selectedAlias.processPool,
                    maxThreads = selectedAlias.maxThreads,
                    dbVersion = selectedAlias.dbVersion,
                    TagColor = selectedAlias.TagColor
                };
                aliases.Add(newAlias);
                SaveAliases();
                UpdateFilteredAliasesList();
                lstBases.SelectedItem = newAlias;

                if (cbClienteAtivo.SelectedItem != null)
                    UpdateAliasesUI(cbClienteAtivo.SelectedItem.ToString()!);

                AddLog("info", $"Alias duplicado: \"{newAlias.name}\".");
            }
        }

        private void btnTestarConexao_Click(object sender, RoutedEventArgs e)
        {
            string server = txtDbServer.Text.Trim();
            string type = rbOracle.IsChecked == true ? "oracle" : "sql";
            if (string.IsNullOrEmpty(server))
            {
                MessageBox.Show("Por favor, digite o servidor de banco.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            TestDbConnection(server, type);
        }

        private void UpdateDbBaseNameVisibility()
        {
            if (lblDbBaseName == null || txtDbBaseName == null || rbSql == null) return;
            
            if (rbSql.IsChecked == true)
            {
                lblDbBaseName.Visibility = Visibility.Visible;
                txtDbBaseName.Visibility = Visibility.Visible;
            }
            else
            {
                lblDbBaseName.Visibility = Visibility.Collapsed;
                txtDbBaseName.Visibility = Visibility.Collapsed;
            }
        }

        private void rbDbProvider_Checked(object sender, RoutedEventArgs e)
        {
            UpdateDbBaseNameVisibility();
        }

        private void btnDeletarBase_Click(object sender, RoutedEventArgs e)
        {
            if (lstBases.SelectedItem is AliasConfig selectedAlias)
            {
                var result = MessageBox.Show($"Deseja realmente excluir a base/alias \"{selectedAlias.name}\"?", "Confirmar Exclusão", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.No) return;

                string deletedName = selectedAlias.name;

                // Find by ID to make it 100% robust
                var aliasToRemove = aliases.FirstOrDefault(a => a.id == selectedAlias.id);
                if (aliasToRemove != null)
                {
                    aliases.Remove(aliasToRemove);
                    SaveAliases();
                }

                // Clear selection state
                lstBases.SelectedItem = null;

                // Refresh main client selectors dropdown lists
                string activeClient = cbPerfis.SelectedItem?.ToString() ?? cbClienteAtivo.SelectedItem?.ToString() ?? string.Empty;
                if (!string.IsNullOrEmpty(activeClient))
                {
                    UpdateAliasesUI(activeClient);
                }

                // Refresh active list of aliases
                UpdateFilteredAliasesList();

                AddLog("info", $"Alias \"{deletedName}\" removido.");
                MessageBox.Show($"Alias \"{deletedName}\" excluído com sucesso.", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void TestDbConnection(string serverAddress, string dbType)
        {
            try
            {
                string host = serverAddress;
                int port = dbType.Equals("oracle", StringComparison.OrdinalIgnoreCase) ? 1521 : 1433;

                // Extrai host:port de vários formatos sem perder o instance name
                if (host.Contains("/") && !host.Contains("\\"))
                {
                    var parts = host.Split('/');
                    host = parts[0].Trim();
                }

                if (host.Contains(","))
                {
                    var parts = host.Split(',');
                    host = parts[0].Trim();
                    int.TryParse(parts[1].Trim(), out port);
                }
                else if (host.Contains(":"))
                {
                    var parts = host.Split(':');
                    host = parts[0].Trim();
                    int.TryParse(parts[1].Trim(), out port);
                }

                AddLog("info", $"Testando conexão de rede com {host}:{port}...");
                
                using (var tcpClient = new System.Net.Sockets.TcpClient())
                {
                    var result = tcpClient.BeginConnect(host, port, null, null);
                    var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(3));
                    if (success)
                    {
                        tcpClient.EndConnect(result);
                        AddLog("info", $"✅ Rede OK: {host} respondendo na porta {port}.");
                        MessageBox.Show($"Sucesso! Rede OK: {host} respondendo na porta {port}.", "Teste de Conexão", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        AddLog("error", $"❌ Erro: Conexão expirou em {host}:{port}.");
                        MessageBox.Show($"Erro: Tempo limite esgotado. Verifique se o servidor {host} está respondendo na porta {port}.", "Teste de Conexão", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog("error", $"❌ Erro ao testar rede: {ex.Message}");
                MessageBox.Show($"Erro ao testar rede: {ex.Message}", "Teste de Conexão", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnImportarAliases_Click(object sender, RoutedEventArgs e)
        {
            if (_isOperationRunning) return;
            SetLoadingState(true);
            try
            {
                ImportarAliasesDoDisco();
                MessageBox.Show("Importação de Aliases concluída!", "Importar Aliases", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private void btnImportarAmbientes_Click(object sender, RoutedEventArgs e)
        {
            if (_isOperationRunning) return;
            SetLoadingState(true);
            try
            {
                ImportarAmbientesDoDisco();
                MessageBox.Show("Importação de Ambientes concluída!", "Importar Ambientes", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private void ImportarAliasesDoDisco()
        {
            var possiveisCaminhos = new List<string>();
            possiveisCaminhos.Add(@"C:\totvs\CorporeRM\RM.Net\Alias.dat");
            // Scan all version folders under C:\RM\Legado
            try
            {
                if (Directory.Exists(@"C:\RM\Legado"))
                {
                    foreach (var dir in Directory.GetDirectories(@"C:\RM\Legado"))
                    {
                        string binAlias = Path.Combine(dir, "Bin", "Alias.dat");
                        if (File.Exists(binAlias))
                            possiveisCaminhos.Add(binAlias);
                    }
                }
            }
            catch { /* ignore access errors */ }
            var pathsArray = possiveisCaminhos.ToArray();

            foreach (var path in pathsArray)
            {
                if (!File.Exists(path)) continue;

                try
                {
                    var xml = XDocument.Load(path);
                    var root = xml.Root;
                    if (root == null) continue;

                    var dbConfig = root.Element("DbConfig");
                    if (dbConfig == null) continue;

                    var servidor = dbConfig.Element("DbServer")?.Value ?? "";
                    var dbType = dbConfig.Element("DbType")?.Value ?? "SqlServer";
                    var dbProvider = dbConfig.Element("DbProvider")?.Value ?? "SqlClient";
                    var dbName = dbConfig.Element("DbName")?.Value ?? "";
                    var usuario = dbConfig.Element("UserName")?.Value ?? "";
                    var senha = dbConfig.Element("Password")?.Value ?? "";
                    var runService = dbConfig.Element("RunService")?.Value == "true";
                    var jobEnabled = dbConfig.Element("JobServerEnabled")?.Value == "true";
                    var maxThreads = int.Parse(dbConfig.Element("JobServerMaxThreads")?.Value ?? "0");
                    var localOnly = dbConfig.Element("JobServerLocalOnly")?.Value == "true";
                    var processPool = dbConfig.Element("JobServerProcessPoolEnabled")?.Value == "true";

                    var nomeAlias = Path.GetFileName(Path.GetDirectoryName(path)) ?? "Importado";
                    var nomeBase = string.IsNullOrEmpty(dbName) ? "CorporeRM" : dbName;

                    var alias = new AliasConfig
                    {
                        id = DateTime.Now.Ticks.ToString() + new Random().Next(1000),
                        name = $"{nomeBase} ({nomeAlias})",
                        Base = "CorporeRM",  // SEMPRE CorporeRM
                        client = "Importado",
                        server = servidor,
                        dbType = dbType.Equals("Oracle", StringComparison.OrdinalIgnoreCase) ? "oracle" : "sql",
                        dbUser = usuario,
                        dbPass = senha,
                        rmUser = "mestre",
                        rmPass = "totvs",
                        runService = runService,
                        jobProcessing = jobEnabled,
                        localOnly = localOnly,
                        processPool = processPool,
                        maxThreads = maxThreads,
                        dbVersion = ExtrairVersaoDoCaminho(path)
                    };

                    if (!aliases.Any(a => a.server == servidor && a.dbUser == usuario))
                    {
                        aliases.Add(alias);
                        AddLog("info", $"Alias importado: {alias.name} ({servidor})");
                    }
                }
                catch (Exception ex)
                {
                    AddLog("error", $"Erro ao importar Alias.dat de {path}: {ex.Message}");
                }
            }

            SaveAliases();
            UpdateFilteredAliasesList();
        }

        private void ImportarAmbientesDoDisco()
        {
            string legadoPath = @"C:\RM\Legado";
            if (!Directory.Exists(legadoPath)) return;

            foreach (var versaoDir in Directory.GetDirectories(legadoPath))
            {
                string nomeVersao = Path.GetFileName(versaoDir);
                string binPath = Path.Combine(versaoDir, "Bin");
                if (!Directory.Exists(binPath)) continue;

                string nomePerfil = $"RM {nomeVersao}";
                if (profiles.ContainsKey(nomePerfil)) continue;

                var profile = new ProfileSettings
                {
                    Name = nomePerfil,
                    RmVersion = nomeVersao,
                    Alias = "CorporeRM",
                    AutoLogin = false,
                    DelBroker = false,
                    VerboseLogs = true,
                    ApagarHost = false
                };

                profiles[nomePerfil] = profile;
                AddLog("info", $"Ambiente importado: {nomePerfil}");
            }

            SaveProfiles();
            UpdateProfilesUI();
        }

        private string ExtrairVersaoDoCaminho(string path)
        {
            var match = System.Text.RegularExpressions.Regex.Match(path, @"(\d+\.\d+\.\d+)");
            return match.Success ? match.Value : "12.1.2602";
        }

        // ---------------------------------------------------------------
        // Fase 3 — New handlers
        // ---------------------------------------------------------------

        private void btnInstalarDualHost_Click(object sender, RoutedEventArgs e)
        {
            if (_isOperationRunning) return;
            SetLoadingState(true);
            try
            {
                string binDir = GetBinDirectory();
                var svc = new SystemService();
                var (success, msg) = svc.InstallDualHost(binDir);
                AddLog(success ? "info" : "error", $"[Dual Host] {msg}");
                MessageBox.Show(msg, "Instalar Dual Host",
                    MessageBoxButton.OK,
                    success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                AddLog("error", $"[Dual Host] Erro inesperado: {ex.Message}");
                MessageBox.Show($"Erro ao instalar Dual Host: {ex.Message}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private void btnValidarDLLs_Click(object sender, RoutedEventArgs e)
        {
            if (_isOperationRunning) return;
            SetLoadingState(true);
            try
            {
                string binDir = GetBinDirectory();
                if (string.IsNullOrEmpty(binDir))
                {
                    AddLog("error", "Pasta de instalação do RM não configurada. Rode o wizard pela aba Sobre.");
                    MessageBox.Show("A pasta de instalação do RM não foi encontrada.\n\nAbra a aba Sobre e clique em 'Reconfigurar' (wizard) para apontar a pasta correta.", "RM não encontrado", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                string customPath = Path.Combine(binDir, "Custom");
                var svc = new SystemService();
                var (total, invalidas, semPrefixo) = svc.ValidarCustomDLLs(customPath);

                if (total == 0 && !System.IO.Directory.Exists(customPath))
                {
                    AddLog("error", $"[Validar DLLs] Pasta Custom não encontrada: {customPath}");
                    MessageBox.Show($"Pasta Custom não encontrada:\n{customPath}",
                        "Validar Custom DLLs", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                foreach (var dll in semPrefixo)
                    AddLog("warn", $"[Validar DLLs] DLL sem prefixo RM.Cst. ou RM.: {dll}");

                AddLog("info", $"[Validar DLLs] {total} DLLs encontradas, {invalidas} sem prefixo.");

                string detail = invalidas > 0
                    ? $"\n\nDLLs sem prefixo:\n" + string.Join("\n", semPrefixo.Select(d => $"  • {d}"))
                    : string.Empty;

                MessageBox.Show(
                    $"Validação de Custom DLLs:\n\n• Total: {total}\n• Sem prefixo RM. ou RM.Cst.: {invalidas}{detail}",
                    "Validar Custom DLLs", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AddLog("error", $"[Validar DLLs] Erro: {ex.Message}");
                MessageBox.Show($"Erro ao validar DLLs: {ex.Message}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        // ---------------------------------------------------------------
        // Import / Export
        // ---------------------------------------------------------------

        private class ExportData
        {
            public List<ProfileSettings> Profiles { get; set; } = new();
            public List<AliasConfig> Aliases { get; set; } = new();
        }

        private void btnExportar_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Exportar Configurações",
                    Filter = "JSON|*.json",
                    FileName = "rmcore_config.json"
                };
                if (dlg.ShowDialog(this) != true) return;

                var data = new ExportData
                {
                    Profiles = profiles.Values.ToList(),
                    Aliases = aliases.ToList()
                };
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(dlg.FileName, json);
                AddLog("info", $"Configurações exportadas: {dlg.FileName}");
                MessageBox.Show("Configurações exportadas com sucesso!", "Exportar", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AddLog("error", $"Erro ao exportar: {ex.Message}");
            }
        }

        private void btnImportar_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Importar Configurações",
                    Filter = "JSON|*.json",
                    FileName = "rmcore_config.json"
                };
                if (dlg.ShowDialog(this) != true) return;

                var json = File.ReadAllText(dlg.FileName);
                var data = JsonSerializer.Deserialize<ExportData>(json);
                if (data == null) throw new Exception("Arquivo inválido.");

                var result = MessageBox.Show(
                    $"Importar {data.Profiles.Count} cliente(s) e {data.Aliases.Count} base(s)?\nOs dados atuais serão substituídos.",
                    "Confirmar Importação", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes) return;

                // Substitui dados
                profiles.Clear();
                foreach (var p in data.Profiles) profiles[p.Name] = p;

                aliases.Clear();
                foreach (var a in data.Aliases) aliases.Add(a);

                SaveProfiles();
                SaveAliases();
                UpdateProfilesUI();
                UpdateFilteredAliasesList();

                if (profiles.Count > 0)
                    LoadProfileToUI(profiles.Values.First());

                AddLog("info", $"Configurações importadas de: {dlg.FileName}");
                MessageBox.Show("Configurações importadas com sucesso!", "Importar", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AddLog("error", $"Erro ao importar: {ex.Message}");
                MessageBox.Show($"Erro ao importar: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnResetFabrica_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Tem certeza? Todos os dados (clientes, bases, configuracoes) serao perdidos.", "Reset de Fabrica", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RM_Core");

                string[] filesToDelete =
                {
                    "rmcore.db", "rmcore.db-shm", "rmcore.db-wal",
                    "app_settings.json", "window_settings.json",
                    "profiles.json", "aliases.json"
                };

                foreach (var file in filesToDelete)
                {
                    string path = Path.Combine(appData, file);
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                        AddLog("info", $"Arquivo removido: {file}");
                    }
                }

                AddLog("info", "Dados resetados com sucesso.");
                MessageBox.Show("Dados resetados. Reinicie o aplicativo.", "Reset de Fabrica", MessageBoxButton.OK, MessageBoxImage.Information);

                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                AddLog("error", $"Erro ao resetar dados: {ex.Message}");
                MessageBox.Show($"Erro ao resetar dados: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task CheckForUpdatesAsync()
        {
            try
            {
                var svc  = new UpdateService();
                var info = await svc.CheckForUpdates();

                if (info != null)
                {
                    _pendingUpdate     = info;
                    _updateCheckFailed = false;
                    Dispatcher.Invoke(() =>
                    {
                        _trayService.ShowToast(
                            "Atualização disponível!",
                            $"Versão {info.Version} disponível. Acesse a aba Sobre para baixar.");
                        AddLog("info", $"[Auto-Update] Nova versão disponível: {info.Version}");
                        if (gridTabSobre.Visibility == Visibility.Visible)
                            AtualizarPanelVersao();
                    });
                }
                else
                {
                    _pendingUpdate     = null;
                    _updateCheckFailed = false;
                }
            }
            catch
            {
                _pendingUpdate     = null;
                _updateCheckFailed = true;
            }
            finally
            {
                _updateCheckDone = true;
                Dispatcher.Invoke(AtualizarPanelVersao);
            }
        }

        private void AtualizarPanelVersao()
        {
            if (txtVersaoPanel == null || txtVersionStatus == null) return;

            txtVersaoPanel.Text = "v1.0.0";

            if (_updateCheckFailed)
            {
                txtVersionStatus.Text = "Erro ao verificar atualizações.";
                txtVersionStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 77, 79));
                bordaNovaVersao.Visibility = Visibility.Collapsed;
            }
            else if (_pendingUpdate != null)
            {
                txtVersionStatus.Text = "Nova versão disponível!";
                txtVersionStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129));
                txtUpdateVersion.Text = $"v{_pendingUpdate.Version} — Baixar agora";
                bordaNovaVersao.Visibility = Visibility.Visible;
            }
            else if (_updateCheckDone)
            {
                txtVersionStatus.Text = "Você está na versão mais recente.";
                txtVersionStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129));
                bordaNovaVersao.Visibility = Visibility.Collapsed;
            }
            else
            {
                txtVersionStatus.Text = "Verificando...";
                txtVersionStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(128, 128, 128));
                bordaNovaVersao.Visibility = Visibility.Collapsed;
            }
        }

        private async void btnCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            _updateCheckDone = false;
            _pendingUpdate   = null;
            _updateCheckFailed = false;
            AtualizarPanelVersao();
            await CheckForUpdatesAsync();
        }

        private async void btnGithubProfile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/senamiguel",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AddLog("error", $"Erro ao abrir GitHub: {ex.Message}");
            }
        }

        private void btnBaixarUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingUpdate != null && !string.IsNullOrEmpty(_pendingUpdate.DownloadUrl))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = _pendingUpdate.DownloadUrl,
                        UseShellExecute = true
                    });
                    AddLog("info", $"Abrindo download: {_pendingUpdate.DownloadUrl}");
                }
                catch (Exception ex)
                {
                    AddLog("error", $"Erro ao abrir download: {ex.Message}");
                }
            }
        }
    }

    /// <summary>Simple DTO for persisting window position and size to disk.</summary>
    internal class WindowSettings
    {
        public double Left   { get; set; }
        public double Top    { get; set; }
        public double Width  { get; set; }
        public double Height { get; set; }
    }

    /// <summary>DTO para configurações gerais do app (toggle de comportamento, etc).</summary>
    internal class AppSettings
    {
        public bool   CloseMinimizesToTray { get; set; } = true;
        public bool   StartWithWindows     { get; set; } = false;
        public bool   StartMinimized       { get; set; } = false;
        public bool   FirstRunComplete     { get; set; } = false;
        public string RmInstallPath        { get; set; } = string.Empty; // pasta <versao>\Bin
        public List<string> FavoriteClientNames { get; set; } = new();
        public List<string> BaseFavoriteIds { get; set; } = new();
        public Dictionary<string, string> BaseTagColors { get; set; } = new();
        public string DefaultClient  { get; set; } = string.Empty;
        public string DefaultBaseId  { get; set; } = string.Empty;
        public string LastClient     { get; set; } = string.Empty;
        public string LastBaseId     { get; set; } = string.Empty;
    }

    public class LogEntry
    {
        public DateTime Time { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;

        public string TimeFormatted => $"[{Time:HH:mm:ss}]";
        public string ColorBrush => Type switch
        {
            "error" or "stderr" => "#ff4d4f",
            "info" => "#4caf50",
            "stdout" => "#5ba3d9",
            _ => "#7a8a99"
        };
    }

    public class ProfileSettings
    {
        [System.Text.Json.Serialization.JsonPropertyName("profileName")]
        public string Name { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("rmVersion")]
        public string RmVersion { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("alias")]
        public string Alias { get; set; } = "CorporeRM";
        [System.Text.Json.Serialization.JsonPropertyName("autoLogin")]
        public bool AutoLogin { get; set; } = true;
        [System.Text.Json.Serialization.JsonPropertyName("delBroker")]
        public bool DelBroker { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("verboseLogs")]
        public bool VerboseLogs { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("apagarHost")]
        public bool ApagarHost { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("normalizePath")]
        public bool NormalizePath { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("enableProcessIsolation")]
        public bool EnableProcessIsolation { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("jobServer3Camadas")]
        public bool JobServer3Camadas { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("enableCompression")]
        public bool EnableCompression { get; set; }
        // Favorito
        public bool IsFavorite { get; set; }
    }

    public class AliasConfig
    {
        public string id { get; set; } = string.Empty;
        public string name { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("base")]
        public string Base { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("client")]
        public string client { get; set; } = string.Empty;
        public string server { get; set; } = string.Empty;
        public string dbType { get; set; } = string.Empty;
        public string dbUser { get; set; } = string.Empty;
        public string dbPass { get; set; } = string.Empty;
        public string rmUser { get; set; } = string.Empty;
        public string rmPass { get; set; } = string.Empty;
        public bool runService { get; set; } = true;
        public bool jobProcessing { get; set; }
        public bool localOnly { get; set; }
        public bool processPool { get; set; }
        public int maxThreads { get; set; }
        public string dbVersion { get; set; } = string.Empty;
        // Extras
        public bool IsFavorite { get; set; }
        public string TagColor { get; set; } = ""; // "green","yellow","red",""
    }
}
