using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SolidWorks.TaskpaneCalculator.UI
{
    public partial class DBSettingsControl : UserControl
    {
        public DBSettingsControl()
        {
            InitializeComponent();
            BtnSave.Click += BtnSave_Click;
            BtnLoadEnv.Click += BtnLoadEnv_Click;
        }

        private void BtnLoadEnv_Click(object sender, RoutedEventArgs e)
        {
            // Load from environment variables if set
            TxtUri.Text = Environment.GetEnvironmentVariable("MONGODB_URI") ?? Environment.GetEnvironmentVariable("MONGO_LOG_CONN") ?? TxtUri.Text;
            TxtDb.Text = Environment.GetEnvironmentVariable("MONGODB_DB") ?? TxtDb.Text;
            TxtPassword.Text = Environment.GetEnvironmentVariable("MONGODB_PW") ?? TxtPassword.Text;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            // For dev purpose we will expose the password plaintext and write to user environment variables
            try
            {
                Environment.SetEnvironmentVariable("MONGODB_URI", TxtUri.Text, EnvironmentVariableTarget.User);
                Environment.SetEnvironmentVariable("MONGODB_DB", TxtDb.Text, EnvironmentVariableTarget.User);
                Environment.SetEnvironmentVariable("MONGODB_PW", TxtPassword.Text, EnvironmentVariableTarget.User);
                MessageBox.Show("DB settings saved to user environment variables (password stored in plain text).", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save settings: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
