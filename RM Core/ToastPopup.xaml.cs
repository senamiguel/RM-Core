using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace RM_Core
{
    public partial class ToastPopup : Window
    {
        private readonly int _durationMs;

        public ToastPopup(string title, string message, int durationMs = 4000)
        {
            InitializeComponent();
            txtTitulo.Text = title;
            txtMensagem.Text = message;
            _durationMs = durationMs;
            Owner = Application.Current.MainWindow;
            Loaded += OnLoaded;
            MouseDown += (_, _) => Close();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Posiciona no canto inferior direito
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - 16;
            Top = workArea.Bottom - Height - 16;

            // Animação de fade in (300ms)
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
            BeginAnimation(OpacityProperty, fadeIn);

            // Fecha após o tempo determinado
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(_durationMs)
            };
            timer.Tick += (s, args) =>
            {
                timer.Stop();

                // Fade out (300ms) e fecha
                var fadeOut = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(300));
                fadeOut.Completed += (_, _) => Close();
                BeginAnimation(OpacityProperty, fadeOut);
            };
            timer.Start();
        }

        protected override void OnDeactivated(EventArgs e)
        {
            base.OnDeactivated(e);
            // Fecha se perder foco (clicou fora)
            Close();
        }
    }
}
