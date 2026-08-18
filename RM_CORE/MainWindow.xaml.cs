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
using RM_Core.Services.Telemetry;

namespace RM_Core
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private System.Collections.ObjectModel.ObservableCollection<LogEntry> logs = new System.Collections.ObjectModel.ObservableCollection<LogEntry>();

        private System.Collections.Generic.Dictionary<string, ProfileSettings> profiles = new System.Collections.Generic.Dictionary<string, ProfileSettings>();
        private string profilesFilePath = System.IO.Path.Combine(GetAppDataDir(), "profiles.json");
        private string aliasesFilePath = System.IO.Path.Combine(GetAppDataDir(), "aliases.json");
        private System.Collections.ObjectModel.ObservableCollection<AliasConfig> aliases = new System.Collections.ObjectModel.ObservableCollection<AliasConfig>();
        private System.Collections.ObjectModel.ObservableCollection<AliasConfig> filteredAliases = new System.Collections.ObjectModel.ObservableCollection<AliasConfig>();
        private bool _isSyncing = false;
        private bool _isOperationRunning = false;
        private bool _sortBasesAsc = true;
        private string _logSearchTerm = "";
        private string? _editingClientOriginalName = null;
        private bool _isCreatingNewClient = false;

        // TrayService — system tray / lifecycle management
        private TrayService _trayService = null!;

        // Telemetry — anonymous usage events
        private RM_Core.Services.Telemetry.TelemetryService? _telemetry;
        private DateTime _sessionStart = DateTime.UtcNow;

        // Pending update info (set by background check on startup)
        private UpdateInfo? _pendingUpdate;
        private bool _updateCheckDone = false;
        private bool _updateCheckFailed = false;

        public static string GetAppDataDir()
        {
            string? customDir = Environment.GetEnvironmentVariable("RMCORE_DATA_DIR");
            string dir = !string.IsNullOrEmpty(customDir)
                ? customDir
                : System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "RM_Core");
            if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
            return dir;
        }

        // Window state persistence path
        private readonly string _windowSettingsPath = System.IO.Path.Combine(GetAppDataDir(), "window_settings.json");

        // App settings (toggle persistido em JSON)
        private readonly string _appSettingsPath = System.IO.Path.Combine(GetAppDataDir(), "app_settings.json");
        private AppSettings _appSettings = new AppSettings();

        public bool IsExiting { get; set; } = false;

        private void ProcessPendingDeletes()
        {
            try
            {
                string appData = GetAppDataDir();
                string flagPath = System.IO.Path.Combine(appData, "rmcore.delete_on_startup");
                if (!System.IO.File.Exists(flagPath)) return;

                var paths = System.IO.File.ReadAllLines(flagPath);
                foreach (var p in paths)
                {
                    try
                    {
                        if (System.IO.File.Exists(p)) System.IO.File.Delete(p);
                    }
                    catch { /* ignore */ }
                }
                try { System.IO.File.Delete(flagPath); } catch { /* ignore */ }
            }
            catch { /* ignore */ }
        }

        public MainWindow()
        {
            _isSyncing = true;
            try
            {
                ProcessPendingDeletes();
                InitializeComponent();
                listLogs.ItemsSource = logs;
                logs.CollectionChanged += Logs_CollectionChanged;
                LoadAppSettings();
                InitializeSelectors();
                LoadAliases();
                InitializeBasesTab();
                LoadProfiles();

                _telemetry = new RM_Core.Services.Telemetry.TelemetryService(
                    installId: _appSettings.InstallId,
                    appVersion: "Alpha-0.6.7",
                    sinks: new List<RM_Core.Services.Telemetry.ITelemetrySink>
                    {
                        new RM_Core.Services.Telemetry.LocalFileTelemetrySink()
                    },
                    clientCountProvider: () => profiles.Count,
                    baseCountProvider: () => aliases.Count);
                _telemetry.Track("app_start", new Dictionary<string, object>
                {
                    ["first_run"] = !_appSettings.FirstRunComplete
                });

                // Initialize tray service (must come after InitializeComponent)
                _trayService = new TrayService(this);

                // Background update check — never blocks the UI thread
                _ = Task.Run(CheckForUpdatesAsync);

                // Restore window position/size from persisted settings
                Loaded += MainWindow_Loaded;

                // Populate service status on startup
                AtualizarStatusServicos();

                // Set version text dynamically to reference the control
                if (txtVersaoApp != null) txtVersaoApp.Text = "Versão Alpha-0.6.7";
            }
            finally
            {
                _isSyncing = false;
            }
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
                        var ambOwner = dbAmbientes.FirstOrDefault(a => a.Id == dbAl.AmbienteId);
                        string ambName = ambOwner?.Nome ?? string.Empty;
                        if (!aliases.Any(a => a.id == dbAl.Id.ToString() || (a.name.Equals(dbAl.Nome, StringComparison.OrdinalIgnoreCase) && a.client.Equals(ambName, StringComparison.OrdinalIgnoreCase))))
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
                        if (int.TryParse(alias.id, out int parsedId) && parsedId > 0)
                        {
                            aliasId = parsedId;
                        }
                        
                        var existing = (aliasId > 0 ? dbAliases.FirstOrDefault(a => a.Id == aliasId) : null)
                                       ?? dbAliases.FirstOrDefault(a => a.Nome.Equals(alias.name, StringComparison.OrdinalIgnoreCase) && a.AmbienteId == amb.Id);

                        if (existing != null)
                        {
                            alias.id = existing.Id.ToString();
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

        private void UpdateAliasesUI(string clientName, string? preselectedBaseName = null)
        {
            cbAliasDB.SelectionChanged -= cbAliasDB_SelectionChanged;
            cbBase.SelectionChanged -= cbBase_SelectionChanged;

            string previouslySelected = preselectedBaseName
                ?? (cbBase.SelectedItem is AliasConfig ac ? ac.name : cbBase.SelectedItem?.ToString())
                ?? (cbAliasDB.SelectedItem?.ToString())
                ?? (profiles.TryGetValue(clientName, out var p) ? p.Alias : string.Empty)
                ?? string.Empty;

            cbAliasDB.Items.Clear();
            cbBase.Items.Clear();

            AliasConfig? itemToSelect = null;

            foreach (var alias in aliases)
            {
                if (alias.client.Equals(clientName, StringComparison.OrdinalIgnoreCase))
                {
                    cbAliasDB.Items.Add(alias.name);
                    cbBase.Items.Add(alias);

                    if (!string.IsNullOrEmpty(previouslySelected) && alias.name.Equals(previouslySelected, StringComparison.OrdinalIgnoreCase))
                    {
                        itemToSelect = alias;
                    }
                }
            }

            if (itemToSelect == null && cbBase.Items.Count > 0)
            {
                itemToSelect = cbBase.Items[0] as AliasConfig;
            }

            if (itemToSelect != null)
            {
                cbBase.SelectedItem = itemToSelect;
                cbAliasDB.SelectedItem = itemToSelect.name;
            }

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

            if (!string.IsNullOrEmpty(selectedVersion) && profiles.TryGetValue(activeClient, out var prof))
            {
                prof.RmVersion = selectedVersion;
                SaveProfiles();
            }

            UpdateAliasesUI(activeClient);
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

            foreach (var kv in profiles)
                kv.Value.IsFavorite = _appSettings.FavoriteClientNames.Contains(kv.Key);
            foreach (var alias in aliases)
            {
                alias.IsFavorite = _appSettings.BaseFavoriteIds.Contains(alias.id);
                if (_appSettings.BaseTagColors.TryGetValue(alias.id, out var color))
                    alias.TagColor = color;
            }

            UpdateProfilesUI();
            ApplyDefaultOrLast();
        }

        private void SaveProfiles(string? oldName = null, string? newName = null)
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    db.Database.EnsureCreated();
                    
                    var dbAmbientes = db.Ambientes.ToList();
                    var dbConfigs = db.AmbienteConfigs.ToList();
                    
                    // If renaming, update the old entity name first so it doesn't get deleted
                    if (!string.IsNullOrEmpty(oldName) && !string.IsNullOrEmpty(newName) && oldName != newName)
                    {
                        var targetAmb = dbAmbientes.FirstOrDefault(a => a.Nome == oldName);
                        if (targetAmb != null)
                        {
                            targetAmb.Nome = newName;
                            targetAmb.FullName = newName;
                        }
                    }

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

        private void UpdateProfilesUI(string? preselectedName = null)
        {
            cbPerfis.SelectionChanged -= cbPerfis_SelectionChanged;
            cbClienteAtivo.SelectionChanged -= cbClienteAtivo_SelectionChanged;

            string selected = preselectedName 
                ?? cbPerfis.SelectedItem?.ToString() 
                ?? cbClienteAtivo.SelectedItem?.ToString() 
                ?? _appSettings.LastClient 
                ?? string.Empty;

            cbPerfis.Items.Clear();
            cbClienteAtivo.Items.Clear();
            cbClienteAssociado.Items.Clear();

            foreach (var key in profiles.Keys)
            {
                cbPerfis.Items.Add(key);
                cbClienteAtivo.Items.Add(key);
                cbClienteAssociado.Items.Add(key);
            }

            if (!string.IsNullOrEmpty(selected) && profiles.ContainsKey(selected))
            {
                cbPerfis.SelectedItem = selected;
                cbClienteAtivo.SelectedItem = selected;
                cbClienteAssociado.SelectedItem = selected;
            }
            else if (profiles.Count > 0)
            {
                string first = profiles.Keys.First();
                cbPerfis.SelectedItem = first;
                cbClienteAtivo.SelectedItem = first;
                cbClienteAssociado.SelectedItem = first;
            }

            cbPerfis.SelectionChanged += cbPerfis_SelectionChanged;
            cbClienteAtivo.SelectionChanged += cbClienteAtivo_SelectionChanged;

            UpdateDefaultIconOnSelectedClient();
            RefreshDefaultSelectors();
        }

        private void LoadProfileToUI(ProfileSettings profile)
        {
            if (_isSyncing) return;
            _isSyncing = true;

            try
            {
                _isCreatingNewClient = false;
                _editingClientOriginalName = profile.Name;
                txtNomePerfil.Text = profile.Name;
                cbVersaoRM.SelectedItem = profile.RmVersion;

                tsAutoLogin.IsOn = profile.AutoLogin;

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

                // Dynamically update and filter bases for this client!
                UpdateAliasesUI(profile.Name);

                // Sync default icon
                UpdateFavoritoIcon(!string.IsNullOrEmpty(_appSettings.DefaultClient) && _appSettings.DefaultClient == profile.Name);

                // Set selected base
                var matchBase = aliases.FirstOrDefault(a => a.name.Equals(profile.Alias, StringComparison.OrdinalIgnoreCase) && a.client.Equals(profile.Name, StringComparison.OrdinalIgnoreCase));
                if (matchBase != null)
                {
                    cbBase.SelectedItem = matchBase;
                    cbAliasDB.SelectedItem = matchBase.name;
                }
                else if (cbBase.Items.Count > 0)
                {
                    cbBase.SelectedIndex = 0;
                    if (cbBase.SelectedItem is AliasConfig first)
                        cbAliasDB.SelectedItem = first.name;
                }
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
                _isCreatingNewClient = false;
                _editingClientOriginalName = profile.Name;
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
                _isCreatingNewClient = false;
                _editingClientOriginalName = profile.Name;
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
                if (cbBase.SelectedItem is AliasConfig ac)
                {
                    cbAliasDB.SelectedItem = ac.name;
                    if (cbPerfis.SelectedItem != null && profiles.TryGetValue(cbPerfis.SelectedItem.ToString()!, out var p))
                    {
                        p.Alias = ac.name;
                        SaveProfiles();
                    }
                    _appSettings.LastBaseId = ac.id;
                    SaveAppSettings();
                    AddLog("info", $"Base \"{ac.name}\" selecionada.");
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
                string baseName = cbAliasDB.SelectedItem.ToString()!;
                string currentClient = cbPerfis.SelectedItem?.ToString() ?? cbClienteAtivo.SelectedItem?.ToString() ?? "";
                var match = aliases.FirstOrDefault(a => a.name == baseName && a.client.Equals(currentClient, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    cbBase.SelectedItem = match;
                    _appSettings.LastBaseId = match.id;
                    SaveAppSettings();
                }
                if (!string.IsNullOrEmpty(currentClient) && profiles.TryGetValue(currentClient, out var p))
                {
                    p.Alias = baseName;
                    SaveProfiles();
                }
                AddLog("info", $"Base \"{baseName}\" sincronizada.");
            }
            finally
            {
                _isSyncing = false;
            }
        }

        private void ts_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isSyncing) return;
            string activeClient = cbPerfis?.SelectedItem?.ToString()
                                  ?? cbClienteAtivo?.SelectedItem?.ToString()
                                  ?? txtNomePerfil?.Text?.Trim()
                                  ?? string.Empty;

            if (!string.IsNullOrEmpty(activeClient) && profiles.TryGetValue(activeClient, out var prof))
            {
                if (tsAutoLogin != null) prof.AutoLogin = tsAutoLogin.IsOn;
                if (tsVerboseLogs != null) prof.VerboseLogs = tsVerboseLogs.IsOn;
                if (tsApagarHost != null) prof.ApagarHost = tsApagarHost.IsOn;
                if (tsNormalizePath != null) prof.NormalizePath = tsNormalizePath.IsOn;
                if (tsEnableProcessIsolation != null) prof.EnableProcessIsolation = tsEnableProcessIsolation.IsOn;
                if (tsJobServer3Camadas != null) prof.JobServer3Camadas = tsJobServer3Camadas.IsOn;
                if (tsEnableCompression != null) prof.EnableCompression = tsEnableCompression.IsOn;

                // Sincroniza toggles na aba Início
                if (tsLimparBrokers != null) tsLimparBrokers.IsOn = prof.ApagarHost;
                if (tsLogsDetalhados != null) tsLogsDetalhados.IsOn = prof.VerboseLogs;

                SaveProfiles();
            }
        }

        private void ClientToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isSyncing) return;
            string activeClient = cbPerfis?.SelectedItem?.ToString() ?? cbClienteAtivo?.SelectedItem?.ToString() ?? string.Empty;
            if (string.IsNullOrEmpty(activeClient)) return;

            if (profiles.TryGetValue(activeClient, out var prof))
            {
                if (tsAutoLogin != null) prof.AutoLogin = tsAutoLogin.IsOn;
                if (tsVerboseLogs != null) prof.VerboseLogs = tsVerboseLogs.IsOn;
                if (tsApagarHost != null) prof.ApagarHost = tsApagarHost.IsOn;
                if (tsNormalizePath != null) prof.NormalizePath = tsNormalizePath.IsOn;
                if (tsEnableProcessIsolation != null) prof.EnableProcessIsolation = tsEnableProcessIsolation.IsOn;
                if (tsJobServer3Camadas != null) prof.JobServer3Camadas = tsJobServer3Camadas.IsOn;
                if (tsEnableCompression != null) prof.EnableCompression = tsEnableCompression.IsOn;

                // Sincroniza toggles na aba Início
                if (tsLimparBrokers != null) tsLimparBrokers.IsOn = prof.ApagarHost;
                if (tsLogsDetalhados != null) tsLogsDetalhados.IsOn = prof.VerboseLogs;

                SaveProfiles();
            }
        }

        private void txtNomePerfil_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                btnSalvarPerfil_Click(sender, e);
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

            string? oldName = null;
            if (!_isCreatingNewClient)
            {
                oldName = _editingClientOriginalName;
                if (string.IsNullOrEmpty(oldName) && cbPerfis.SelectedItem != null)
                {
                    string selected = cbPerfis.SelectedItem.ToString()!;
                    if (profiles.ContainsKey(selected))
                    {
                        oldName = selected;
                    }
                }
            }

            bool isRenaming = !string.IsNullOrEmpty(oldName) && !oldName.Equals(name, StringComparison.OrdinalIgnoreCase);

            if (isRenaming)
            {
                if (profiles.ContainsKey(name))
                {
                    MessageBox.Show($"Já existe um cliente cadastrado com o nome \"{name}\".", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (profiles.TryGetValue(oldName!, out var oldProfile))
                {
                    profiles.Remove(oldName!);
                }

                foreach (var al in aliases)
                {
                    if (al.client.Equals(oldName, StringComparison.OrdinalIgnoreCase))
                    {
                        al.client = name;
                    }
                }

                if (_appSettings.DefaultClient == oldName) _appSettings.DefaultClient = name;
                if (_appSettings.LastClient == oldName) _appSettings.LastClient = name;
                for (int i = 0; i < _appSettings.FavoriteClientNames.Count; i++)
                {
                    if (_appSettings.FavoriteClientNames[i] == oldName) _appSettings.FavoriteClientNames[i] = name;
                }

                _telemetry?.Track("profile_renamed", new Dictionary<string, object>
                {
                    ["old_name"] = oldName!,
                    ["new_name"] = name
                });
            }
            else
            {
                _telemetry?.Track("profile_saved", new Dictionary<string, object>
                {
                    ["name"] = name,
                    ["is_new"] = !profiles.ContainsKey(name)
                });
            }

            var profile = new ProfileSettings
            {
                Name = name,
                RmVersion = cbVersaoRM.SelectedItem?.ToString() ?? "12.1.2602",
                Alias = (cbAliasDB.SelectedItem != null && !string.IsNullOrWhiteSpace(cbAliasDB.SelectedItem.ToString()))
                    ? cbAliasDB.SelectedItem.ToString()!
                    : (cbBase.SelectedItem is AliasConfig ac ? ac.name : (cbBase.SelectedItem?.ToString() ?? "CorporeRM")),
                AutoLogin = tsAutoLogin.IsOn,
                VerboseLogs = tsVerboseLogs.IsOn,
                ApagarHost = tsApagarHost.IsOn,
                NormalizePath = tsNormalizePath.IsOn,
                EnableProcessIsolation = tsEnableProcessIsolation.IsOn,
                JobServer3Camadas = tsJobServer3Camadas.IsOn,
                EnableCompression = tsEnableCompression.IsOn
            };

            profiles[name] = profile;
            _appSettings.LastClient = name;
            SaveAppSettings();
            SaveProfiles(oldName: isRenaming ? oldName : null, newName: isRenaming ? name : null);
            SaveAliases();

            _isCreatingNewClient = false;
            _editingClientOriginalName = name;

            UpdateProfilesUI(name);
            LoadProfileToUI(profile);
            UpdateFilteredAliasesList();

            AddLog("info", isRenaming ? $"Cliente \"{oldName}\" renomeado para \"{name}\" com sucesso." : $"Cliente \"{name}\" salvo com sucesso.");
            txtNomePerfil.Focus();
        }

        private void btnNovoPerfil_Click(object sender, RoutedEventArgs e)
        {
            _isCreatingNewClient = true;
            _editingClientOriginalName = null;
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
                tsVerboseLogs.IsOn = true;
                tsApagarHost.IsOn = false;
                tsNormalizePath.IsOn = false;
                tsEnableProcessIsolation.IsOn = false;
                tsJobServer3Camadas.IsOn = false;
                tsEnableCompression.IsOn = false;

                tsLimparBrokers.IsOn = false;
                tsLogsDetalhados.IsOn = true;

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
            
            MessageBoxResult result;
            if (Environment.GetEnvironmentVariable("RMCORE_HEADLESS") == "1")
            {
                result = MessageBoxResult.Yes;
            }
            else
            {
                result = MessageBox.Show($"Deseja realmente excluir o cliente \"{selectedName}\"?", "Confirmar Exclusão", MessageBoxButton.YesNo, MessageBoxImage.Question);
            }
            if (result == MessageBoxResult.No) return;

            profiles.Remove(selectedName);
            SaveProfiles();

            // Clean up in-memory aliases so SaveAliases doesn't recreate this client
            var toRemove = aliases.Where(a => a.client.Equals(selectedName, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var al in toRemove)
            {
                aliases.Remove(al);
            }
            SaveAliases();

            if (_appSettings.DefaultClient == selectedName) _appSettings.DefaultClient = string.Empty;
            if (_appSettings.LastClient == selectedName) _appSettings.LastClient = string.Empty;
            _appSettings.FavoriteClientNames.Remove(selectedName);
            SaveAppSettings();

            _editingClientOriginalName = null;

            AddLog("info", $"Cliente \"{selectedName}\" removido.");

            UpdateProfilesUI();
            UpdateFilteredAliasesList();
            
            if (cbPerfis.SelectedItem != null)
            {
                string newSelected = cbPerfis.SelectedItem?.ToString() ?? string.Empty;
                if (profiles.TryGetValue(newSelected, out var profile))
                {
                    LoadProfileToUI(profile);
                }
            }
            else if (profiles.Count > 0)
            {
                LoadProfileToUI(profiles.Values.First());
            }
            else
            {
                btnNovoPerfil_Click(sender, e);
            }
            
            AddLog("info", $"Cliente \"{selectedName}\" excluído com sucesso.");
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
            bool isVerbose = (tsLogsDetalhados?.IsOn == true) || (tsVerboseLogs?.IsOn == true);
            if (!isVerbose && type != "error" && type != "warn" && type != "stderr")
                return;

            void Append()
            {
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
            }

            if (Dispatcher.CheckAccess())
            {
                Append();
            }
            else
            {
                Dispatcher.BeginInvoke(new Action(Append));
            }

            // Persist to file — silently ignore any I/O errors
            try
            {
                string logDir  = GetAppDataDir();
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
            try
            {
                string logPath = Path.Combine(GetAppDataDir(), "logs.txt");
                if (File.Exists(logPath)) File.WriteAllText(logPath, string.Empty);
            }
            catch { }
            AddLog("info", "Logs limpos na interface e no disco.");
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
            _telemetry?.Track("feature_used", new Dictionary<string, object> { ["feature"] = "iniciar_completo" });
            await IniciarRMPlusHostAsync();
        }

        public async Task IniciarRMPlusHostAsync()
        {
            if (_isOperationRunning) return;

            var activeAlias = GetActiveAlias();
            if (activeAlias == null)
            {
                AddLog("error", "Nenhuma Base/Alias ativa selecionada. Selecione uma base antes de iniciar.");
                MessageBox.Show("Nenhuma Base/Alias selecionada. Por favor, selecione uma base na tela inicial antes de iniciar o ambiente.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string binDir = GetBinDirectory();
            if (string.IsNullOrEmpty(binDir) || !Directory.Exists(binDir))
            {
                AddLog("error", "Pasta de instalação do RM (BIN) não configurada ou não encontrada.");
                MessageBox.Show("A pasta de instalação do RM não foi encontrada. Acesse a aba Sobre e reconfigure o diretório da instalação.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SetLoadingState(true);
            try
            {
                AddLog("info", "Iniciando RM + Host Principal...");

                StartHostPrincipal();
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
            _telemetry?.Track("feature_used", new Dictionary<string, object> { ["feature"] = "derrubar_tudo" });
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

        private void btnReconfigurar_Click(object sender, RoutedEventArgs e)
        {
            _telemetry?.Track("feature_used", new Dictionary<string, object> { ["feature"] = "wizard_reconfigurar" });

            RunFirstRunWizard(fromButton: true);
        }

        private void btnReverPrivacidade_Click(object sender, RoutedEventArgs e)
        {
            _telemetry?.Track("feature_used", new Dictionary<string, object> { ["feature"] = "rever_privacidade" });

            var wiz = new WizardWindow { Owner = this };
            wiz.ShowDialog();
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
            string baseSelecionada = (cbBase.SelectedItem is AliasConfig ac) ? ac.name : (cbBase.SelectedItem?.ToString() ?? string.Empty);
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
            var alias = filteredAliases.FirstOrDefault(a => a.name.Equals(baseSelecionada, StringComparison.OrdinalIgnoreCase));
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
            _telemetry?.Track("feature_used", new Dictionary<string, object> { ["feature"] = "iis_config" });
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

                string? ssmsPath = null;
                if (!string.IsNullOrWhiteSpace(_appSettings.SsmsPath) && File.Exists(_appSettings.SsmsPath))
                {
                    ssmsPath = _appSettings.SsmsPath;
                }
                else
                {
                    string[] possiblePaths = {
                        @"C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Ssms.exe",
                        @"C:\Program Files\Microsoft SQL Server Management Studio 20\Common7\IDE\Ssms.exe",
                        @"C:\Program Files (x86)\Microsoft SQL Server Management Studio 20\Common7\IDE\Ssms.exe",
                        @"C:\Program Files\Microsoft SQL Server Management Studio 19\Common7\IDE\Ssms.exe",
                        @"C:\Program Files (x86)\Microsoft SQL Server Management Studio 19\Common7\IDE\Ssms.exe",
                        @"C:\Program Files\Microsoft SQL Server Management Studio 18\Common7\IDE\Ssms.exe",
                        @"C:\Program Files (x86)\Microsoft SQL Server Management Studio 18\Common7\IDE\Ssms.exe",
                        @"C:\Program Files\Microsoft SQL Server Management Studio 17\Common7\IDE\Ssms.exe",
                        @"C:\Program Files (x86)\Microsoft SQL Server Management Studio 17\Common7\IDE\Ssms.exe",
                    };
                    foreach (var p in possiblePaths)
                    {
                        if (File.Exists(p)) { ssmsPath = p; break; }
                    }
                }

                if (ssmsPath == null)
                {
                    MessageBox.Show("SQL Server Management Studio não encontrado.\nVerifique se está instalado ou reconfigure o caminho executando o wizard (aba Sobre).", "SSMS não encontrado", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string ssmsServer = alias.server;
                string host = ssmsServer;
                string instance = string.Empty;
                int port = 0;

                int portSeparatorIndex = ssmsServer.IndexOfAny(new[] { ',', ':' });
                if (portSeparatorIndex >= 0)
                {
                    string portPart = ssmsServer.Substring(portSeparatorIndex + 1).Trim();
                    string digits = new string(portPart.TakeWhile(char.IsDigit).ToArray());
                    int.TryParse(digits, out port);
                }

                int instanceSeparatorIndex = ssmsServer.IndexOf('\\');
                if (instanceSeparatorIndex >= 0)
                {
                    string instancePart = ssmsServer.Substring(instanceSeparatorIndex + 1).Trim();
                    int extraSep = instancePart.IndexOfAny(new[] { ',', ':' });
                    instance = extraSep >= 0 ? instancePart.Substring(0, extraSep).Trim() : instancePart;
                }
                else if (portSeparatorIndex >= 0)
                {
                    string portPart = ssmsServer.Substring(portSeparatorIndex + 1).Trim();
                    int backslashIndex = portPart.IndexOf('\\');
                    if (backslashIndex >= 0)
                    {
                        instance = portPart.Substring(backslashIndex + 1).Trim();
                    }
                }

                int firstSeparator = ssmsServer.IndexOfAny(new[] { '\\', ',', ':', '/' });
                if (firstSeparator >= 0)
                {
                    host = ssmsServer.Substring(0, firstSeparator).Trim();
                }

                ssmsServer = host;
                if (!string.IsNullOrEmpty(instance))
                {
                    ssmsServer += "\\" + instance;
                }
                if (port > 0)
                {
                    ssmsServer += "," + port;
                }

                string args = $"-S \"{ssmsServer}\" -N Optional";
                if (!string.IsNullOrWhiteSpace(alias.Base))
                    args += $" -d \"{alias.Base}\"";
                if (!string.IsNullOrWhiteSpace(alias.dbUser))
                    args += $" -U \"{alias.dbUser}\"";

                // Copia a senha para a área de transferência (SSMS não aceita -P)
                if (!string.IsNullOrWhiteSpace(alias.dbPass))
                {
                    Clipboard.SetText(alias.dbPass);
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = ssmsPath,
                    Arguments = args,
                    UseShellExecute = true
                });

                string senhaMsg = !string.IsNullOrWhiteSpace(alias.dbPass) 
                    ? "\n\n🔑 Senha copiada para a área de transferência!" +
                      "\n\nComo a Microsoft bloqueou o envio de senhas de forma automática, " +
                      "você precisará colar a senha (Ctrl+V) toda vez que abrir o SSMS por aqui."
                    : "";
                MessageBox.Show($"SSMS aberto para {ssmsServer}.{senhaMsg}", "SSMS", MessageBoxButton.OK, MessageBoxImage.Information);
                AddLog("info", $"SSMS aberto: servidor {ssmsServer}, base {alias.Base}, user {alias.dbUser}");
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
                    if ((subDir.Attributes & FileAttributes.ReparsePoint) != 0)
                        continue;

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
            // 1) Versão selecionada na aba Clientes (Prioridade máxima)
            string selectedVersion = cbVersaoRM?.SelectedItem?.ToString() ?? string.Empty;
            if (!string.IsNullOrEmpty(selectedVersion))
            {
                string legacyPath = $@"C:\RM\Legado\{selectedVersion}\Bin";
                if (Directory.Exists(legacyPath)) return legacyPath;
            }

            // 2) Caminho salvo pelo wizard na 1ª execução (Fallback)
            if (!string.IsNullOrEmpty(_appSettings.RmInstallPath) && Directory.Exists(_appSettings.RmInstallPath))
            {
                return _appSettings.RmInstallPath;
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
            if (cbBase.SelectedItem is AliasConfig selectedAlias)
            {
                return selectedAlias;
            }

            string activeClient = cbClienteAtivo.SelectedItem?.ToString() ?? cbPerfis.SelectedItem?.ToString() ?? string.Empty;
            
            if (cbBase.SelectedItem != null)
            {
                string selectedName = (cbBase.SelectedItem is AliasConfig ac) ? ac.name : (cbBase.SelectedItem?.ToString() ?? string.Empty);
                var found = aliases.FirstOrDefault(a => a.name.Equals(selectedName, StringComparison.OrdinalIgnoreCase) && (string.IsNullOrEmpty(activeClient) || a.client.Equals(activeClient, StringComparison.OrdinalIgnoreCase)));
                if (found != null) return found;
            }

            if (!string.IsNullOrEmpty(activeClient) && profiles.TryGetValue(activeClient, out var prof) && !string.IsNullOrEmpty(prof.Alias))
            {
                var found = aliases.FirstOrDefault(a => a.name.Equals(prof.Alias, StringComparison.OrdinalIgnoreCase) && a.client.Equals(activeClient, StringComparison.OrdinalIgnoreCase));
                if (found != null) return found;
            }

            if (!string.IsNullOrEmpty(activeClient))
            {
                var found = aliases.FirstOrDefault(a => a.client.Equals(activeClient, StringComparison.OrdinalIgnoreCase));
                if (found != null) return found;
            }

            return aliases.FirstOrDefault()!;
        }

        private static string XmlEscape(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return System.Security.SecurityElement.Escape(value);
        }

        private void CreateAliasDat(string binDir, AliasConfig alias)
        {
            try
            {
                bool isSql = alias.dbType.Equals("sql", StringComparison.OrdinalIgnoreCase);
                string dbType = isSql ? "SqlServer" : "Oracle";
                string dbProvider = isSql ? "SqlClient" : "OracleClient";
                string dbNameTag = isSql ? $"<DbName>{XmlEscape(alias.Base)}</DbName>" : "<DbName/>";

                // Read new toggle values (must be accessed on the UI thread — already on dispatcher here)
                string normalizePath        = tsNormalizePath.IsOn.ToString().ToLower();
                string enableProcIsolation  = tsEnableProcessIsolation.IsOn.ToString().ToLower();
                string jobServer3Camadas    = tsJobServer3Camadas.IsOn.ToString().ToLower();
                string enableCompression    = tsEnableCompression.IsOn.ToString().ToLower();

                string cleanServer = alias.server;
                if (isSql)
                {
                    string host = cleanServer;
                    string instance = string.Empty;
                    int port = 0;

                    int portSeparatorIndex = cleanServer.IndexOfAny(new[] { ',', ':' });
                    if (portSeparatorIndex >= 0)
                    {
                        string portPart = cleanServer.Substring(portSeparatorIndex + 1).Trim();
                        string digits = new string(portPart.TakeWhile(char.IsDigit).ToArray());
                        int.TryParse(digits, out port);
                    }

                    int instanceSeparatorIndex = cleanServer.IndexOf('\\');
                    if (instanceSeparatorIndex >= 0)
                    {
                        string instancePart = cleanServer.Substring(instanceSeparatorIndex + 1).Trim();
                        int extraSep = instancePart.IndexOfAny(new[] { ',', ':' });
                        instance = extraSep >= 0 ? instancePart.Substring(0, extraSep).Trim() : instancePart;
                    }
                    else if (portSeparatorIndex >= 0)
                    {
                        string portPart = cleanServer.Substring(portSeparatorIndex + 1).Trim();
                        int backslashIndex = portPart.IndexOf('\\');
                        if (backslashIndex >= 0)
                        {
                            instance = portPart.Substring(backslashIndex + 1).Trim();
                        }
                    }

                    int firstSeparator = cleanServer.IndexOfAny(new[] { '\\', ',', ':', '/' });
                    if (firstSeparator >= 0)
                    {
                        host = cleanServer.Substring(0, firstSeparator).Trim();
                    }

                    cleanServer = host;
                    if (!string.IsNullOrEmpty(instance))
                    {
                        cleanServer += "\\" + instance;
                    }
                    if (port > 0)
                    {
                        cleanServer += "," + port;
                    }
                }

                string xml = $@"<?xml version=""1.0"" standalone=""yes""?>
<RMSAliasData xmlns=""http://tempuri.org/RMSAliasData.xsd"">
  <DbConfig>
    <Alias>CorporeRM</Alias>
    <DbType>{dbType}</DbType>
    <DbProvider>{dbProvider}</DbProvider>
    <DbServer>{XmlEscape(cleanServer)}</DbServer>
    {dbNameTag}
    <UserName>{XmlEscape(alias.dbUser)}</UserName>
    <Password>{XmlEscape(alias.dbPass)}</Password>
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
    <IsolateProcess>{enableProcIsolation}</IsolateProcess>
    <JobServer3Camadas>{jobServer3Camadas}</JobServer3Camadas>
    <DefaultDB>CorporeRM</DefaultDB>
    <EnableCompression>{enableCompression}</EnableCompression>
    <ConnectionStringExtraParams>Encrypt=False;TrustServerCertificate=True</ConnectionStringExtraParams>
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

        private void CleanBrokerCustomIfNeeded(string binDir)
        {
            try
            {
                string activeClientName = cbClienteAtivo.SelectedItem?.ToString() ?? cbPerfis.SelectedItem?.ToString() ?? string.Empty;
                if (!string.IsNullOrEmpty(activeClientName) &&
                    profiles.TryGetValue(activeClientName, out var activeProfile) &&
                    activeProfile.ApagarHost)
                {
                    if (string.IsNullOrEmpty(binDir)) binDir = GetBinDirectory();
                    if (!string.IsNullOrEmpty(binDir))
                    {
                        string brokerCustomPath = Path.Combine(binDir, "_BrokerCustom.dat");
                        if (File.Exists(brokerCustomPath))
                        {
                            File.Delete(brokerCustomPath);
                            AddLog("info", "[Deletar Broker Custom] _BrokerCustom.dat excluído antes da inicialização do Host.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog("error", $"[Deletar Broker Custom] Falha ao excluir _BrokerCustom.dat: {ex.Message}");
            }
        }

        private void StartHostPrincipal()
        {
            PrepareAlias();

            string binDir = GetBinDirectory();
            CleanBrokerCustomIfNeeded(binDir);

            string path = Path.Combine(binDir, "RM.Host.exe");
            if (!File.Exists(path))
                path = Path.Combine(binDir, "RM.Host.ServiceManager.exe");

            StartHostProcess(path, "Host Principal");
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
            CleanBrokerCustomIfNeeded(binDir);

            string path = Path.Combine(binDir, "RM.Host1.exe"); 
            if (!File.Exists(path))
                path = Path.Combine(binDir, "RM.Host.exe");

            StartHostProcess(path, "Host 2");
        }

        private void StopHost2()
        {
            KillProcessByName("RM.Host1");
            KillProcessByName("RM.Host");
        }

        private void StartHostProcess(string path, string displayName)
        {
            if (!File.Exists(path))
            {
                AddLog("error", $"{displayName} não encontrado em: {path}");
                MessageBox.Show($"{displayName} não encontrado em: {path}", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                string binDir = Path.GetDirectoryName(path)!;

                var psi = new ProcessStartInfo
                {
                    FileName = path,
                    WorkingDirectory = binDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = false
                };

                var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

                proc.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        AddLog("stdout", $"[{displayName}] {e.Data}");
                    }
                };

                proc.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        AddLog("stderr", $"[{displayName}] {e.Data}");
                    }
                };

                proc.Exited += (s, e) =>
                {
                    AddLog("info", $"{displayName} finalizado.");
                    Dispatcher.BeginInvoke(new Action(() => AtualizarStatusServicos()));
                };

                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                AddLog("info", $"{displayName} iniciado com captura de stream em tempo real (PID: {proc.Id}).");
            }
            catch (Exception ex)
            {
                AddLog("error", $"Erro ao iniciar {displayName} com captura: {ex.Message}. Tentando modo direto...");
                StartProcess(path, displayName);
            }
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

                        string args = $"alias=\"CorporeRM\" user=\"{user}\" password=\"{pass}\"";

                        Process.Start(new ProcessStartInfo
                        {
                            FileName = path,
                            Arguments = args,
                            UseShellExecute = true,
                            WorkingDirectory = binDir
                        });
                        AddLog("info", $"RM.exe iniciado com AutoLogin na base \"{activeAlias.name}\" (Base: {activeAlias.Base}, Usuário: {user}).");
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
                string[] baseDirs = {
                    AppDomain.CurrentDomain.BaseDirectory,
                    Environment.CurrentDirectory,
                    Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "."
                };

                foreach (var baseDir in baseDirs)
                {
                    if (string.IsNullOrEmpty(baseDir)) continue;

                    string local = Path.Combine(baseDir, "RM.HostCheck.exe");
                    if (File.Exists(local)) return local;

                    string inTools = Path.Combine(baseDir, "tools", "RM.HostCheck.exe");
                    if (File.Exists(inTools)) return inTools;

                    var dir = new DirectoryInfo(baseDir);
                    while (dir != null)
                    {
                        string p1 = Path.Combine(dir.FullName, "RM.HostCheck.exe");
                        if (File.Exists(p1)) return p1;

                        string p2 = Path.Combine(dir.FullName, "tools", "RM.HostCheck.exe");
                        if (File.Exists(p2)) return p2;

                        dir = dir.Parent;
                    }
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

                var psi = new ProcessStartInfo
                {
                    FileName = hostCheckExe,
                    Arguments = $"\"{binDir}\" {hcPort} {timeoutMs}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    AddLog("error", "Falha ao iniciar RM.HostCheck.exe.");
                    return false;
                }

                var stderrTask = proc.StandardError.ReadToEndAsync();
                var stdoutTask = proc.StandardOutput.ReadToEndAsync();
                await proc.WaitForExitAsync();

                string stderr = await stderrTask;
                string stdout = await stdoutTask;

                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    AddLog("warn", $"[HostCheck] {stderr.Trim()}");
                }
                if (!string.IsNullOrWhiteSpace(stdout))
                {
                    AddLog("info", $"[HostCheck] {stdout.Trim()}");
                }

                AddLog("info", proc.ExitCode switch
                {
                    0 => "Ícone verde confirmado (Host autenticado com sucesso).",
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
            KillProcessByName("RM.Host1");
            KillProcessByName("RM.Host.Service");
            KillProcessByName("RM.ProcessPool.Process");
            KillProcessByName("RM.Host.JobServer");
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

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                _telemetry?.Track("app_close", new Dictionary<string, object>
                {
                    ["session_sec"] = (int)(DateTime.UtcNow - _sessionStart).TotalSeconds
                });
                _telemetry?.FlushSync();
                _telemetry?.Dispose();
            }
            catch { }

            try
            {
                _trayService?.Dispose();
            }
            catch { }

            base.OnClosed(e);

            if (IsExiting)
            {
                try { Application.Current?.Shutdown(); } catch { }
                try { Process.GetCurrentProcess().Kill(); } catch { Environment.Exit(0); }
            }
        }

        // ---------------------------------------------------------------
        // Window position / size persistence
        // ---------------------------------------------------------------

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            RestoreWindowSettings();
            ApplyStartWithWindowsSetting(_appSettings.StartWithWindows);

            if (Environment.GetEnvironmentVariable("RMCORE_HEADLESS") == "1")
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = -32000;
                Top = -32000;
                ShowInTaskbar = false;
            }

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
            _appSettings.SsmsPath             = wiz.SsmsPath;
            _appSettings.PrivacyAccepted      = wiz.PrivacyAccepted;

            if (wiz.PrivacyAccepted)
            {
                _telemetry?.Track("privacy_accepted", new Dictionary<string, object>
                {
                    ["country"] = System.Globalization.CultureInfo.CurrentUICulture.Name
                });
            }

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
                if (File.Exists(_appSettingsPath))
                {
                    var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_appSettingsPath));
                    if (loaded != null) _appSettings = loaded;
                }
            }
            catch { /* keep defaults */ }

            if (string.IsNullOrEmpty(_appSettings.InstallId))
            {
                _appSettings.InstallId = Guid.NewGuid().ToString("N");
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_appSettingsPath)!);
                    File.WriteAllText(_appSettingsPath, JsonSerializer.Serialize(_appSettings));
                }
                catch { /* non-critical */ }
            }

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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SaveAppSettings falhou: {ex.Message}");
            }
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
            if (btnImportarOutrosAppsCliente != null) btnImportarOutrosAppsCliente.IsEnabled = !loading;
            if (btnImportarOutrosAppsHeader != null) btnImportarOutrosAppsHeader.IsEnabled = !loading;
            if (btnImportarOutrosApps != null) btnImportarOutrosApps.IsEnabled = !loading;
            
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

                string? tabName = rb == rbTabInicio ? "home"
                                : rb == rbTabPerfil ? "clientes"
                                : rb == rbTabLogs   ? "logs"
                                : rb == rbTabSobre  ? "sobre"
                                : null;
                if (tabName != null) _telemetry?.Track("tab_opened", new Dictionary<string, object> { ["tab"] = tabName });
            }
        }

        private void btnGerenciarAliases_Click(object sender, RoutedEventArgs e)
        {
            cbClienteAssociado.Items.Clear();
            foreach (var key in profiles.Keys)
            {
                cbClienteAssociado.Items.Add(key);
            }
            string active = cbPerfis.SelectedItem?.ToString() ?? cbClienteAtivo.SelectedItem?.ToString() ?? string.Empty;
            if (!string.IsNullOrEmpty(active) && cbClienteAssociado.Items.Contains(active))
            {
                cbClienteAssociado.SelectedItem = active;
            }
            txtSearchBase.Text = string.Empty;
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
                _appSettings.BaseTagColors[selectedAlias.id] = tag;
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

            bool isNowDefault = _appSettings.DefaultClient != profile.Name;
            _appSettings.DefaultClient = isNowDefault ? profile.Name : string.Empty;

            foreach (var p in profiles.Values)
            {
                p.IsFavorite = (!string.IsNullOrEmpty(_appSettings.DefaultClient) && p.Name.Equals(_appSettings.DefaultClient, StringComparison.OrdinalIgnoreCase));
            }

            UpdateFavoritoIcon(isNowDefault);
            SaveAppSettings();
            AddLog("info", isNowDefault ? $"Cliente \"{profile.Name}\" definido como padrão." : $"Cliente \"{profile.Name}\" removido do padrão.");
        }

        private void btnToggleBaseFavorito_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AliasConfig alias)
            {
                bool isNowDefault = _appSettings.DefaultBaseId != alias.id;
                _appSettings.DefaultBaseId = isNowDefault ? alias.id : string.Empty;

                foreach (var a in aliases)
                {
                    a.IsFavorite = (!string.IsNullOrEmpty(_appSettings.DefaultBaseId) && a.id == _appSettings.DefaultBaseId);
                }

                SaveAppSettings();
                lstBases.Items.Refresh();
                RefreshCbBaseColors();

                AddLog("info", isNowDefault ? $"Base \"{alias.name}\" definida como padrão." : $"Base \"{alias.name}\" removida do padrão.");
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
            string baseIdToLoad = !string.IsNullOrEmpty(_appSettings.DefaultBaseId) && aliases.Any(a => a.id == _appSettings.DefaultBaseId && a.client.Equals(clientToLoad, StringComparison.OrdinalIgnoreCase))
                ? _appSettings.DefaultBaseId
                : (!string.IsNullOrEmpty(_appSettings.LastBaseId) && aliases.Any(a => a.id == _appSettings.LastBaseId && a.client.Equals(clientToLoad, StringComparison.OrdinalIgnoreCase))
                    ? _appSettings.LastBaseId
                    : string.Empty);

            if (!string.IsNullOrEmpty(baseIdToLoad))
            {
                var alias = aliases.FirstOrDefault(a => a.id == baseIdToLoad && a.client.Equals(clientToLoad, StringComparison.OrdinalIgnoreCase));
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
            string selectedName = txtNomePerfil.Text.Trim();
            if (string.IsNullOrEmpty(selectedName))
            {
                selectedName = cbPerfis.SelectedItem?.ToString() ?? cbClienteAtivo.SelectedItem?.ToString() ?? string.Empty;
            }

            if (string.IsNullOrEmpty(selectedName) || !profiles.TryGetValue(selectedName, out var profile))
            {
                MessageBox.Show("Selecione um cliente para exportar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                string safeName = string.Join("_", selectedName.Split(Path.GetInvalidFileNameChars()));
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = $"Exportar Cliente - {selectedName}",
                    Filter = "JSON|*.json",
                    FileName = $"rmcore_cliente_{safeName}.json"
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
                AddLog("info", $"Cliente \"{selectedName}\" exportado para: {dlg.FileName} ({clientAliases.Count} base(s)).");
                MessageBox.Show($"Cliente \"{selectedName}\" exportado com sucesso!\n\n• Bases exportadas: {clientAliases.Count}\n• Arquivo: {dlg.FileName}", "Exportar Cliente", MessageBoxButton.OK, MessageBoxImage.Information);
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
                txtDbPassVisible.Text = selectedAlias.dbPass;
                txtRmUser.Text = selectedAlias.rmUser;
                pbRmPass.Password = selectedAlias.rmPass;
                txtRmPassVisible.Text = selectedAlias.rmPass;

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
            _telemetry?.Track("feature_used", new Dictionary<string, object> { ["feature"] = "nova_base" });
            string activeClient = cbPerfis.SelectedItem?.ToString()
                                 ?? cbClienteAtivo.SelectedItem?.ToString()
                                 ?? txtNomePerfil.Text.Trim();

            if (string.IsNullOrEmpty(activeClient))
                activeClient = "Cliente Padrão";

            string defaultVersion = string.Empty;
            if (!string.IsNullOrEmpty(activeClient) && profiles.TryGetValue(activeClient, out var p))
                defaultVersion = p.RmVersion;

            string baseCandidate = "Nova Conexão";
            int baseNum = 2;
            while (aliases.Any(a => a.client.Equals(activeClient, StringComparison.OrdinalIgnoreCase) && a.name.Equals(baseCandidate, StringComparison.OrdinalIgnoreCase)))
            {
                baseCandidate = $"Nova Conexão {baseNum++}";
            }

            var newAlias = new AliasConfig
            {
                id = DateTime.Now.Ticks.ToString(),
                name = baseCandidate,
                Base = "CorporeRM",
                client = activeClient,
                dbVersion = defaultVersion,
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

            string clientForUI = cbPerfis.SelectedItem?.ToString() ?? cbClienteAtivo.SelectedItem?.ToString() ?? string.Empty;
            if (!string.IsNullOrEmpty(clientForUI))
            {
                UpdateAliasesUI(clientForUI, preselectedBaseName: newAlias.name);
            }
            else
            {
                UpdateFilteredAliasesList();
            }
            AddLog("info", $"Nova base adicionada para o cliente \"{activeClient}\".");
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

                string dbPassValue = (pbDbPass.Visibility == Visibility.Visible) ? pbDbPass.Password.Trim() : txtDbPassVisible.Text.Trim();
                string rmPassValue = (pbRmPass.Visibility == Visibility.Visible) ? pbRmPass.Password.Trim() : txtRmPassVisible.Text.Trim();

                string oldName = selectedAlias.name;
                string oldClient = selectedAlias.client;
                string newClient = cbClienteAssociado.SelectedItem?.ToString() ?? selectedAlias.client;

                selectedAlias.name = name;
                selectedAlias.dbType = rbOracle.IsChecked == true ? "oracle" : "sql";
                selectedAlias.Base = txtDbBaseName.Text.Trim();
                selectedAlias.server = txtDbServer.Text.Trim();
                selectedAlias.dbUser = txtDbUser.Text.Trim();
                selectedAlias.dbPass = dbPassValue;
                selectedAlias.rmUser = txtRmUser.Text.Trim();
                selectedAlias.rmPass = rmPassValue;
                
                pbDbPass.Password = dbPassValue;
                txtDbPassVisible.Text = dbPassValue;
                pbRmPass.Password = rmPassValue;
                txtRmPassVisible.Text = rmPassValue;

                selectedAlias.client = newClient;
                selectedAlias.dbVersion = cbDbVersion.SelectedItem?.ToString() ?? selectedAlias.dbVersion;

                selectedAlias.runService = chkRunService.IsChecked == true;
                selectedAlias.jobProcessing = chkJobProcessing.IsChecked == true;
                // Save TagColor from color selector
                selectedAlias.localOnly = chkLocalOnly.IsChecked == true;
                selectedAlias.processPool = chkProcessPool.IsChecked == true;

                int maxThreads = 0;
                if (int.TryParse(txtMaxThreads.Text.Trim(), out int parsedThreads))
                {
                    maxThreads = Math.Max(0, parsedThreads);
                }
                selectedAlias.maxThreads = maxThreads;

                SaveAliases();

                // If renamed, update active profile's alias pointer
                if (!oldName.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    if (profiles.TryGetValue(oldClient, out var clientProf) && clientProf.Alias == oldName)
                    {
                        clientProf.Alias = name;
                        SaveProfiles();
                    }
                }

                // If moved to a different client, migrate profile connections
                if (!oldClient.Equals(newClient, StringComparison.OrdinalIgnoreCase))
                {
                    if (profiles.TryGetValue(oldClient, out var oldProf) && oldProf.Alias == oldName)
                    {
                        var nextBase = aliases.FirstOrDefault(a => a.client.Equals(oldClient, StringComparison.OrdinalIgnoreCase) && a.id != selectedAlias.id);
                        oldProf.Alias = nextBase?.name ?? "CorporeRM";
                        SaveProfiles();
                    }
                    if (profiles.TryGetValue(newClient, out var newProf) && (string.IsNullOrEmpty(newProf.Alias) || newProf.Alias == "CorporeRM"))
                    {
                        newProf.Alias = name;
                        SaveProfiles();
                    }
                    UpdateAliasesUI(oldClient);
                    UpdateAliasesUI(newClient);
                }
                
                // Refresh list display
                lstBases.Items.Refresh();

                string clientToUpdate = cbClienteAtivo.SelectedItem?.ToString() ?? cbPerfis.SelectedItem?.ToString() ?? newClient;
                if (!string.IsNullOrEmpty(clientToUpdate))
                {
                    UpdateAliasesUI(clientToUpdate, preselectedBaseName: name);
                }

                UpdateFilteredAliasesList();

                AddLog("info", $"Alias \"{name}\" atualizado com sucesso.");
            }
        }

        private void txtDbAliasName_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                btnSalvarBase_Click(sender, e);
            }
        }

        private void btnDuplicarBase_Click(object sender, RoutedEventArgs e)
        {
            if (lstBases.SelectedItem is AliasConfig selectedAlias)
            {
                string copyCandidate = $"{selectedAlias.name} (cópia)";
                int copyNum = 2;
                while (aliases.Any(a => a.client.Equals(selectedAlias.client, StringComparison.OrdinalIgnoreCase) && a.name.Equals(copyCandidate, StringComparison.OrdinalIgnoreCase)))
                {
                    copyCandidate = $"{selectedAlias.name} (cópia {copyNum++})";
                }

                var newAlias = new AliasConfig
                {
                    id = DateTime.Now.Ticks.ToString(),
                    name = copyCandidate,
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
                    UpdateAliasesUI(cbClienteAtivo.SelectedItem.ToString()!, preselectedBaseName: newAlias.name);

                AddLog("info", $"Alias duplicado: \"{newAlias.name}\".");
            }
        }

        private async void btnTestarConexao_Click(object sender, RoutedEventArgs e)
        {
            string server = txtDbServer.Text.Trim();
            string type = rbOracle.IsChecked == true ? "oracle" : "sql";
            if (string.IsNullOrEmpty(server))
            {
                MessageBox.Show("Por favor, digite o servidor de banco.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            await TestDbConnectionAsync(server, type);
        }

        private async Task TestDbConnectionAsync(string serverAddress, string dbType)
        {
            try
            {
                string host = serverAddress;
                int defaultPort = dbType.Equals("oracle", StringComparison.OrdinalIgnoreCase) ? 1521 : 1433;
                int port = 0;

                int portSeparatorIndex = serverAddress.IndexOfAny(new[] { ',', ':' });
                if (portSeparatorIndex >= 0)
                {
                    string portPart = serverAddress.Substring(portSeparatorIndex + 1).Trim();
                    string digits = new string(portPart.TakeWhile(char.IsDigit).ToArray());
                    int.TryParse(digits, out port);
                }

                int firstSeparator = serverAddress.IndexOfAny(new[] { '\\', ',', ':', '/' });
                if (firstSeparator >= 0)
                {
                    host = serverAddress.Substring(0, firstSeparator).Trim();
                }

                if (host == "." || host == "(local)" || host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                {
                    host = "127.0.0.1";
                }

                if (port == 0)
                {
                    port = defaultPort;
                }

                AddLog("info", $"Testando conexão de rede com {host}:{port}...");
                
                using (var tcpClient = new System.Net.Sockets.TcpClient())
                {
                    var connectTask = tcpClient.ConnectAsync(host, port);
                    var timeoutTask = Task.Delay(3000);
                    var completedTask = await Task.WhenAny(connectTask, timeoutTask);

                    if (completedTask == connectTask && tcpClient.Connected)
                    {
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

        private void UpdateDbBaseNameVisibility()
        {
            if (lblDbBaseName == null || txtDbBaseName == null) return;
            bool isOracle = rbOracle?.IsChecked == true;
            lblDbBaseName.Text = isOracle ? "SERVICE NAME / INSTÂNCIA ORACLE" : "NOME DA BASE DE DADOS";
        }

        private void rbDbProvider_Checked(object sender, RoutedEventArgs e)
        {
            UpdateDbBaseNameVisibility();
        }

        private void btnDeletarBase_Click(object sender, RoutedEventArgs e)
        {
            if (lstBases.SelectedItem is AliasConfig selectedAlias)
            {
                MessageBoxResult confirm;
                if (Environment.GetEnvironmentVariable("RMCORE_HEADLESS") == "1")
                {
                    confirm = MessageBoxResult.Yes;
                }
                else
                {
                    confirm = MessageBox.Show($"Deseja realmente excluir a base \"{selectedAlias.name}\"?", "Confirmar Exclusão", MessageBoxButton.YesNo, MessageBoxImage.Question);
                }
                if (confirm != MessageBoxResult.Yes) return;

                string client = selectedAlias.client;
                string baseId = selectedAlias.id;

                aliases.Remove(selectedAlias);
                SaveAliases();

                if (_appSettings.DefaultBaseId == baseId) _appSettings.DefaultBaseId = string.Empty;
                if (_appSettings.LastBaseId == baseId) _appSettings.LastBaseId = string.Empty;
                _appSettings.BaseFavoriteIds.Remove(baseId);
                _appSettings.BaseTagColors.Remove(baseId);
                SaveAppSettings();

                if (profiles.TryGetValue(client, out var prof) && prof.Alias == selectedAlias.name)
                {
                    var nextBase = aliases.FirstOrDefault(a => a.client.Equals(client, StringComparison.OrdinalIgnoreCase));
                    prof.Alias = nextBase?.name ?? "CorporeRM";
                    SaveProfiles();
                }

                UpdateFilteredAliasesList();
                if (filteredAliases.Count > 0)
                {
                    lstBases.SelectedItem = filteredAliases.First();
                }
                else
                {
                    lstBases.SelectedItem = null;
                }

                if (cbClienteAtivo.SelectedItem != null)
                {
                    UpdateAliasesUI(cbClienteAtivo.SelectedItem.ToString()!);
                }

                AddLog("info", $"Base \"{selectedAlias.name}\" removida com sucesso.");
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

        private void btnImportarOutrosApps_Click(object sender, RoutedEventArgs e)
        {
            if (_isOperationRunning) return;

            var menu = new ContextMenu
            {
                Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1E1E22")),
                BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#33FFFFFF")),
                Foreground = System.Windows.Media.Brushes.White
            };

            var itemToolkit = new MenuItem
            {
                Header = "⚡ TOTVS RM Toolkit (JSON)",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 12,
                Padding = new Thickness(10, 6, 10, 6)
            };
            itemToolkit.Click += (_, _) =>
            {
                SetLoadingState(true);
                try { ImportarDoToolkit(); } finally { SetLoadingState(false); }
            };

            var itemAtalhos = new MenuItem
            {
                Header = "🚀 Atalhos (viniciusfs15/Atalhos - SQLite)",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 12,
                Padding = new Thickness(10, 6, 10, 6)
            };
            itemAtalhos.Click += (_, _) =>
            {
                SetLoadingState(true);
                try { ImportarDoAtalhos(); } finally { SetLoadingState(false); }
            };

            menu.Items.Add(itemToolkit);
            menu.Items.Add(itemAtalhos);

            if (sender is FrameworkElement elem)
            {
                menu.PlacementTarget = elem;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                menu.IsOpen = true;
            }
            else
            {
                menu.IsOpen = true;
            }
        }

        private void btnImportarToolkit_Click(object sender, RoutedEventArgs e)
        {
            btnImportarOutrosApps_Click(sender, e);
        }

        public void ImportarDoToolkit(string? specificPath = null)
        {
            var candidateDirs = new List<string>();

            if (!string.IsNullOrWhiteSpace(specificPath))
            {
                if (Directory.Exists(specificPath))
                    candidateDirs.Add(specificPath);
                else if (File.Exists(specificPath))
                    candidateDirs.Add(Path.GetDirectoryName(specificPath)!);
            }

            string? customToolkitEnv = Environment.GetEnvironmentVariable("TOOLKIT_DATA_DIR");
            if (!string.IsNullOrWhiteSpace(customToolkitEnv) && Directory.Exists(customToolkitEnv))
            {
                candidateDirs.Add(customToolkitEnv);
            }

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            // Prioriza o local oficial do TOTVS RM Toolkit (LocalAppData\com.totvs.rmtoolkit)
            candidateDirs.Add(Path.Combine(localAppData, "com.totvs.rmtoolkit"));
            candidateDirs.Add(Path.Combine(appData, "com.totvs.rmtoolkit"));
            candidateDirs.Add(Path.Combine(localAppData, "rm-toolkit"));
            candidateDirs.Add(Path.Combine(appData, "rm-toolkit"));
            candidateDirs.Add(Path.Combine(appData, "Toolkit"));
            candidateDirs.Add(Path.Combine(localAppData, "Programs", "Toolkit"));
            candidateDirs.Add(Path.Combine(localAppData, "Programs", "Toolkit", "resources"));

            string? foundDir = null;
            string? profilesFile = null;
            string? aliasesFile = null;

            foreach (var dir in candidateDirs)
            {
                if (Directory.Exists(dir))
                {
                    string pFile = Path.Combine(dir, "profiles.json");
                    string aFile = Path.Combine(dir, "aliases.json");
                    if (File.Exists(pFile) || File.Exists(aFile))
                    {
                        foundDir = dir;
                        profilesFile = File.Exists(pFile) ? pFile : null;
                        aliasesFile = File.Exists(aFile) ? aFile : null;
                        break;
                    }
                }
            }

            if (foundDir == null || (profilesFile == null && aliasesFile == null))
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Selecione o arquivo aliases.json ou profiles.json do Toolkit",
                    Filter = "Arquivos JSON (*.json)|*.json|Todos os Arquivos (*.*)|*.*",
                    InitialDirectory = Directory.Exists(Path.Combine(appData, "rm-toolkit")) ? Path.Combine(appData, "rm-toolkit") : localAppData
                };

                if (dlg.ShowDialog() == true)
                {
                    string selectedDir = Path.GetDirectoryName(dlg.FileName)!;
                    string pFile = Path.Combine(selectedDir, "profiles.json");
                    string aFile = Path.Combine(selectedDir, "aliases.json");
                    foundDir = selectedDir;
                    profilesFile = File.Exists(pFile) ? pFile : (dlg.FileName.EndsWith("profiles.json", StringComparison.OrdinalIgnoreCase) ? dlg.FileName : null);
                    aliasesFile = File.Exists(aFile) ? aFile : (dlg.FileName.EndsWith("aliases.json", StringComparison.OrdinalIgnoreCase) ? dlg.FileName : null);
                }
                else
                {
                    return;
                }
            }

            int importedProfilesCount = 0;
            int importedAliasesCount = 0;

            // 1. Import Profiles
            if (profilesFile != null && File.Exists(profilesFile))
            {
                try
                {
                    string pJson = File.ReadAllText(profilesFile);
                    using var doc = JsonDocument.Parse(pJson);
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        string profName = prop.Name;
                        var elem = prop.Value;

                        string rmVer = elem.TryGetProperty("rmVersion", out var v) ? v.GetString() ?? "12.1.2602" : "12.1.2602";
                        string aliasRef = elem.TryGetProperty("alias", out var a) ? a.GetString() ?? "" : "";
                        bool autoLogin = elem.TryGetProperty("autoLogin", out var al) && al.GetBoolean();
                        bool verbose = elem.TryGetProperty("verboseLogs", out var vl) && vl.GetBoolean();
                        bool apagarHost = elem.TryGetProperty("apagarHost", out var ah) && ah.GetBoolean();

                        var profile = new ProfileSettings
                        {
                            Name = profName,
                            RmVersion = rmVer,
                            Alias = aliasRef,
                            AutoLogin = autoLogin,
                            VerboseLogs = verbose,
                            ApagarHost = apagarHost
                        };

                        profiles[profName] = profile;
                        importedProfilesCount++;
                    }
                }
                catch (Exception ex)
                {
                    AddLog("error", $"Erro ao ler profiles do Toolkit: {ex.Message}");
                }
            }

            // 2. Import Aliases
            if (aliasesFile != null && File.Exists(aliasesFile))
            {
                try
                {
                    string aJson = File.ReadAllText(aliasesFile);
                    using var doc = JsonDocument.Parse(aJson);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var elem in doc.RootElement.EnumerateArray())
                        {
                            string id = elem.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? DateTime.Now.Ticks.ToString() : DateTime.Now.Ticks.ToString();
                            string name = elem.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? "Base Importada" : "Base Importada";
                            string baseName = elem.TryGetProperty("base", out var bProp) ? bProp.GetString() ?? "CorporeRM" : (elem.TryGetProperty("Base", out var bProp2) ? bProp2.GetString() ?? "CorporeRM" : "CorporeRM");
                            if (string.IsNullOrWhiteSpace(baseName)) baseName = "CorporeRM";

                            string server = elem.TryGetProperty("server", out var sProp) ? sProp.GetString() ?? "" : "";
                            string dbType = elem.TryGetProperty("dbType", out var dtProp) ? dtProp.GetString() ?? "sql" : "sql";
                            string dbUser = elem.TryGetProperty("dbUser", out var duProp) ? duProp.GetString() ?? "rm" : "rm";
                            string dbPass = elem.TryGetProperty("dbPass", out var dpProp) ? dpProp.GetString() ?? "" : "";
                            string rmUser = elem.TryGetProperty("rmUser", out var ruProp) ? ruProp.GetString() ?? "mestre" : "mestre";
                            string rmPass = elem.TryGetProperty("rmPass", out var rpProp) ? rpProp.GetString() ?? "totvs" : "totvs";
                            bool runService = !elem.TryGetProperty("runService", out var rsProp) || rsProp.GetBoolean();
                            bool jobProc = elem.TryGetProperty("jobProcessing", out var jpProp) && jpProp.GetBoolean();
                            bool localOnly = elem.TryGetProperty("localOnly", out var loProp) && loProp.GetBoolean();
                            bool processPool = elem.TryGetProperty("processPool", out var ppProp) && ppProp.GetBoolean();
                            int maxThreads = elem.TryGetProperty("maxThreads", out var mtProp) ? mtProp.GetInt32() : 0;
                            string dbVersion = elem.TryGetProperty("dbVersion", out var dvProp) ? dvProp.GetString() ?? "12.1.2602" : "12.1.2602";

                            string associatedClient = "Cliente Padrão";
                            if (elem.TryGetProperty("client", out var cProp) && !string.IsNullOrWhiteSpace(cProp.GetString()))
                            {
                                associatedClient = cProp.GetString()!;
                            }
                            else
                            {
                                var matchingProfile = profiles.Values.FirstOrDefault(p => p.Alias.Equals(name, StringComparison.OrdinalIgnoreCase));
                                if (matchingProfile != null)
                                {
                                    associatedClient = matchingProfile.Name;
                                }
                                else if (profiles.Count > 0)
                                {
                                    var fuzzyProf = profiles.Values.FirstOrDefault(p => name.IndexOf(p.Name, StringComparison.OrdinalIgnoreCase) >= 0 || p.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
                                    if (fuzzyProf != null)
                                    {
                                        associatedClient = fuzzyProf.Name;
                                    }
                                    else
                                    {
                                        associatedClient = profiles.Keys.First();
                                    }
                                }
                            }

                            if (!profiles.ContainsKey(associatedClient))
                            {
                                profiles[associatedClient] = new ProfileSettings
                                {
                                    Name = associatedClient,
                                    RmVersion = dbVersion,
                                    Alias = name,
                                    AutoLogin = true,
                                    VerboseLogs = true,
                                    ApagarHost = false
                                };
                                importedProfilesCount++;
                            }

                            var existingAlias = aliases.FirstOrDefault(a => a.name.Equals(name, StringComparison.OrdinalIgnoreCase) && a.client.Equals(associatedClient, StringComparison.OrdinalIgnoreCase));
                            if (existingAlias != null)
                            {
                                existingAlias.server = server;
                                existingAlias.Base = baseName;
                                existingAlias.dbType = dbType.ToLower() == "oracle" ? "oracle" : "sql";
                                existingAlias.dbUser = dbUser;
                                existingAlias.dbPass = dbPass;
                                existingAlias.rmUser = rmUser;
                                existingAlias.rmPass = rmPass;
                                existingAlias.runService = runService;
                                existingAlias.jobProcessing = jobProc;
                                existingAlias.localOnly = localOnly;
                                existingAlias.processPool = processPool;
                                existingAlias.maxThreads = maxThreads;
                                existingAlias.dbVersion = dbVersion;
                            }
                            else
                            {
                                var newAlias = new AliasConfig
                                {
                                    id = id,
                                    name = name,
                                    client = associatedClient,
                                    Base = baseName,
                                    server = server,
                                    dbType = dbType.ToLower() == "oracle" ? "oracle" : "sql",
                                    dbUser = dbUser,
                                    dbPass = dbPass,
                                    rmUser = rmUser,
                                    rmPass = rmPass,
                                    runService = runService,
                                    jobProcessing = jobProc,
                                    localOnly = localOnly,
                                    processPool = processPool,
                                    maxThreads = maxThreads,
                                    dbVersion = dbVersion
                                };
                                aliases.Add(newAlias);
                            }
                            importedAliasesCount++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    AddLog("error", $"Erro ao ler aliases do Toolkit: {ex.Message}");
                }
            }

            SaveProfiles();
            SaveAliases();
            UpdateProfilesUI();
            UpdateFilteredAliasesList();

            if (profiles.Count > 0)
            {
                var first = profiles.Values.First();
                LoadProfileToUI(first);
            }

            AddLog("info", $"✅ Importação do Toolkit concluída: {importedProfilesCount} cliente(s) e {importedAliasesCount} base(s) importadas de {foundDir}.");
            if (Environment.GetEnvironmentVariable("RMCORE_HEADLESS") != "1")
            {
                MessageBox.Show($"Sucesso!\n\nForam importados do Toolkit:\n• {importedProfilesCount} Cliente(s)\n• {importedAliasesCount} Base(s)\n\nOrigem: {foundDir}", "Importar do Toolkit", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        public void ImportarDoAtalhos(string? specificPath = null)
        {
            string? foundDb = null;

            if (!string.IsNullOrWhiteSpace(specificPath) && File.Exists(specificPath))
            {
                foundDb = specificPath;
            }
            else
            {
                // Busca automática em locais conhecidos do Atalhos
                var candidateDirs = new List<string>();
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

                candidateDirs.Add(Path.Combine(userProfile, "Documents", "Atalhos"));
                candidateDirs.Add(Path.Combine(localAppData, "Atalhos"));
                candidateDirs.Add(Path.Combine(appData, "Atalhos"));
                candidateDirs.Add(Path.Combine(userProfile, "Documents"));
                candidateDirs.Add(Path.Combine(userProfile, "Downloads"));
                candidateDirs.Add(Path.Combine(userProfile, "Desktop"));

                foreach (var dir in candidateDirs)
                {
                    if (Directory.Exists(dir))
                    {
                        try
                        {
                            var dbFiles = Directory.GetFiles(dir, "*.db", SearchOption.AllDirectories)
                                .Concat(Directory.GetFiles(dir, "*.sqlite", SearchOption.AllDirectories))
                                .Concat(Directory.GetFiles(dir, "*.sqlite3", SearchOption.AllDirectories))
                                .ToList();

                            var preferred = dbFiles.FirstOrDefault(f => Path.GetFileName(f).Equals("Atalhos.db", StringComparison.OrdinalIgnoreCase) ||
                                                                        Path.GetFileName(f).Equals("atalhos.db", StringComparison.OrdinalIgnoreCase) ||
                                                                        Path.GetFileName(f).Equals("database.sqlite", StringComparison.OrdinalIgnoreCase) ||
                                                                        Path.GetFileName(f).Equals("rmcore.db", StringComparison.OrdinalIgnoreCase));
                            if (preferred != null)
                            {
                                foundDb = preferred;
                                break;
                            }
                            else if (dbFiles.Count > 0)
                            {
                                foundDb = dbFiles[0];
                                break;
                            }
                        }
                        catch { /* ignore scan permission errors */ }
                    }
                }
            }

            if (string.IsNullOrEmpty(foundDb) || !File.Exists(foundDb))
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Selecione o banco de dados SQLite do Atalhos (Atalhos.db / database.sqlite)",
                    Filter = "Banco SQLite (*.db;*.sqlite;*.sqlite3)|*.db;*.sqlite;*.sqlite3|Todos os Arquivos (*.*)|*.*",
                    InitialDirectory = Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "Atalhos"))
                        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "Atalhos")
                        : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                };

                if (dlg.ShowDialog() == true)
                {
                    foundDb = dlg.FileName;
                }
                else
                {
                    return;
                }
            }

            int importedProfilesCount = 0;
            int importedAliasesCount = 0;

            try
            {
                using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={foundDb};Mode=ReadOnly");
                conn.Open();

                // 1. Mapeamento de Ambientes (Clientes)
                var ambMap = new Dictionary<string, string>(); // Id (string/Guid) -> Nome
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND (name='Ambientes' OR name='ambientes');";
                    var tableName = cmd.ExecuteScalar()?.ToString();

                    if (!string.IsNullOrEmpty(tableName))
                    {
                        using var ambCmd = conn.CreateCommand();
                        ambCmd.CommandText = $"SELECT * FROM {tableName};";
                        using var reader = ambCmd.ExecuteReader();
                        while (reader.Read())
                        {
                            string id = reader["Id"]?.ToString() ?? Guid.NewGuid().ToString();
                            string nome = reader["Nome"]?.ToString() ?? $"Cliente_{id}";
                            string rmVer = "";
                            try { rmVer = reader["Unidade"]?.ToString() ?? ""; } catch { }
                            if (string.IsNullOrWhiteSpace(rmVer))
                            {
                                try { rmVer = reader["RmVersion"]?.ToString() ?? "12.1.2602"; } catch { rmVer = "12.1.2602"; }
                            }
                            if (string.IsNullOrWhiteSpace(rmVer)) rmVer = "12.1.2602";

                            bool autoLogin = false;
                            try { autoLogin = Convert.ToBoolean(reader["AutoLogin"]); } catch { }

                            ambMap[id] = nome;

                            if (!profiles.ContainsKey(nome))
                            {
                                profiles[nome] = new ProfileSettings
                                {
                                    Name = nome,
                                    RmVersion = rmVer,
                                    Alias = "CorporeRM",
                                    AutoLogin = autoLogin,
                                    VerboseLogs = true,
                                    ApagarHost = false
                                };
                                importedProfilesCount++;
                            }
                        }
                    }
                }

                // 2. Aliases (Bases de Dados)
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND (name='Aliases' OR name='aliases');";
                    var tableName = cmd.ExecuteScalar()?.ToString();

                    if (!string.IsNullOrEmpty(tableName))
                    {
                        using var aliasCmd = conn.CreateCommand();
                        aliasCmd.CommandText = $"SELECT * FROM {tableName};";
                        using var reader = aliasCmd.ExecuteReader();
                        while (reader.Read())
                        {
                            string id = reader["Id"]?.ToString() ?? DateTime.Now.Ticks.ToString();
                            string nome = reader["Nome"]?.ToString() ?? "Base";
                            
                            string baseName = "CorporeRM";
                            try { baseName = reader["BaseName"]?.ToString() ?? "CorporeRM"; } catch { }
                            if (string.IsNullOrWhiteSpace(baseName))
                            {
                                try { baseName = reader["DbName"]?.ToString() ?? "CorporeRM"; } catch { }
                            }
                            if (string.IsNullOrWhiteSpace(baseName)) baseName = "CorporeRM";

                            string ambId = "";
                            try { ambId = reader["AmbienteId"]?.ToString() ?? ""; } catch { }
                            string clientName = ambMap.TryGetValue(ambId, out var cName) ? cName : "Cliente Padrão";

                            string servidor = "";
                            try { servidor = reader["Servidor"]?.ToString() ?? ""; } catch { }
                            if (string.IsNullOrWhiteSpace(servidor))
                            {
                                try { servidor = reader["DbServer"]?.ToString() ?? ""; } catch { }
                            }

                            string dbType = "sql";
                            try { dbType = reader["DbType"]?.ToString() ?? "sql"; } catch { }

                            string dbUser = "rm";
                            try { dbUser = reader["UsuarioDB"]?.ToString() ?? ""; } catch { }
                            if (string.IsNullOrWhiteSpace(dbUser))
                            {
                                try { dbUser = reader["DbUser"]?.ToString() ?? "rm"; } catch { dbUser = "rm"; }
                            }

                            string dbPass = "";
                            try { dbPass = reader["SenhaDB"]?.ToString() ?? ""; } catch { }
                            if (string.IsNullOrWhiteSpace(dbPass))
                            {
                                try { dbPass = reader["DbPass"]?.ToString() ?? ""; } catch { }
                            }

                            string rmUser = "mestre";
                            try { rmUser = reader["Usuario"]?.ToString() ?? "mestre"; } catch { }

                            string rmPass = "totvs";
                            try { rmPass = reader["Senha"]?.ToString() ?? "totvs"; } catch { }

                            bool runService = true;
                            try { runService = Convert.ToBoolean(reader["RunService"]); } catch { }

                            bool jobEnabled = false;
                            try { jobEnabled = Convert.ToBoolean(reader["JobServerEnabled"]); } catch { }

                            bool localOnly = false;
                            try { localOnly = Convert.ToBoolean(reader["JobServerLocalOnly"]); } catch { }

                            bool processPool = false;
                            try { processPool = Convert.ToBoolean(reader["JobServerProcessPoolEnabled"]); } catch { }

                            int maxThreads = 0;
                            try { maxThreads = Convert.ToInt32(reader["JobServerMaxThreads"]); } catch { }

                            string sgbd = "12.1.2602";
                            try { sgbd = reader["Sgbd"]?.ToString() ?? "12.1.2602"; } catch { }

                            if (!profiles.ContainsKey(clientName))
                            {
                                profiles[clientName] = new ProfileSettings
                                {
                                    Name = clientName,
                                    RmVersion = sgbd,
                                    Alias = nome,
                                    AutoLogin = true,
                                    VerboseLogs = true,
                                    ApagarHost = false
                                };
                                importedProfilesCount++;
                            }

                            var existing = aliases.FirstOrDefault(a => a.name.Equals(nome, StringComparison.OrdinalIgnoreCase) && a.client.Equals(clientName, StringComparison.OrdinalIgnoreCase));
                            if (existing != null)
                            {
                                existing.Base = baseName;
                                existing.server = servidor;
                                existing.dbType = dbType.ToLower().Contains("oracle") ? "oracle" : "sql";
                                existing.dbUser = dbUser;
                                existing.dbPass = dbPass;
                                existing.rmUser = rmUser;
                                existing.rmPass = rmPass;
                                existing.runService = runService;
                                existing.jobProcessing = jobEnabled;
                                existing.localOnly = localOnly;
                                existing.processPool = processPool;
                                existing.maxThreads = maxThreads;
                                existing.dbVersion = sgbd;
                            }
                            else
                            {
                                aliases.Add(new AliasConfig
                                {
                                    id = id,
                                    name = nome,
                                    client = clientName,
                                    Base = baseName,
                                    server = servidor,
                                    dbType = dbType.ToLower().Contains("oracle") ? "oracle" : "sql",
                                    dbUser = dbUser,
                                    dbPass = dbPass,
                                    rmUser = rmUser,
                                    rmPass = rmPass,
                                    runService = runService,
                                    jobProcessing = jobEnabled,
                                    localOnly = localOnly,
                                    processPool = processPool,
                                    maxThreads = maxThreads,
                                    dbVersion = sgbd
                                });
                            }
                            importedAliasesCount++;
                        }
                    }
                }

                SaveProfiles();
                SaveAliases();
                UpdateProfilesUI();
                UpdateFilteredAliasesList();

                if (profiles.Count > 0)
                {
                    var first = profiles.Values.First();
                    LoadProfileToUI(first);
                }

                AddLog("info", $"✅ Importação do Atalhos concluída: {importedProfilesCount} cliente(s) e {importedAliasesCount} base(s) importadas de {foundDb}.");
                if (Environment.GetEnvironmentVariable("RMCORE_HEADLESS") != "1")
                {
                    MessageBox.Show($"Sucesso!\n\nForam importados do Atalhos:\n• {importedProfilesCount} Cliente(s)\n• {importedAliasesCount} Base(s)\n\nOrigem: {foundDb}", "Importar de outros apps - Atalhos", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                AddLog("error", $"Erro ao importar do Atalhos: {ex.Message}");
                if (Environment.GetEnvironmentVariable("RMCORE_HEADLESS") != "1")
                {
                    MessageBox.Show($"Erro ao importar do Atalhos:\n\n{ex.Message}", "Importar Atalhos", MessageBoxButton.OK, MessageBoxImage.Error);
                }
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

                var choice = MessageBox.Show(
                    $"Deseja MESCLAR os {data.Profiles.Count} cliente(s) e {data.Aliases.Count} base(s) importados com os existentes?\n\n• Clique 'Sim' para Adicionar / Mesclar.\n• Clique 'Não' para Substituir Tudo.\n• Clique 'Cancelar' para Abortar.",
                    "Opção de Importação", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (choice == MessageBoxResult.Cancel) return;

                if (choice == MessageBoxResult.No)
                {
                    profiles.Clear();
                    aliases.Clear();
                }

                foreach (var p in data.Profiles) profiles[p.Name] = p;
                foreach (var a in data.Aliases)
                {
                    var existing = aliases.FirstOrDefault(x => x.id == a.id || (x.name == a.name && x.client == a.client));
                    if (existing == null)
                    {
                        aliases.Add(a);
                    }
                    else
                    {
                        int idx = aliases.IndexOf(existing);
                        aliases[idx] = a;
                    }
                }

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
            _telemetry?.Track("feature_used", new Dictionary<string, object> { ["feature"] = "reset_factory" });

            var result = MessageBox.Show("Tem certeza? Todos os dados (clientes, bases, configuracoes) serao perdidos.", "Reset de Fabrica", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                string appData = GetAppDataDir();

                string[] filesToDelete =
                {
                    "rmcore.db", "rmcore.db-shm", "rmcore.db-wal",
                    "app_settings.json", "window_settings.json",
                    "profiles.json", "aliases.json"
                };

                var failedFiles = new List<string>();
                foreach (var file in filesToDelete)
                {
                    string path = Path.Combine(appData, file);
                    if (!File.Exists(path)) continue;
                    try
                    {
                        File.Delete(path);
                        AddLog("info", $"Arquivo removido: {file}");
                    }
                    catch
                    {
                        failedFiles.Add(path);
                    }
                }

                if (failedFiles.Count > 0)
                {
                    string pendingFlag = Path.Combine(appData, "rmcore.delete_on_startup");
                    try
                    {
                        File.WriteAllLines(pendingFlag, failedFiles);
                        AddLog("warn", $"{failedFiles.Count} arquivo(s) marcados para remocao no proximo inicio.");
                    }
                    catch (Exception ex)
                    {
                        AddLog("error", $"Nao foi possivel agendar limpeza: {ex.Message}");
                    }
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();

                AddLog("info", "Dados resetados com sucesso.");
                MessageBox.Show("Dados resetados. O aplicativo sera reiniciado.", "Reset de Fabrica", MessageBoxButton.OK, MessageBoxImage.Information);

                string exePath = Environment.ProcessPath
                    ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                    ?? Application.ResourceAssembly.Location;

                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = exePath,
                        UseShellExecute = true,
                        WorkingDirectory = Path.GetDirectoryName(exePath) ?? string.Empty
                    };
                    System.Diagnostics.Process.Start(psi);
                }
                catch (Exception startEx)
                {
                    AddLog("error", $"Nao foi possivel reiniciar automaticamente: {startEx.Message}. Feche e abra o app manualmente.");
                }

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

            txtVersaoPanel.Text = "vAlpha-0.6.7";

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

        private async void btnBaixarUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingUpdate != null && !string.IsNullOrEmpty(_pendingUpdate.DownloadUrl))
            {
                btnBaixarUpdate.IsEnabled = false;
                btnBaixarUpdate.Content = "Baixando (0%)...";
                AddLog("info", $"[Auto-Update] Baixando atualização {_pendingUpdate.Version}...");

                try
                {
                    var svc = new UpdateService();
                    await svc.DownloadAndApplyUpdateAsync(_pendingUpdate.DownloadUrl, percent =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            btnBaixarUpdate.Content = $"Baixando ({percent}%)...";
                        });
                    });

                    btnBaixarUpdate.Content = "Instalando e reiniciando...";
                    AddLog("info", "[Auto-Update] Download concluído com sucesso. Reiniciando o app...");

                    await Task.Delay(800);

                    IsExiting = true;
                    SaveWindowSettings();
                    SaveAppSettings();
                    Application.Current.Shutdown();
                }
                catch (Exception ex)
                {
                    AddLog("error", $"[Auto-Update] Falha no download automático: {ex.Message}. Abrindo no navegador...");
                    btnBaixarUpdate.Content = "Atualizar e Reiniciar";
                    btnBaixarUpdate.IsEnabled = true;

                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = _pendingUpdate.DownloadUrl,
                            UseShellExecute = true
                        });
                    }
                    catch { }
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
        public bool   PrivacyAccepted     { get; set; } = false;
        public string RmInstallPath        { get; set; } = string.Empty; // pasta <versao>\Bin
        public string SsmsPath             { get; set; } = string.Empty; // executável Ssms.exe
        public List<string> FavoriteClientNames { get; set; } = new();
        public List<string> BaseFavoriteIds { get; set; } = new();
        public Dictionary<string, string> BaseTagColors { get; set; } = new();
        public string DefaultClient  { get; set; } = string.Empty;
        public string DefaultBaseId  { get; set; } = string.Empty;
        public string LastClient     { get; set; } = string.Empty;
        public string LastBaseId     { get; set; } = string.Empty;
        public string InstallId      { get; set; } = string.Empty;
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
