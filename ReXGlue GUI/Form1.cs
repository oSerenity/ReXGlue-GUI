using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ReXGlue_GUI
{
    public partial class Form1 : Form
    {
        private readonly FolderBrowserDialog _folderBrowserDialog;
        private const string REXSDK_ENV = "REXSDK";
        private const string BASESDK_ENV = "BaseSDKPath";
        private const EnvironmentVariableTarget EnvTarget = EnvironmentVariableTarget.User;

        public Form1()
        {
            InitializeComponent();

            _folderBrowserDialog = new FolderBrowserDialog
            {
                Description = "Select the parent folder that contains rexglue-sdk",
                UseDescriptionForTitle = true
            };

            // Prefer existing user-level REXSDK when present; otherwise ensure the user configures it once at startup.
            EnsureRexsdkConfigured();

            // Populate New Project tab root
            PopulateNewProjectRoot();
        }

        // Populate the New Project tab root folder from available environment or base folder
        private void PopulateNewProjectRoot()
        {
            try
            {
                // Prefer BaseSDKPath if set
                string baseSdk = Environment.GetEnvironmentVariable(BASESDK_ENV, EnvTarget) ?? string.Empty;
                string root = string.Empty;

                if (!string.IsNullOrWhiteSpace(baseSdk) && Directory.Exists(baseSdk))
                {
                    root = Path.Combine(baseSdk, "rexglue-sdk");
                }
                else
                {
                    string rexsdk = Environment.GetEnvironmentVariable(REXSDK_ENV, EnvTarget) ?? string.Empty;
                    var derived = DeriveBaseFromRexsdk(rexsdk);
                    if (!string.IsNullOrWhiteSpace(derived) && Directory.Exists(derived))
                    {
                        root = Path.Combine(derived, "rexglue-sdk");
                    }
                    else if (!string.IsNullOrWhiteSpace(textBoxBaseFolder.Text) && Directory.Exists(NormalizePath(textBoxBaseFolder.Text)))
                    {
                        root = Path.Combine(NormalizePath(textBoxBaseFolder.Text), "rexglue-sdk");
                    }
                }

                if (!string.IsNullOrWhiteSpace(root))
                {
                    textBoxNewProjectRoot.Text = NormalizePath(root);
                }
            }
            catch
            {
                // don't throw from UI update
            }
        }

        private void buttonNewBrowse_Click(object? sender, EventArgs e)
        {
            if (_folderBrowserDialog.ShowDialog(this) == DialogResult.OK)
            {
                textBoxNewProjectRoot.Text = _folderBrowserDialog.SelectedPath;
            }
        }

        private void buttonInitProject_Click(object? sender, EventArgs e)
        {
            string root = NormalizePath(textBoxNewProjectRoot.Text);
            string appName = textBoxAppName.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                MessageBox.Show("Please select a valid Root Folder for the new project.", "Missing Root", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(appName))
            {
                MessageBox.Show("Please enter an Application Name.", "Missing Name", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Initialize project (placeholder) — actual implementation of 'rexglue init' would be done here.
            string fullPath = Path.Combine(root, appName);
            MessageBox.Show($"Initialize project at:\n{fullPath}", "Initialize Project", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void textBoxBaseFolder_TextChanged(object sender, EventArgs e)
        {
            UpdatePreview();
            PopulateNewProjectRoot();
        }

        private void buttonBrowse_Click(object sender, EventArgs e)
        {
            if (_folderBrowserDialog.ShowDialog() == DialogResult.OK)
            {
                textBoxBaseFolder.Text = _folderBrowserDialog.SelectedPath;
            }
        }

        private void buttonApply_Click(object sender, EventArgs e)
        {
            string baseFolder = NormalizePath(textBoxBaseFolder.Text);
            if (string.IsNullOrWhiteSpace(baseFolder))
            {
                MessageBox.Show(
                    "Please select a base folder first.",
                    "Missing Folder",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }
            string rexsdkValue = Path.Combine(baseFolder, "rexglue-sdk", "out", "install", "win-amd64");
            string releasePath = Path.Combine(baseFolder, "rexglue-sdk", "out", "win-amd64", "Release");

            // Basic validations to prevent user mistakes
            string sdkFolder = Path.Combine(baseFolder, "rexglue-sdk");
            if (!Directory.Exists(sdkFolder))
            {
                MessageBox.Show(
                    $"The folder 'rexglue-sdk' was not found inside the selected base folder:\n{baseFolder}\n\nPlease select the correct parent folder.",
                    "Missing 'rexglue-sdk'",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            string existing = Environment.GetEnvironmentVariable(REXSDK_ENV, EnvTarget) ?? string.Empty;

            // If REXSDK is already set, do not modify it or Path. Instead set BaseSDKPath to the base folder.
            if (!string.IsNullOrWhiteSpace(existing))
            {
                string derivedBase = DeriveBaseFromRexsdk(existing) ?? baseFolder;
                if (string.IsNullOrWhiteSpace(derivedBase))
                {
                    MessageBox.Show(
                        "Could not derive a base folder from the existing REXSDK value. Please select the base folder manually.",
                        "Base Folder Required",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                if (!TrySetEnvWithConfirmation(BASESDK_ENV, derivedBase))
                    return;

                MessageBox.Show($"BaseSDKPath set to:\n{derivedBase}\n\nExisting REXSDK was left unchanged.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // No existing REXSDK — set REXSDK and update Path as before
            if (!Directory.Exists(rexsdkValue))
            {
                var dr = MessageBox.Show(
                    $"Computed REXSDK path does not exist:\n{rexsdkValue}\n\nThis may mean the SDK isn't installed yet.\nDo you want to continue and set the variable anyway?",
                    "REXSDK Path Missing",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (dr != DialogResult.Yes)
                    return;
            }

            if (!Directory.Exists(releasePath))
            {
                var dr = MessageBox.Show(
                    $"The expected Release folder does not exist:\n{releasePath}\n\nIf you proceed, the Path entry will still be added but may be invalid. Continue?",
                    "Release Path Missing",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (dr != DialogResult.Yes)
                    return;
            }

            if (!TrySetEnvWithConfirmation(REXSDK_ENV, rexsdkValue))
                return;

            if (!TryAddReleaseToUserPath(releasePath))
            {
                MessageBox.Show("Failed to update user Path.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            MessageBox.Show($"User variables updated successfully.\n\nREXSDK = {rexsdkValue}\nAdded to Path = {releasePath}", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void buttonClose_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void UpdatePreview()
        {
            // Prefer an existing user-level REXSDK if user didn't specify a base folder
            string existingRexsdk = Environment.GetEnvironmentVariable("REXSDK", EnvironmentVariableTarget.User) ?? string.Empty;
            string baseFolder = NormalizePath(textBoxBaseFolder.Text);

            if (string.IsNullOrWhiteSpace(baseFolder) && !string.IsNullOrWhiteSpace(existingRexsdk))
            {
                textBoxRexsdk.Text = existingRexsdk;
                var derived = DeriveBaseFromRexsdk(existingRexsdk);
                if (!string.IsNullOrWhiteSpace(derived))
                {
                    textBoxBaseFolder.Text = derived;
                    textBoxPathEntry.Text = Path.Combine(derived, "rexglue-sdk", "out", "win-amd64", "Release");
                }
                else
                {
                    textBoxPathEntry.Text = Path.Combine(existingRexsdk, "..", "Release");
                }

                return;
            }

            if (string.IsNullOrWhiteSpace(baseFolder))
            {
                textBoxRexsdk.Text = string.Empty;
                textBoxPathEntry.Text = string.Empty;
                return;
            }

            textBoxRexsdk.Text = Path.Combine(baseFolder, "rexglue-sdk", "out", "install", "win-amd64");
            textBoxPathEntry.Text = Path.Combine(baseFolder, "rexglue-sdk", "out", "win-amd64", "Release");
        }

        private static string NormalizePath(string? pathText)
        {
            if (string.IsNullOrWhiteSpace(pathText))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(pathText.Trim());
            }
            catch
            {
                return pathText.Trim();
            }
        }

        // Helper: derive base folder from a REXSDK path if it contains 'rexglue-sdk'
        private static string? DeriveBaseFromRexsdk(string rexsdk)
        {
            if (string.IsNullOrWhiteSpace(rexsdk))
                return null;

            try
            {
                int idx = rexsdk.IndexOf("rexglue-sdk", StringComparison.OrdinalIgnoreCase);
                if (idx <= 0)
                    return null;

                string baseFolder = rexsdk.Substring(0, idx).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return string.IsNullOrWhiteSpace(baseFolder) ? null : NormalizePath(baseFolder);
            }
            catch
            {
                return null;
            }
        }

        // Ensure REXSDK exists or prompt user to configure it at startup.
        private void EnsureRexsdkConfigured()
        {
            string existing = Environment.GetEnvironmentVariable(REXSDK_ENV, EnvTarget) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(existing))
            {
                if (Directory.Exists(existing))
                {
                    textBoxRexsdk.Text = existing;
                    var derived = DeriveBaseFromRexsdk(existing);
                    if (!string.IsNullOrWhiteSpace(derived))
                    {
                        textBoxBaseFolder.Text = derived;
                        textBoxPathEntry.Text = Path.Combine(derived, "rexglue-sdk", "out", "win-amd64", "Release");
                    }
                    else
                    {
                        textBoxPathEntry.Text = Path.Combine(existing, "..", "Release");
                    }

                    return; // existing good value
                }

                MessageBox.Show("A user variable named REXSDK was found but the path does not exist or is invalid.\n\nPlease select the correct base folder that contains 'rexglue-sdk' to ensure the tool can create new projects.",
                    "Invalid REXSDK", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            // No valid existing REXSDK — prompt user to select it. Allow explicit cancel to close app.
            while (true)
            {
                var dr = _folderBrowserDialog.ShowDialog(this);
                if (dr == DialogResult.OK)
                {
                    string selectedBase = NormalizePath(_folderBrowserDialog.SelectedPath);
                    string sdkFolder = Path.Combine(selectedBase, "rexglue-sdk");
                    if (!Directory.Exists(sdkFolder))
                    {
                        MessageBox.Show($"The folder 'rexglue-sdk' was not found inside the selected base folder:\n{selectedBase}\n\nPlease select the correct parent folder.",
                            "Missing 'rexglue-sdk'", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        continue; // require correct selection
                    }

                    string rexsdkValue = Path.Combine(selectedBase, "rexglue-sdk", "out", "install", "win-amd64");
                    string releasePath = Path.Combine(selectedBase, "rexglue-sdk", "out", "win-amd64", "Release");

                    try
                    {
                        // Ask for confirmation before writing user environment
                        if (!TrySetEnvWithConfirmation(REXSDK_ENV, rexsdkValue))
                            continue;

                        if (!TryAddReleaseToUserPath(releasePath))
                        {
                            MessageBox.Show("Failed to update user Path.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            continue;
                        }

                        MessageBox.Show($"REXSDK set to:\n{rexsdkValue}\nAdded to Path = {releasePath}", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        textBoxBaseFolder.Text = selectedBase;
                        UpdatePreview();
                        break; // done
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to set REXSDK: \n\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                else
                {
                    var confirm = MessageBox.Show("You must select the base folder that contains 'rexglue-sdk' to continue. Cancel will close the application.\n\nDo you want to cancel?",
                        "Selection Required", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (confirm == DialogResult.Yes)
                    {
                        // Close main form (will exit application)
                        Close();
                        return;
                    }
                }
            }
        }

        // Try to set an environment variable but confirm with the user if it already exists with different value.
        private bool TrySetEnvWithConfirmation(string name, string value)
        {
            string existing = Environment.GetEnvironmentVariable(name, EnvTarget) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(existing) && !string.Equals(NormalizePath(existing), NormalizePath(value), StringComparison.OrdinalIgnoreCase))
            {
                var dr = MessageBox.Show($"A user variable named {name} is already set to:\n{existing}\n\nDo you want to overwrite it with:\n{value}?",
                    "Confirm Overwrite", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (dr != DialogResult.Yes)
                    return false;
            }

            try
            {
                Environment.SetEnvironmentVariable(name, value, EnvTarget);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Add releasePath to user's Path if not already present
        private bool TryAddReleaseToUserPath(string releasePath)
        {
            try
            {
                string currentUserPath = Environment.GetEnvironmentVariable("Path", EnvTarget) ?? string.Empty;
                var entries = currentUserPath.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
                string normalizedRelease = NormalizePath(releasePath);
                bool existsRelease = entries.Any(p => string.Equals(NormalizePath(p), normalizedRelease, StringComparison.OrdinalIgnoreCase));
                if (!existsRelease)
                {
                    entries.Add(releasePath);
                    string newPath = string.Join(";", entries.Distinct(StringComparer.OrdinalIgnoreCase));
                    Environment.SetEnvironmentVariable("Path", newPath, EnvTarget);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
