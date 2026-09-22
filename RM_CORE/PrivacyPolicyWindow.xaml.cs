using System.Windows;

namespace RM_Core
{
    public partial class PrivacyPolicyWindow : Window
    {
        public PrivacyPolicyWindow()
        {
            InitializeComponent();
        }

        protected override void OnSourceInitialized(System.EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                iNKORE.UI.WPF.Modern.Helpers.Styles.BackdropHelper.ApplyDarkMode(this);
            }
            catch { }
        }

        private void btnFechar_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
