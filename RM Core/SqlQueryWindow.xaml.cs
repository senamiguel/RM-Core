using System;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Microsoft.Data.SqlClient;

namespace RM_Core
{
    public partial class SqlQueryWindow : Window
    {
        public static MainWindow? ActiveMainWindow { get; set; }

        private AliasConfig? _activeAlias;

        public SqlQueryWindow()
        {
            InitializeComponent();
            Loaded += SqlQueryWindow_Loaded;
        }

        private void SqlQueryWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (Owner is MainWindow mw)
            {
                ActiveMainWindow = mw;
                _activeAlias = mw.GetActiveAlias();
                if (_activeAlias != null)
                {
                    txtDbInfo.Text = $"Servidor: {_activeAlias.server} | Base: {_activeAlias.Base} | Tipo: {_activeAlias.dbType}";
                }
                else
                {
                    txtDbInfo.Text = "Servidor: - | Base: - | Tipo: -";
                    txtStatus.Text = "Nenhuma base ativa selecionada.";
                    txtStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 77, 79));
                }
            }
        }

        private void btnExecutar_Click(object sender, RoutedEventArgs e)
        {
            if (_activeAlias == null)
            {
                txtStatus.Text = "Nenhuma base ativa selecionada.";
                txtStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 77, 79));
                return;
            }

            string query = txtSqlQuery.Text.Trim();
            if (string.IsNullOrEmpty(query))
            {
                txtStatus.Text = "Digite uma consulta SQL.";
                txtStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 77, 79));
                return;
            }

            if (_activeAlias.dbType.Equals("oracle", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Oracle não suportado ainda", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtStatus.Text = "Oracle não suportado ainda.";
                txtStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 77, 79));
                return;
            }

            ExecutarSqlServer(query);
        }

        private void ExecutarSqlServer(string query)
        {
            string connectionString = $"Server={_activeAlias!.server};Database={_activeAlias.Base};User Id={_activeAlias.dbUser};Password={_activeAlias.dbPass};TrustServerCertificate=True;";

            try
            {
                using var conn = new SqlConnection(connectionString);
                conn.Open();

                using var adapter = new SqlDataAdapter(query, conn);
                var dt = new DataTable();
                adapter.Fill(dt);

                gridResultados.ItemsSource = dt.DefaultView;

                txtStatus.Text = $"Consulta executada. {dt.Rows.Count} linha(s) retornada(s).";
                txtStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80));
            }
            catch (Exception ex)
            {
                txtStatus.Text = $"Erro: {ex.Message}";
                txtStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 77, 79));
            }
        }
    }
}
