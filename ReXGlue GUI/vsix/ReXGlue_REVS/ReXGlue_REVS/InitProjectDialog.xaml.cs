using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.Shell;

namespace ReXGlue_REVS
{
    public partial class InitProjectDialog : Window
    {
        public InitProjectDialog()
        {
            InitializeComponent();
            textBoxRoot.TextChanged += (s, e) => UpdateFullPath();
            textBoxAppName.TextChanged += (s, e) => UpdateFullPath();
        }

        /// <summary>After successful init, the full project path (root + app name).</summary>
        public string CreatedProjectPath { get; private set; }

        /// <summary>Application name as entered (for resolving &lt;app&gt;_config.toml).</summary>
        public string CreatedAppName { get; private set; }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private void UpdateFullPath()
        {
            string root = NormalizePath(textBoxRoot.Text);
            string app = (textBoxAppName?.Text ?? "").Trim();
            textFullPath.Text = (!string.IsNullOrEmpty(root) && !string.IsNullOrEmpty(app))
                ? "Full path: " + Path.Combine(root, app)
                : "Full path: (fill in root and name above)";
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
            {
                dlg.Description = "Select the root folder for the new project";
                dlg.SelectedPath = textBoxRoot.Text;
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    textBoxRoot.Text = dlg.SelectedPath;
            }
        }

        private async void Initialize_Click(object sender, RoutedEventArgs e)
        {
            string root = NormalizePath(textBoxRoot.Text);
            string app = (textBoxAppName?.Text ?? "").Trim();
            if (string.IsNullOrEmpty(root))
            {
                MessageBox.Show("Please select a valid Root Folder.", "Missing Root", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!Directory.Exists(root))
            {
                MessageBox.Show("Root folder does not exist. Please select a valid folder.", "Invalid Root", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrEmpty(app))
            {
                MessageBox.Show("Please enter an Application Name.", "Missing Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string fullPath = Path.Combine(root, app);
            var btn = (Button)sender;
            btn.IsEnabled = false;
            try
            {
                bool ok = await RexglueRunner.RunInitAsync(root, app);
                if (ok)
                {
                    CreatedProjectPath = fullPath;
                    CreatedAppName = app;
                    MessageBox.Show("Project initialized at:\n" + fullPath, "Initialize Project", MessageBoxButton.OK, MessageBoxImage.Information);
                    DialogResult = true;
                    Close();
                }
                else
                    MessageBox.Show("rexglue init exited with a non-zero code.\nCheck the ReXGlue tool Output.", "Initialize Project", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                btn.IsEnabled = true;
            }
        }
    }
}
