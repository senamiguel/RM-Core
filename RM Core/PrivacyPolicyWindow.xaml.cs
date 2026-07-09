using System.Windows;

namespace RM_Core
{
    public partial class PrivacyPolicyWindow : Window
    {
        public PrivacyPolicyWindow()
        {
            InitializeComponent();
        }

        private void btnFechar_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
