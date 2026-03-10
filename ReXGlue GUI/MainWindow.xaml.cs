using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using EnvDTE;
using EnvDTE80;
using Path = System.IO.Path;
using Process = System.Diagnostics.Process;
using TextRange = System.Windows.Documents.TextRange;
using Window = System.Windows.Window;

namespace ReXGlue_GUI
{
    public partial class MainWindow : Window
    {
        // ── Constants ─────────────────────────────────────────────────────
        private const string REXSDK_ENV  = "REXSDK";
        private const string BASESDK_ENV = "BaseSDKPath";
        private const EnvironmentVariableTarget EnvTarget = EnvironmentVariableTarget.User;

        private static readonly List<(string Label, string Value)> StarterTemplates = new()
        {
            ("= {}",             "= {}"),
            ("= { name, size }", "= { name = \"rex_sub_%ADDR%\", size = 0x0 }"),
            ("= { parent, size }", "= { parent = 0x00000000, size = 0x0 }"),
        };

        // Credit easter egg: type "futon" anywhere to show popup
        private const string CreditSecret = "futon";
        private string _creditBuffer = "";

        // ── Constructor ───────────────────────────────────────────────────
        public MainWindow()
        {
            InitializeComponent();
#if DEBUG
            Title = "ReXGlue GUI (DEBUG)";
#endif
            textBoxTomlEditor.Document.PageWidth = 4000;
            textBoxTomlEditor.SelectionChanged  += textBoxTomlEditor_SelectionChanged;

            BuildStarterChips();
            CheckAndConfigureSdk();
            PopulateNewProjectRoot();

            // Restore last session TOML
            string? last = LoadLastTomlPath();
            if (last != null)
            {
                tabBtnCodeGen.IsChecked = true;
                tabBtnCodeGen.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ToggleButton.CheckedEvent));
                LoadTomlFile(last);
            }
        }

        // Path to the small state file that remembers the last loaded TOML
        private static readonly string StatePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ReXGlue", "last_toml.txt");

        // Shared handoff folder used by IDC script — must exist before IDC runs
        private static readonly string AppDataReXGlue = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ReXGlue");

        private static void SaveLastTomlPath(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
                Directory.CreateDirectory(AppDataReXGlue);
                File.WriteAllText(StatePath, path);
            }
            catch { }
        }

        private static string? LoadLastTomlPath()
        {
            try
            {
                if (!File.Exists(StatePath)) return null;
                string p = File.ReadAllText(StatePath).Trim();
                return File.Exists(p) ? p : null;
            }
            catch { return null; }
        }

        private static string? BrowseForFolder(string title = "Select folder")
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = title, Multiselect = false };
            return dlg.ShowDialog() == true ? dlg.FolderName : null;
        }

        private static string? BrowseForFile(string title, string filter)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        private static string? PromptInput(string title, string prompt, string prefill = "")
        {
            var tb = new TextBox
            {
                Background             = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)),
                Foreground             = Brushes.LightGray,
                BorderBrush            = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                FontFamily             = new FontFamily("Consolas"),
                FontSize               = 12,
                Height                 = 28,
                Padding                = new Thickness(6, 0, 6, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin                 = new Thickness(0, 0, 0, 16),
                Text                   = prefill
            };
            var btnOk = new Button
            {
                Content              = "OK",
                Width                = 80,
                Height               = 28,
                HorizontalAlignment  = HorizontalAlignment.Right,
                Background           = new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x2E)),
                Foreground           = Brushes.LightGray,
                BorderBrush          = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44))
            };
            var stack = new StackPanel { Margin = new Thickness(20) };
            stack.Children.Add(new TextBlock
            {
                Text         = prompt,
                Foreground   = Brushes.LightGray,
                FontFamily   = new FontFamily("Segoe UI"),
                FontSize     = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 0, 0, 10)
            });
            stack.Children.Add(tb);
            stack.Children.Add(btnOk);

            var win = new Window
            {
                Title                  = title,
                MinWidth               = 380, Width  = 420,
                MinHeight              = 200, Height = 220,
                WindowStartupLocation  = WindowStartupLocation.CenterOwner,
                Background             = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x24)),
                ResizeMode             = ResizeMode.CanResize,
                Content                = stack
            };
            btnOk.Click += (_, _) => win.DialogResult = true;

            // Select all pre-filled text so user can immediately overtype
            if (!string.IsNullOrEmpty(prefill))
                tb.Loaded += (_, _) => { tb.Focus(); tb.SelectAll(); };

            return win.ShowDialog() == true ? tb.Text : null;
        }

        private static string NormalizePath(string? p)
        {
            if (string.IsNullOrWhiteSpace(p)) return string.Empty;
            try { return Path.GetFullPath(p.Trim()); } catch { return p.Trim(); }
        }

        private static string? DeriveBaseFromRexsdk(string rexsdk)
        {
            if (string.IsNullOrWhiteSpace(rexsdk)) return null;
            try
            {
                int idx = rexsdk.IndexOf("rexglue-sdk", StringComparison.OrdinalIgnoreCase);
                if (idx <= 0) return null;
                string b = rexsdk[..idx].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return string.IsNullOrWhiteSpace(b) ? null : NormalizePath(b);
            }
            catch { return null; }
        }

        private string GetEnv(string name) =>
            Environment.GetEnvironmentVariable(name, EnvTarget) ?? string.Empty;

        private bool TrySetEnvWithConfirmation(string name, string value)
        {
            string existing = GetEnv(name);
            if (!string.IsNullOrWhiteSpace(existing) &&
                !string.Equals(NormalizePath(existing), NormalizePath(value), StringComparison.OrdinalIgnoreCase))
            {
                if (MessageBox.Show($"'{name}' is already set to:\n{existing}\n\nOverwrite with:\n{value}?",
                    "Confirm Overwrite", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return false;
            }
            try { Environment.SetEnvironmentVariable(name, value, EnvTarget); return true; }
            catch { return false; }
        }

        private bool TryAddReleaseToUserPath(string releasePath)
        {
            try
            {
                string norm    = NormalizePath(releasePath);
                var entries    = GetEnv("Path")
                                    .Split(';', StringSplitOptions.RemoveEmptyEntries)
                                    .Select(p => p.Trim())
                                    .Where(p => !string.IsNullOrWhiteSpace(p))
                                    .ToList();
                if (!entries.Any(p => string.Equals(NormalizePath(p), norm, StringComparison.OrdinalIgnoreCase)))
                {
                    entries.Add(releasePath);
                    Environment.SetEnvironmentVariable("Path",
                        string.Join(";", entries.Distinct(StringComparer.OrdinalIgnoreCase)), EnvTarget);
                }
                return true;
            }
            catch { return false; }
        }

        // Warn and return false if missing; show MessageBox with given title.
        private static bool Require(string value, string message, string title = "Missing")
        {
            if (!string.IsNullOrWhiteSpace(value)) return true;
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        // ── TAB NAVIGATION ────────────────────────────────────────────────

        private void TabBtn_Checked(object sender, RoutedEventArgs e)
        {
            if (panelNewProject == null || sender is not RadioButton rb) return;
            panelNewProject.Visibility = Visibility.Collapsed;
            panelCodeGen.Visibility    = Visibility.Collapsed;
            panelAddrParser.Visibility = Visibility.Collapsed;
            panelOutput.Visibility     = Visibility.Collapsed;
            switch (rb.Tag?.ToString())
            {
                case "NewProject": panelNewProject.Visibility = Visibility.Visible; break;
                case "CodeGen":    panelCodeGen.Visibility    = Visibility.Visible; break;
                case "AddrParser":
                    panelAddrParser.Visibility = Visibility.Visible;
                    CheckClipboardForSetjmp();
                    break;
                case "Output":     panelOutput.Visibility     = Visibility.Visible; break;
            }
        }

        private static readonly string SetjmpFileName = "rexglue_setjmp.txt";

        // Matches where the IDC script writes via cmd: %APPDATA%\ReXGlue\rexglue_setjmp.txt
        private static readonly string SetjmpIdaUserPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ReXGlue", "rexglue_setjmp.txt");

        private static readonly Regex RxSetjmpLine =
            new(@"(setjmp_address|longjmp_address)\s*=\s*(0x[0-9a-fA-F]+)", RegexOptions.IgnoreCase);

        private void CheckClipboardForSetjmp()
        {
            try
            {
                // 1. IDA user dir — matches get_user_idadir() in IDC (primary)
                // 2. Next to the loaded TOML (fallback for manual drops)
                string? foundPath = null;

                if (File.Exists(SetjmpIdaUserPath))
                    foundPath = SetjmpIdaUserPath;

                if (foundPath == null && !string.IsNullOrWhiteSpace(_currentTomlPath))
                {
                    string sibling = Path.Combine(Path.GetDirectoryName(_currentTomlPath)!, SetjmpFileName);
                    if (File.Exists(sibling)) foundPath = sibling;
                }

                if (foundPath == null) return;

                string content = File.ReadAllText(foundPath).Trim();
                if (string.IsNullOrWhiteSpace(content)) return;

                string? setjmp = null, longjmp = null;
                foreach (string line in content.Replace("\r\n", "\n").Split('\n'))
                {
                    var m = RxSetjmpLine.Match(line);
                    if (!m.Success) continue;
                    string key = m.Groups[1].Value.ToLowerInvariant();
                    string val = m.Groups[2].Value;
                    if (key == "setjmp_address")  setjmp  = val;
                    if (key == "longjmp_address") longjmp = val;
                }

                if (setjmp == null && longjmp == null) return;
                if (string.IsNullOrWhiteSpace(_currentTomlPath) || !File.Exists(_currentTomlPath))
                {
                    AppendOutput("[setjmp] IDC result found but no TOML is loaded.", OutWarn);
                    return;
                }

                string msg = "IDC setjmp data found:\n";
                if (setjmp  != null) msg += $"  setjmp_address  = {setjmp}\n";
                if (longjmp != null) msg += $"  longjmp_address = {longjmp}\n";
                msg += "\nApply to loaded TOML?";

                if (MessageBox.Show(msg, "Apply setjmp / longjmp?",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

                File.Delete(foundPath);
                ApplySetjmpToToml(setjmp, longjmp);
            }
            catch { }
        }

        private void ApplySetjmpToToml(string? setjmp, string? longjmp)
        {
            var lines   = NormalizedLines(GetEditorText());
            int applied = 0;

            for (int i = 0; i < lines.Count; i++)
            {
                string t = lines[i].Trim();
                if (setjmp != null && t.StartsWith("setjmp_address", StringComparison.OrdinalIgnoreCase))
                { lines[i] = $"setjmp_address = {setjmp}";   applied++; setjmp  = null; }
                else if (longjmp != null && t.StartsWith("longjmp_address", StringComparison.OrdinalIgnoreCase))
                { lines[i] = $"longjmp_address = {longjmp}"; applied++; longjmp = null; }
            }

            // Append any not found — insert before [functions] or at end
            if (setjmp != null || longjmp != null)
            {
                int funcIdx  = lines.FindIndex(l => l.Trim().Equals("[functions]", StringComparison.OrdinalIgnoreCase));
                int insertAt = funcIdx >= 0 ? funcIdx : lines.Count;
                if (setjmp  != null) { lines.Insert(insertAt, $"setjmp_address = {setjmp}");   insertAt++; applied++; }
                if (longjmp != null) { lines.Insert(insertAt, $"longjmp_address = {longjmp}"); applied++; }
            }

            SetEditorText(string.Join("\n", lines));
            buttonSave_Click(this, new RoutedEventArgs());
            AppendOutput($"[setjmp] Applied {applied} value(s) to TOML and saved.", OutOk);
            tabBtnCodeGen.IsChecked = true;
            tabBtnCodeGen.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ToggleButton.CheckedEvent));
        }

        private void buttonTheme_Click(object sender, RoutedEventArgs e)
            => MessageBox.Show("Theme toggle coming soon.", "Theme", MessageBoxButton.OK, MessageBoxImage.Information);

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key is Key.Escape or Key.Enter or Key.Space) return;
            char ch = KeyToChar(e);
            if (ch == '\0') return;
            _creditBuffer += char.ToLowerInvariant(ch);
            if (_creditBuffer.Length > CreditSecret.Length)
                _creditBuffer = _creditBuffer[^CreditSecret.Length..];
            if (_creditBuffer == CreditSecret)
            {
                _creditBuffer = "";
                ShowCreditPopup();
            }
        }

        private static char KeyToChar(KeyEventArgs e)
        {
            if (e.Key < Key.A || e.Key > Key.Z) return '\0';
            bool shift = e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Shift);
            return (char)((shift ? 'A' : 'a') + (e.Key - Key.A));
        }

        private void ShowCreditPopup() =>
            MessageBox.Show("     ReXGlue GUI\n     Made by MaxDeadBear\n     Vodka Doc\n", "♥", MessageBoxButton.OK, MessageBoxImage.None);

        private void buttonRefresh_Click(object sender, RoutedEventArgs e)
        {
            CheckAndConfigureSdk();
            PopulateNewProjectRoot();
        }

        // ── SDK SETUP OVERLAY ─────────────────────────────────────────────

        private void CheckAndConfigureSdk()
        {
            RefreshStatusIndicators();
            string rexsdk  = GetEnv(REXSDK_ENV);
            string baseSdk = GetEnv(BASESDK_ENV);
            bool rexOk  = !string.IsNullOrWhiteSpace(rexsdk)  && Directory.Exists(rexsdk);
            bool baseOk = !string.IsNullOrWhiteSpace(baseSdk) && Directory.Exists(baseSdk);
            if (rexOk && baseOk) { panelSdkSetup.Visibility = Visibility.Collapsed; return; }
            if (!string.IsNullOrWhiteSpace(rexsdk))
            {
                string? d = DeriveBaseFromRexsdk(rexsdk);
                if (!string.IsNullOrWhiteSpace(d)) textBoxSetupBaseFolder.Text = d;
            }
            textSdkSetupMessage.Text = rexOk
                ? "BaseSDKPath is missing. Please confirm your SDK base folder."
                : "REXSDK is not configured. Select the parent folder that contains 'rexglue-sdk'.";
            panelSdkSetup.Visibility = Visibility.Visible;
        }

        private void RefreshStatusIndicators()
        {
            string rexsdk   = GetEnv(REXSDK_ENV);
            string baseSdk  = GetEnv(BASESDK_ENV);
            string userPath = GetEnv("Path");
            SetStatus(statusRexsdk,  rexsdk,  Directory.Exists(rexsdk));
            SetStatus(statusBaseSdk, baseSdk, Directory.Exists(baseSdk));
            bool pathOk = userPath.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Any(p => p.Contains("rexglue-sdk", StringComparison.OrdinalIgnoreCase)
                       && p.Contains("Release",     StringComparison.OrdinalIgnoreCase));
            statusPathEntry.Text  = pathOk ? "● Set" : "● Not set";
            statusPathEntry.Style = (Style)FindResource(pathOk ? "StatusOk" : "StatusWarn");
        }

        private void SetStatus(TextBlock tb, string value, bool exists)
        {
            (tb.Text, tb.Style) = string.IsNullOrWhiteSpace(value) ? ("● Not set",      (Style)FindResource("StatusWarn"))
                                : !exists                          ? ("● Invalid path", (Style)FindResource("StatusWarn"))
                                :                                    ($"● {value}",     (Style)FindResource("StatusOk"));
        }

        private void textBoxSetupBaseFolder_TextChanged(object sender, TextChangedEventArgs e)
        {
            string b = NormalizePath(textBoxSetupBaseFolder.Text);
            textBoxSetupPreviewRexsdk.Text = string.IsNullOrWhiteSpace(b) ? string.Empty
                : Path.Combine(b, "rexglue-sdk", "out", "install", "win-amd64");
            textBoxSetupPreviewPath.Text = string.IsNullOrWhiteSpace(b) ? string.Empty
                : Path.Combine(b, "rexglue-sdk", "out", "win-amd64", "Release");
        }

        private void buttonSetupBrowse_Click(object sender, RoutedEventArgs e)
        {
            string? s = BrowseForFolder("Select the parent folder that contains rexglue-sdk");
            if (s != null) textBoxSetupBaseFolder.Text = NormalizePath(s);
        }

        private async void buttonSetupApply_Click(object sender, RoutedEventArgs e)
        {
            string baseFolder = NormalizePath(textBoxSetupBaseFolder.Text);
            if (!Require(baseFolder, "Please select a base folder.", "Missing Folder")) return;

            string sdkRoot = Path.Combine(baseFolder, "rexglue-sdk");
            if (!Directory.Exists(sdkRoot))
            {
                if (MessageBox.Show(
                    $"'rexglue-sdk' not found inside:\n{baseFolder}\n\n" +
                    "Would you like to clone the SDK from GitHub into this folder now?\n\n" +
                    "Command:\n  git clone https://github.com/rexglue/rexglue-sdk",
                    "Clone SDK from GitHub?", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;

                try
                {
                    Directory.CreateDirectory(baseFolder);
                    var proc = Process.Start(new ProcessStartInfo
                    {
                        FileName               = "git",
                        Arguments              = "clone https://github.com/rexglue/rexglue-sdk",
                        WorkingDirectory       = baseFolder,
                        UseShellExecute        = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError  = true,
                        CreateNoWindow         = true
                    });

                    if (proc == null)
                    {
                        MessageBox.Show("Failed to start 'git'. Ensure Git is installed and on PATH.",
                            "Git Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    string stdout = string.Empty, stderr = string.Empty;
                    await Task.Run(() =>
                    {
                        stdout = proc.StandardOutput.ReadToEnd();
                        stderr = proc.StandardError.ReadToEnd();
                        proc.WaitForExit();
                    });

                    AppendOutput($"[SDK Setup] git clone exit {proc.ExitCode}\n{stdout}" +
                                 (string.IsNullOrWhiteSpace(stderr) ? "" : "\n[stderr]\n" + stderr));

                    if (proc.ExitCode != 0 || !Directory.Exists(sdkRoot))
                    {
                        MessageBox.Show("Cloning 'rexglue-sdk' failed. Check the Output tab.",
                            "Clone Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    AppendOutput($"[SDK Setup] git clone error: {ex.Message}");
                    MessageBox.Show($"Failed to clone 'rexglue-sdk':\n{ex.Message}",
                        "Clone Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            string rexsdkValue = Path.Combine(sdkRoot, "out", "install", "win-amd64");
            string releasePath = Path.Combine(sdkRoot, "out", "win-amd64", "Release");

            if (!Directory.Exists(rexsdkValue))
            if (!TrySetEnvWithConfirmation(REXSDK_ENV,  rexsdkValue)) return;
            if (!TrySetEnvWithConfirmation(BASESDK_ENV, baseFolder))  return;
            if (!TryAddReleaseToUserPath(releasePath))
            { MessageBox.Show("Failed to update user Path.", "Error", MessageBoxButton.OK, MessageBoxImage.Error); return; }

            RefreshStatusIndicators();
            panelSdkSetup.Visibility = Visibility.Collapsed;
            PopulateNewProjectRoot();
            AppendOutput($"SDK configured:\n  REXSDK      = {rexsdkValue}\n  BaseSDKPath = {baseFolder}\n  Path        = {releasePath}");
        }

        private void buttonSetupCancel_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Close without configuring the SDK?", "Cancel",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) Close();
        }

        // ── NEW PROJECT TAB ───────────────────────────────────────────────

        private void PopulateNewProjectRoot()
        {
            try
            {
                string baseSdk  = GetEnv(BASESDK_ENV);
                string? derived = !string.IsNullOrWhiteSpace(baseSdk) && Directory.Exists(baseSdk)
                    ? baseSdk
                    : DeriveBaseFromRexsdk(GetEnv(REXSDK_ENV));
                if (!string.IsNullOrWhiteSpace(derived) && Directory.Exists(derived))
                    textBoxNewProjectRoot.Text = NormalizePath(Path.Combine(derived, "rexglue-sdk"));
            }
            catch { }
        }

        private void buttonNewBrowse_Click(object sender, RoutedEventArgs e)
        {
            string? s = BrowseForFolder("Select the root folder for the new project");
            if (s != null) textBoxNewProjectRoot.Text = s;
        }

        private void textBoxAppName_TextChanged(object sender, TextChangedEventArgs e)
        {
            placeholderAppName.Visibility = string.IsNullOrEmpty(textBoxAppName.Text)
                ? Visibility.Visible : Visibility.Collapsed;
            string root = NormalizePath(textBoxNewProjectRoot.Text);
            string app  = textBoxAppName.Text?.Trim() ?? string.Empty;
            textFullPath.Text = (!string.IsNullOrWhiteSpace(root) && !string.IsNullOrWhiteSpace(app))
                ? $"Full path: {Path.Combine(root, app)}"
                : "Full path: (fill in root and name above)";
        }

        private async void buttonInitProject_Click(object sender, RoutedEventArgs e)
        {
            string root = NormalizePath(textBoxNewProjectRoot.Text);
            string app  = textBoxAppName.Text?.Trim() ?? string.Empty;
            if (!Require(root, "Please select a valid Root Folder.", "Missing Root") || !Directory.Exists(root))
            { MessageBox.Show("Please select a valid Root Folder.", "Missing Root", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (!Require(app, "Please enter an Application Name.", "Missing Name")) return;

            string fullPath = Path.Combine(root, app);
            textBoxCodeGenDir.Text     = fullPath;
            var initBtn = (Button)sender;
            initBtn.IsEnabled      = false;
            tabBtnOutput.IsChecked = true;

            try
            {
                Directory.CreateDirectory(fullPath);
                string args = $"init --app_name \"{app}\" --app_root \"{fullPath}\"";
                AppendOutput($"[Initialize Project]\n  Command: rexglue {args}");

                Process? proc = RunRexglue(root, args);
                if (proc == null) { ShowProcessError("[Initialize Project]"); return; }

                await Task.Run(() => proc.WaitForExit());
                bool initOk = proc.ExitCode == 0;
                AppendOutput($"[Initialize Project] ExitCode: {proc.ExitCode}", initOk ? OutOk : OutErr);
                try { Directory.CreateDirectory(Path.Combine(fullPath, "assets")); } catch { }

                if (initOk)
                    MessageBox.Show($"Project initialized at:\n{fullPath}", "Initialize Project", MessageBoxButton.OK, MessageBoxImage.Information);
                else
                    MessageBox.Show($"rexglue exited with code {proc.ExitCode}.\nCheck the Output tab.", "Initialize Project", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                AppendOutput($"[Initialize Project Error] {ex.Message}");
                MessageBox.Show($"Initialization failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { initBtn.IsEnabled = true; }
        }

        private Process? RunRexglue(string workDir, string args, Action<string>? onLine = null)
        {
            if (string.IsNullOrWhiteSpace(workDir) || string.IsNullOrWhiteSpace(args)) return null;

            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName               = "rexglue",
                    Arguments              = args,
                    WorkingDirectory       = workDir,
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true
                },
                EnableRaisingEvents = true
            };

            proc.OutputDataReceived += (_, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;
                onLine?.Invoke(e.Data);
                Dispatcher.Invoke(() => AppendRawLine(e.Data));
            };
            proc.ErrorDataReceived += (_, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;
                onLine?.Invoke(e.Data);
                Dispatcher.Invoke(() => AppendRawLine("[ERR] " + e.Data));
            };

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            return proc;
        }

        private void ShowProcessError(string tag) =>
            MessageBox.Show("Failed to start 'rexglue'. Ensure it is installed and on PATH.", tag, MessageBoxButton.OK, MessageBoxImage.Error);

        // ── FUNCTIONS SECTION HELPERS ─────────────────────────────────────

        private void buttonWriteFunctions_Click(object sender, RoutedEventArgs e)
        {
            string text = GetEditorText();
            if (text.Contains("[functions]"))
            { MessageBox.Show("[functions] section already exists.", "Already Present", MessageBoxButton.OK, MessageBoxImage.Information); return; }

            var lines       = NormalizedLines(text);
            int insertIndex = lines.Count;

            var caretPara = textBoxTomlEditor.CaretPosition.Paragraph;
            if (caretPara != null)
            {
                var paras = textBoxTomlEditor.Document.Blocks.OfType<Paragraph>().ToList();
                int idx   = paras.IndexOf(caretPara);
                if (idx >= 0 && idx < lines.Count) insertIndex = idx;
            }

            if (lines.Count == 0)
                lines.Add("[functions]");
            else if (insertIndex >= lines.Count)
                lines[string.IsNullOrWhiteSpace(lines[^1]) ? lines.Count - 1 : lines.Count] = "[functions]";
            else if (string.IsNullOrWhiteSpace(lines[insertIndex]))
                lines[insertIndex] = "[functions]";
            else
                lines.Insert(insertIndex, "[functions]");

            SetEditorText(string.Join("\n", lines));
            AppendOutput("[Functions] Wrote [functions] header at caret line.");
        }

        private void buttonAddFunctionAddress_Click(object sender, RoutedEventArgs e)
        {
            string? clipAddr = TryExtractAddressFromClipboard();
            string? addr;
            if (clipAddr != null)
            {
                addr = clipAddr;
            }
            else
            {
                string clipRaw = string.Empty;
                try { if (Clipboard.ContainsText()) clipRaw = Clipboard.GetText().Trim(); } catch { }
                addr = PromptInput("Add Function Address",
                    "Enter function address (e.g. 0x827E9A60):", prefill: clipRaw);
            }

            if (string.IsNullOrWhiteSpace(addr)) return;
            addr = addr.Trim();
            if (!addr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                addr = "0x" + addr.TrimStart('0', 'x', 'X');

            var lines = NormalizedLines(GetEditorText());
            var (inserted, _) = InjectAddressesIntoFunctions(lines, new[] { addr });
            if (inserted == 0)
            { MessageBox.Show($"Address {addr} already exists under [functions].", "Already Present", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            SetEditorText(string.Join("\n", lines));
            AppendOutput($"[Functions] Added {addr} = {{}}");
        }

        private static readonly Regex RxClipAddr =
            new(@"(0x8[0-9a-fA-F]{7})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Returns the first 0x8xxxxxxx address from the clipboard, or null.
        private static string? TryExtractAddressFromClipboard()
        {
            try
            {
                if (!Clipboard.ContainsText()) return null;
                var m = RxClipAddr.Match(Clipboard.GetText());
                return m.Success ? m.Groups[1].Value : null;
            }
            catch { return null; }
        }

        private void buttonWriteRexcrt_Click(object sender, RoutedEventArgs e)
        {
            string text = GetEditorText();
            if (text.Contains("setjmp_address") || text.Contains("longjmp_address"))
            { MessageBox.Show("setjmp/longjmp entries are already present.", "Already Present", MessageBoxButton.OK, MessageBoxImage.Information); return; }

            var lines = NormalizedLines(text);
            lines.Add("[rexcrt]");
            SetEditorText(string.Join("\n", lines));
            AppendOutput("Wrote [rexcrt] header at end.");
        }

        // ── EDITOR STATE ──────────────────────────────────────────────────

        private string          _currentTomlPath      = string.Empty;
        private bool            _suppressEditorUpdate = false;
        private bool            _highlightPending     = false;
        private bool            _isDirty              = false;
        private List<TextRange> _findMatches          = new();
        private int             _findIndex            = -1;

        // VS Code Dark+ token colours
        private static readonly SolidColorBrush ColDefault  = new(Color.FromRgb(0xD4, 0xD4, 0xD4));
        private static readonly SolidColorBrush ColComment  = new(Color.FromRgb(0x6A, 0x99, 0x55));
        private static readonly SolidColorBrush ColSection  = new(Color.FromRgb(0x56, 0x9C, 0xD6));
        private static readonly SolidColorBrush ColKey      = new(Color.FromRgb(0x9C, 0xDC, 0xFE));
        private static readonly SolidColorBrush ColEquals   = new(Color.FromRgb(0xD4, 0xD4, 0xD4));
        private static readonly SolidColorBrush ColHex      = new(Color.FromRgb(0xB5, 0xCE, 0xA8));
        private static readonly SolidColorBrush ColString   = new(Color.FromRgb(0xCE, 0x91, 0x78));
        private static readonly SolidColorBrush ColBool     = new(Color.FromRgb(0x56, 0x9C, 0xD6));
        private static readonly SolidColorBrush ColNumber   = new(Color.FromRgb(0xB5, 0xCE, 0xA8));
        private static readonly SolidColorBrush ColBrace    = new(Color.FromRgb(0xFF, 0xD7, 0x00));
        private static readonly SolidColorBrush ColIdent    = new(Color.FromRgb(0x9C, 0xDC, 0xFE));
        private static readonly SolidColorBrush ColFindMark = new(Color.FromRgb(0x51, 0x3A, 0x00));
        private static readonly SolidColorBrush ColFindCur  = new(Color.FromRgb(0xA8, 0x68, 0x00));

        // Compiled regexes — avoid recompiling per keystroke
        private static readonly Regex RxHex    = new(@"^(0[xX][0-9a-fA-F]+)(.*)$", RegexOptions.Compiled);
        private static readonly Regex RxHexChk = new(@"^0[xX][0-9a-fA-F]+",        RegexOptions.Compiled);
        private static readonly Regex RxNum    = new(@"^-?[0-9]+(\.[0-9]+)?$",      RegexOptions.Compiled);

        private static readonly SolidColorBrush StatusSaved   = new(Color.FromRgb(0x4E, 0xC9, 0x4E)); // green
        private static readonly SolidColorBrush StatusUnsaved = new(Color.FromRgb(0xFF, 0xCC, 0x00)); // yellow

        private void SetDirty(bool dirty)
        {
            _isDirty = dirty;
            if (string.IsNullOrWhiteSpace(_currentTomlPath)) return;
            string name = Path.GetFileName(_currentTomlPath);
            textStatusLeft.Text      = dirty ? $"{_currentTomlPath}  [unsaved]" : name;
            textStatusLeft.Foreground = dirty ? StatusUnsaved : StatusSaved;
        }

        // ── KEYBOARD SHORTCUTS ────────────────────────────────────────────

        private void textBoxTomlEditor_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            bool ctrl  = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            bool alt   = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);

            if (ctrl && !alt)
            {
                switch (e.Key)
                {
                    case Key.S:           buttonSave_Click(sender, e);   e.Handled = true; return;
                    case Key.F:           OpenFindBar(replace: false);   e.Handled = true; return;
                    case Key.H:           OpenFindBar(replace: true);    e.Handled = true; return;
                    case Key.G:           GoToLine();                    e.Handled = true; return;
                    case Key.D:           DuplicateLine();               e.Handled = true; return;
                    case Key.OemQuestion:
                    case Key.Divide:      ToggleLineComment();           e.Handled = true; return;
                    case Key.Home:        textBoxTomlEditor.CaretPosition = textBoxTomlEditor.Document.ContentStart; e.Handled = true; return;
                    case Key.End:         textBoxTomlEditor.CaretPosition = textBoxTomlEditor.Document.ContentEnd;   e.Handled = true; return;
                }
            }

            if (alt && !ctrl)
            {
                switch (e.Key)
                {
                    case Key.Up:   MoveLineUp();   e.Handled = true; return;
                    case Key.Down: MoveLineDown(); e.Handled = true; return;
                }
            }

            if (e.Key == Key.Escape && findBar.Visibility == Visibility.Visible)
            { CloseFindBar(); e.Handled = true; return; }

            if (e.Key == Key.Tab && !textBoxTomlEditor.Selection.IsEmpty)
            { IndentSelection(shift ? -1 : 1); e.Handled = true; }
        }

        // ── STATUS BAR ────────────────────────────────────────────────────

        private void textBoxTomlEditor_SelectionChanged(object sender, RoutedEventArgs e) => UpdateCursorStatus();

        private void UpdateCursorStatus()
        {
            try
            {
                var caret = textBoxTomlEditor.CaretPosition;
                int line  = 1;
                foreach (Block b in textBoxTomlEditor.Document.Blocks)
                {
                    if (b is Paragraph para)
                    {
                        if (para.ContentEnd.CompareTo(caret) < 0) line++;
                        else break;
                    }
                }
                var lineStart = caret.GetLineStartPosition(0) ?? caret.DocumentStart;
                int col = new TextRange(lineStart, caret).Text.Length + 1;
                textStatusCursor.Text = $"Ln {line}, Col {col}";
            }
            catch { }
        }

        // ── FIND / REPLACE ────────────────────────────────────────────────

        private void OpenFindBar(bool replace)
        {
            var replaceVis              = replace ? Visibility.Visible : Visibility.Collapsed;
            findBar.Visibility          = Visibility.Visible;
            labelReplace.Visibility     = replaceVis;
            textBoxReplace.Visibility   = replaceVis;
            buttonReplaceOne.Visibility = replaceVis;
            buttonReplaceAll.Visibility = replaceVis;
            if (!textBoxTomlEditor.Selection.IsEmpty)
                textBoxFind.Text = textBoxTomlEditor.Selection.Text;
            textBoxFind.Focus();
            textBoxFind.SelectAll();
            RunFind();
        }

        private void CloseFindBar()
        {
            findBar.Visibility = Visibility.Collapsed;
            ClearFindHighlights();
            _findMatches.Clear();
            _findIndex        = -1;
            textFindInfo.Text = string.Empty;
            textBoxTomlEditor.Focus();
        }

        private void textBoxFind_TextChanged(object sender, TextChangedEventArgs e) => RunFind();

        private void textBoxFind_KeyDown(object sender, KeyEventArgs e)
        {
            if      (e.Key == Key.Enter)  { (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? (Action)FindPrev : FindNext)(); e.Handled = true; }
            else if (e.Key == Key.Escape) { CloseFindBar(); e.Handled = true; }
        }

        private void textBoxReplace_KeyDown(object sender, KeyEventArgs e)
        {
            if      (e.Key == Key.Enter)  { buttonReplaceOne_Click(sender, e); e.Handled = true; }
            else if (e.Key == Key.Escape) { CloseFindBar();                    e.Handled = true; }
        }

        private void buttonFindNext_Click(object sender, RoutedEventArgs e)  => FindNext();
        private void buttonFindPrev_Click(object sender, RoutedEventArgs e)  => FindPrev();
        private void buttonFindClose_Click(object sender, RoutedEventArgs e) => CloseFindBar();

        private void buttonReplaceOne_Click(object sender, RoutedEventArgs e)
        {
            if (_findMatches.Count == 0 || _findIndex < 0) { RunFind(); return; }
            _findMatches[_findIndex].Text = textBoxReplace.Text;
            RunFind();
        }

        private void buttonReplaceAll_Click(object sender, RoutedEventArgs e)
        {
            string find = textBoxFind.Text;
            if (string.IsNullOrEmpty(find)) return;
            string text  = GetEditorText();
            string esc   = Regex.Escape(find);
            int count    = Regex.Matches(text, esc, RegexOptions.IgnoreCase).Count;
            SetEditorText(Regex.Replace(text, esc, textBoxReplace.Text, RegexOptions.IgnoreCase));
            textFindInfo.Text = $"{count} replacement(s) made";
            AppendOutput($"[Replace All] '{find}' → '{textBoxReplace.Text}' ({count} replacements)");
        }

        private void RunFind()
        {
            ClearFindHighlights();
            _findMatches.Clear();
            _findIndex = -1;
            string query = textBoxFind.Text;
            if (string.IsNullOrEmpty(query)) { textFindInfo.Text = string.Empty; return; }

            var pos = textBoxTomlEditor.Document.ContentStart;
            var end = textBoxTomlEditor.Document.ContentEnd;
            while (pos != null)
            {
                var found = FindNextOccurrence(pos, end, query);
                if (found == null) break;
                _findMatches.Add(found);
                found.ApplyPropertyValue(TextElement.BackgroundProperty, ColFindMark);
                pos = found.End;
            }

            if (_findMatches.Count == 0) { textFindInfo.Text = "No results"; return; }
            _findIndex = 0;
            HighlightCurrentMatch();
        }

        private void FindNext()
        { if (_findMatches.Count == 0) { RunFind(); return; } _findIndex = (_findIndex + 1) % _findMatches.Count; HighlightCurrentMatch(); }

        private void FindPrev()
        { if (_findMatches.Count == 0) { RunFind(); return; } _findIndex = (_findIndex - 1 + _findMatches.Count) % _findMatches.Count; HighlightCurrentMatch(); }

        private void HighlightCurrentMatch()
        {
            for (int i = 0; i < _findMatches.Count; i++)
                _findMatches[i].ApplyPropertyValue(TextElement.BackgroundProperty,
                    i == _findIndex ? (object)ColFindCur : ColFindMark);
            var match = _findMatches[_findIndex];
            match.Start.Paragraph?.BringIntoView();
            textBoxTomlEditor.CaretPosition = match.Start;
            textBoxTomlEditor.Selection.Select(match.Start, match.End);
            textFindInfo.Text = $"{_findIndex + 1} of {_findMatches.Count}";
        }

        private void ClearFindHighlights()
        {
            foreach (var r in _findMatches)
                r.ApplyPropertyValue(TextElement.BackgroundProperty, DependencyProperty.UnsetValue);
        }

        private static TextRange? FindNextOccurrence(TextPointer start, TextPointer end, string query)
        {
            for (var pos = start; pos != null && pos.CompareTo(end) < 0;
                 pos = pos.GetNextContextPosition(LogicalDirection.Forward))
            {
                if (pos.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.Text) continue;
                if (pos.GetAdjacentElement(LogicalDirection.Forward) is not Run run) continue;
                int idx = run.Text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;
                var s  = run.ContentStart.GetPositionAtOffset(idx);
                var e2 = run.ContentStart.GetPositionAtOffset(idx + query.Length);
                if (s != null && e2 != null) return new TextRange(s, e2);
            }
            return null;
        }

        // ── EDITING COMMANDS ──────────────────────────────────────────────

        private void GoToLine()
        {
            string? input = PromptInput("Go to Line", "Line number:");
            if (!int.TryParse(input, out int lineNum) || lineNum < 1) return;
            var blocks = textBoxTomlEditor.Document.Blocks.ToList();
            int target = Math.Min(lineNum - 1, blocks.Count - 1);
            if (blocks[target] is Paragraph para) { textBoxTomlEditor.CaretPosition = para.ContentStart; para.BringIntoView(); }
        }

        private void ToggleLineComment()
        {
            var para = textBoxTomlEditor.CaretPosition.Paragraph;
            if (para == null) return;
            var range   = new TextRange(para.ContentStart, para.ContentEnd);
            string line = range.Text;
            range.Text  = line.TrimStart().StartsWith('#')
                ? line.TrimStart()[1..].TrimStart()
                : "# " + line;
            HighlightDocument();
        }

        private void DuplicateLine()
        {
            var para = textBoxTomlEditor.CaretPosition.Paragraph;
            if (para == null) return;
            var newPara = new Paragraph { Margin = ZeroMargin };
            newPara.Inlines.Add(new Run(new TextRange(para.ContentStart, para.ContentEnd).Text));
            para.SiblingBlocks.InsertAfter(para, newPara);
            HighlightDocument();
        }

        private void MoveLineUp()
        {
            var para = textBoxTomlEditor.CaretPosition.Paragraph;
            if (para?.PreviousBlock == null) return;
            SwapBlockText(para, para.PreviousBlock);
            textBoxTomlEditor.CaretPosition = para.PreviousBlock.ContentStart;
            HighlightDocument();
        }

        private void MoveLineDown()
        {
            var para = textBoxTomlEditor.CaretPosition.Paragraph;
            if (para?.NextBlock == null) return;
            SwapBlockText(para, para.NextBlock);
            textBoxTomlEditor.CaretPosition = para.NextBlock.ContentStart;
            HighlightDocument();
        }

        private static void SwapBlockText(Block a, Block b)
        {
            var ra = new TextRange(a.ContentStart, a.ContentEnd);
            var rb = new TextRange(b.ContentStart, b.ContentEnd);
            (ra.Text, rb.Text) = (rb.Text, ra.Text);
        }

        private void IndentSelection(int direction)
        {
            var sel   = textBoxTomlEditor.Selection;
            var start = sel.Start.Paragraph;
            var end   = sel.End.Paragraph;
            if (start == null) return;
            for (var para = start; para != null; para = para.NextBlock as Paragraph)
            {
                var range  = new TextRange(para.ContentStart, para.ContentEnd);
                range.Text = direction > 0
                    ? "\t" + range.Text
                    : (range.Text.Length > 0 && range.Text[0] == '\t' ? range.Text[1..] : range.Text);
                if (para == end) break;
            }
            HighlightDocument();
        }

        // ── EDITOR TEXT HELPERS ───────────────────────────────────────────

        private string GetEditorText() =>
            new TextRange(textBoxTomlEditor.Document.ContentStart, textBoxTomlEditor.Document.ContentEnd).Text;

        private void SetEditorText(string text)
        {
            _suppressEditorUpdate = true;
            try
            {
                textBoxTomlEditor.Document.Blocks.Clear();
                textBoxTomlEditor.Document.PageWidth = 4000;
                // Split on both CRLF and LF; strip a single trailing empty entry from trailing newline
                var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                int count = lines.Length;
                if (count > 0 && lines[count - 1].Length == 0) count--;
                for (int i = 0; i < count; i++)
                {
                    var para = new Paragraph { Margin = ZeroMargin };
                    para.Inlines.Add(new Run(lines[i]) { Foreground = ColDefault });
                    textBoxTomlEditor.Document.Blocks.Add(para);
                }
            }
            finally { _suppressEditorUpdate = false; }
            HighlightDocument();
            UpdateLineNumbers();
        }

        // Normalize line endings and split — used throughout
        private static List<string> NormalizedLines(string text) =>
            text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();

        // ── SYNTAX HIGHLIGHTING ───────────────────────────────────────────

        private void HighlightDocument()
        {
            if (_suppressEditorUpdate) return;
            _suppressEditorUpdate = true;
            try
            {
                foreach (Block block in textBoxTomlEditor.Document.Blocks.ToList())
                {
                    if (block is not Paragraph para) continue;
                    string line = new TextRange(para.ContentStart, para.ContentEnd).Text;
                    para.Inlines.Clear();
                    HighlightLine(para, line);
                }
            }
            finally { _suppressEditorUpdate = false; }
            UpdateLineNumbers();
            UpdateCursorStatus();
        }

        private static void HighlightLine(Paragraph para, string line)
        {
            if (line.Length == 0) { para.Inlines.Add(new Run("") { Foreground = ColDefault }); return; }
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith('#')) { para.Inlines.Add(new Run(line) { Foreground = ColComment }); return; }
            if (trimmed.StartsWith('[')) { para.Inlines.Add(new Run(line) { Foreground = ColSection }); return; }
            int eq = line.IndexOf('=');
            if (eq > 0)
            {
                para.Inlines.Add(new Run(line[..eq]) { Foreground = ColKey });
                para.Inlines.Add(new Run("=")        { Foreground = ColEquals });
                AddValueRuns(para, line[(eq + 1)..]);
                return;
            }
            para.Inlines.Add(new Run(line) { Foreground = ColDefault });
        }

        private static void AddValueRuns(Paragraph para, string value)
        {
            int bo = value.IndexOf('{');
            if (bo >= 0)
            {
                if (bo > 0) para.Inlines.Add(new Run(value[..bo]) { Foreground = ColDefault });
                int bc          = value.LastIndexOf('}');
                string inner    = bc > bo ? value[(bo + 1)..bc] : value[(bo + 1)..];
                string trailing = bc > 0 && bc < value.Length - 1 ? value[(bc + 1)..] : string.Empty;
                para.Inlines.Add(new Run("{") { Foreground = ColBrace });
                var parts = inner.Split(',');
                for (int i = 0; i < parts.Length; i++)
                {
                    string p  = parts[i];
                    int    ns = p.Length - p.TrimStart().Length;
                    int    ts = p.Length - p.TrimEnd().Length;
                    if (ns > 0) para.Inlines.Add(new Run(p[..ns])            { Foreground = ColDefault });
                    if (ns < p.Length) para.Inlines.Add(new Run(p[ns..].TrimEnd()) { Foreground = ColIdent });
                    if (ts > 0) para.Inlines.Add(new Run(new string(' ', ts)) { Foreground = ColDefault });
                    if (i < parts.Length - 1) para.Inlines.Add(new Run(",")  { Foreground = ColBrace });
                }
                para.Inlines.Add(new Run("}") { Foreground = ColBrace });
                if (!string.IsNullOrEmpty(trailing)) para.Inlines.Add(new Run(trailing) { Foreground = ColDefault });
                return;
            }

            string v    = value.TrimStart();
            string lead = value[..^v.Length];
            if (lead.Length > 0) para.Inlines.Add(new Run(lead) { Foreground = ColDefault });

            if (RxHexChk.IsMatch(v))
            {
                var m = RxHex.Match(v);
                para.Inlines.Add(new Run(m.Groups[1].Value) { Foreground = ColHex });
                if (m.Groups[2].Length > 0) para.Inlines.Add(new Run(m.Groups[2].Value) { Foreground = ColDefault });
                return;
            }
            if (v.StartsWith('"'))           { para.Inlines.Add(new Run(v) { Foreground = ColString }); return; }
            if (v is "true" or "false")      { para.Inlines.Add(new Run(v) { Foreground = ColBool });   return; }
            if (RxNum.IsMatch(v))            { para.Inlines.Add(new Run(v) { Foreground = ColNumber }); return; }
            para.Inlines.Add(new Run(v) { Foreground = ColDefault });
        }

        private void textBoxTomlEditor_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressEditorUpdate || _highlightPending) return;
            if (!string.IsNullOrWhiteSpace(_currentTomlPath) && !_isDirty)
                SetDirty(true);
            _highlightPending = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                _highlightPending = false;
                HighlightDocument();
            });
        }

        private void UpdateLineNumbers()
        {
            int count = Math.Max(1, textBoxTomlEditor.Document.Blocks.Count);
            lineNumbersList.ItemsSource = Enumerable.Range(1, count);
        }

        private void editorScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var sv = (ScrollViewer)sender;
            sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta / 3.0);
            e.Handled = true;
        }

        // ── FILE OPERATIONS ───────────────────────────────────────────────

        private void textBoxCodeGenDir_TextChanged(object sender, TextChangedEventArgs e) { }

        private void textBoxCodeGenFile_TextChanged(object sender, TextChangedEventArgs e)
        {
            string path = textBoxCodeGenFile.Text?.Trim() ?? string.Empty;
            textBreadcrumb.Text = path;
            if (File.Exists(path) && path.EndsWith(".toml", StringComparison.OrdinalIgnoreCase))
                LoadTomlFile(path);
        }

        private void buttonCodeGenDirBrowse_Click(object sender, RoutedEventArgs e)
        {
            string? s = BrowseForFolder("Select project directory");
            if (s == null) return;
            textBoxCodeGenDir.Text = s;
            string? toml = Directory.GetFiles(s, "*.toml", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (toml != null) { textBoxCodeGenFile.Text = toml; LoadTomlFile(toml); }
        }

        private void buttonCodeGenFileBrowse_Click(object sender, RoutedEventArgs e)
        {
            string? s = BrowseForFile("Select TOML config file", "TOML files (*.toml)|*.toml|All files (*.*)|*.*");
            if (s == null) return;
            textBoxCodeGenFile.Text = s;
            textBoxCodeGenDir.Text  = Path.GetDirectoryName(s) ?? string.Empty;
            LoadTomlFile(s);
        }

        private void buttonScanToml_Click(object sender, RoutedEventArgs e)
        {
            string dir = textBoxCodeGenDir.Text?.Trim() ?? string.Empty;
            if (!Directory.Exists(dir)) { MessageBox.Show("Select a valid directory first.", "No Directory", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            string? toml = Directory.GetFiles(dir, "*.toml", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (toml == null) { MessageBox.Show("No .toml files found.", "Not Found", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            textBoxCodeGenFile.Text = toml;
            LoadTomlFile(toml);
        }

        private void LoadTomlFile(string path)
        {
            if (!File.Exists(path)) return;
            _currentTomlPath    = path;
            textBreadcrumb.Text = path;
            textStatusLeft.Text = Path.GetFileName(path);
            textStatusLeft.Foreground = StatusSaved;
            _isDirty = false;
            SaveLastTomlPath(path);
            try   { SetEditorText(File.ReadAllText(path)); }
            catch (Exception ex) { AppendOutput($"[Error loading TOML] {ex.Message}"); }
        }

        private void buttonReload_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_currentTomlPath) && File.Exists(_currentTomlPath))
                LoadTomlFile(_currentTomlPath);
        }

        private void buttonSave_Click(object sender, RoutedEventArgs e)
        {
            if (!Require(_currentTomlPath, "No file loaded.", "Nothing to Save")) return;
            try
            {
                File.WriteAllText(_currentTomlPath, GetEditorText());
                AppendOutput($"[Saved] {_currentTomlPath}");
                SetDirty(false);
            }
            catch (Exception ex) { MessageBox.Show($"Save failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void buttonAddSetjmp_Click(object sender, RoutedEventArgs e)
        {
            string text = GetEditorText();
            if (text.Contains("setjmp_address") || text.Contains("longjmp_address"))
            { MessageBox.Show("setjmp/longjmp entries are already present.", "Already Added", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            SetEditorText(text.TrimEnd('\n', '\r') + "\nsetjmp_address = 0x00000000\nlongjmp_address = 0x00000000\n");
            AppendOutput("[Added] setjmp_address and longjmp_address entries.");
        }

        // Shared helper: find the [functions] section bounds in a line list.
        // Returns (start, end) where start is the header index, end is exclusive end of section.
        private static (int start, int end) FindFunctionSection(List<string> lines)
        {
            int start = lines.FindIndex(l => l.Trim().Equals("[functions]", StringComparison.OrdinalIgnoreCase));
            if (start < 0) return (-1, -1);
            int end = lines.Count;
            for (int i = start + 1; i < lines.Count; i++)
            {
                string s = lines[i].Trim();
                if (s.StartsWith('[') && !s.StartsWith('#')) { end = i; break; }
            }
            return (start, end);
        }

        // Shared helper: insert addresses into [functions], creating the section if needed.
        // Returns (inserted, skipped) counts. Does NOT save or switch tabs.
        private static (int inserted, int skipped) InjectAddressesIntoFunctions(
            List<string> tomlLines, IEnumerable<string> addresses, string? comment = null)
        {
            int headerIdx = tomlLines.FindIndex(l =>
                l.Trim().Equals("[functions]", StringComparison.OrdinalIgnoreCase));
            if (headerIdx < 0)
            {
                if (tomlLines.Count > 0 && !string.IsNullOrWhiteSpace(tomlLines[^1]))
                    tomlLines.Add(string.Empty);
                tomlLines.Add("[functions]");
                headerIdx = tomlLines.Count - 1;
            }

            var (_, funcEnd) = FindFunctionSection(tomlLines);
            var existing     = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = headerIdx + 1; i < funcEnd; i++)
            {
                string s = tomlLines[i].Trim();
                if (!s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) continue;
                int eq = s.IndexOf('=');
                existing.Add(eq >= 0 ? s[..eq].Trim() : s);
            }

            int inserted = 0, insertAt = headerIdx + 1, skipped = 0;
            foreach (string addr in addresses)
            {
                if (existing.Add(addr))
                {
                    string entry = comment != null ? $"{addr} = {{}}  {comment}" : $"{addr} = {{}}";
                    tomlLines.Insert(insertAt++, entry);
                    inserted++;
                }
                else skipped++;
            }
            return (inserted, skipped);
        }

        private void buttonRemoveDupes_Click(object sender, RoutedEventArgs e)
        {
            var lines = NormalizedLines(GetEditorText());
            var (funcStart, funcEnd) = FindFunctionSection(lines);
            if (funcStart < 0) { MessageBox.Show("No [functions] section found.", "Remove Dupes", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            var seen     = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var newLines = new List<string>(lines.Count);
            int removed  = 0;

            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                if (i > funcStart && i < funcEnd && line.TrimStart().StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    int eq    = line.IndexOf('=');
                    string key = (eq >= 0 ? line[..eq].Trim() : line.Trim()).ToLowerInvariant();
                    if (!seen.Add(key)) { removed++; continue; }
                }
                newLines.Add(line);
            }

            if (removed == 0) { AppendOutput("[Remove Dupes] No duplicates found in [functions]."); return; }
            SetEditorText(string.Join("\n", newLines));
            AppendOutput($"[Remove Dupes] Removed {removed} duplicate(s) from [functions].");
        }

        private void buttonClearValues_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.ContextMenu != null)
            {
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.IsOpen          = true;
            }
        }

        private void ClearValuesMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem mi || mi.Tag is not string tag) return;
            ClearFunctionValues(
                clearName:   tag is "All" or "Name",
                clearParent: tag is "All" or "Parent",
                clearSize:   tag is "All" or "Size");
        }

        private void ClearFunctionValues(bool clearName, bool clearParent, bool clearSize)
        {
            var lines = NormalizedLines(GetEditorText());
            var (funcStart, funcEnd) = FindFunctionSection(lines);
            if (funcStart < 0) { MessageBox.Show("No [functions] section found.", "Clear Values", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            var newLines = new List<string>(lines.Count);
            int cleared  = 0;

            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                if (i > funcStart && i < funcEnd && line.TrimStart().StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    int eq = line.IndexOf('=');
                    if (eq >= 0)
                    {
                        string addr   = line[..eq].Trim();
                        string valStr = line[(eq + 1)..].Trim();
                        if (clearName && clearParent && clearSize) { newLines.Add($"{addr} = {{}}"); cleared++; continue; }
                        var inner   = ParseBraceFields(valStr);
                        bool changed = (clearName   && inner.Remove("name"))
                                     | (clearParent && inner.Remove("parent"))
                                     | (clearSize   && inner.Remove("size"));
                        if (changed) { newLines.Add($"{addr} = {RebuildBraceFields(inner)}"); cleared++; continue; }
                    }
                }
                newLines.Add(line);
            }

            if (cleared == 0) { AppendOutput("[Clear Values] No matching fields to clear."); return; }
            SetEditorText(string.Join("\n", newLines));

            var parts = new List<string>(3);
            if (clearName)   parts.Add("name");
            if (clearParent) parts.Add("parent");
            if (clearSize)   parts.Add("size");
            AppendOutput($"[Clear Values] Cleared {string.Join("/", parts)} from {cleared} function(s).");
        }

        private static Dictionary<string, string> ParseBraceFields(string val)
        {
            var result  = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string t    = val.Trim().TrimStart('{').TrimEnd('}').Trim();
            if (string.IsNullOrWhiteSpace(t)) return result;
            foreach (string pair in t.Split(','))
            {
                var kv = pair.Split('=', 2, StringSplitOptions.TrimEntries);
                if      (kv.Length == 2)                                      result[kv[0]] = kv[1];
                else if (kv.Length == 1 && !string.IsNullOrWhiteSpace(kv[0])) result[kv[0]] = "";
            }
            return result;
        }

        private static string RebuildBraceFields(Dictionary<string, string> fields)
        {
            if (fields.Count == 0) return "{}";
            return "{ " + string.Join(", ", fields.Select(kv => string.IsNullOrEmpty(kv.Value) ? kv.Key : $"{kv.Key} = {kv.Value}")) + " }";
        }

        private void buttonSaveBackup_Click(object sender, RoutedEventArgs e)
        {
            if (!Require(_currentTomlPath, "No file loaded.", "Nothing to Back Up")) return;
            string backup = $"{_currentTomlPath}.bak_{DateTime.Now:yyyyMMdd_HHmmss}";
            try   { File.WriteAllText(backup, GetEditorText()); AppendOutput($"[Backup Saved] {backup}"); }
            catch (Exception ex) { MessageBox.Show($"Backup failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void buttonLoadBackup_Click(object sender, RoutedEventArgs e)
        {
            string? s = BrowseForFile("Load backup TOML", "TOML backup (*.bak;*.toml)|*.bak;*.toml|All files|*.*");
            if (s != null) LoadTomlFile(s);
        }

        private async void buttonRunCodeGen_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_currentTomlPath) && File.Exists(_currentTomlPath))
                buttonSave_Click(sender, e);

            if (!Require(GetEnv(REXSDK_ENV), "REXSDK is not set. Run SDK Setup first.", "Not Configured")) return;
            if (string.IsNullOrWhiteSpace(_currentTomlPath) || !File.Exists(_currentTomlPath))
            { MessageBox.Show("No TOML config file loaded. Load a file in the Code Gen tab first.", "No Config", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            string dir  = Path.GetDirectoryName(_currentTomlPath) ?? string.Empty;
            string args = $"codegen \"{_currentTomlPath}\"";

            var codeGenBtn = (Button)sender;
            codeGenBtn.IsEnabled   = false;
            tabBtnOutput.IsChecked = true;
            tabBtnOutput.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ToggleButton.CheckedEvent));
            AppendOutput($"[Run Code Generation]\n  Config:  {_currentTomlPath}\n  Command: rexglue {args}");

            // Collect raw output lines so we can parse errors after exit
            var outputLines = new List<string>();

            Process? proc = null;
            try
            {
                proc = RunRexglue(dir, args, extraLine => { lock (outputLines) outputLines.Add(extraLine); });
                if (proc == null) { ShowProcessError("Code Generation"); return; }

                await Task.Run(() => proc.WaitForExit());
                bool ok = proc.ExitCode == 0;
                AppendOutput($"[Run Code Generation] Done. ExitCode: {proc.ExitCode}", ok ? OutOk : OutErr);

                if (!ok)
                    TryInjectUnresolvedCalls(outputLines);
            }
            catch (Exception ex)
            {
                AppendOutput($"[Run Code Generation Error] {ex.Message}");
                MessageBox.Show($"Code generation failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { codeGenBtn.IsEnabled = true; }
        }

        // Parse UnresolvedCall addresses from output, inject into TOML [functions], save, switch tab.
        private void TryInjectUnresolvedCalls(List<string> lines)
        {
            // Matches both:
            //   "  0x82171220 from 0x82171F54: ..."          (UnresolvedCall)
            //   "  Unresolved conditional branch to 0x822F7918 from 0x822F7928"
            var addrRegex = new Regex(@"(?:to\s+)?(0x[0-9a-fA-F]+)\s+from\s+0x[0-9a-fA-F]+", RegexOptions.IgnoreCase);
            var seen      = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var newAddrs  = new List<string>();

            lock (lines)
            {
                foreach (string line in lines)
                {
                    var m = addrRegex.Match(line);
                    if (m.Success && seen.Add(m.Groups[1].Value.ToLowerInvariant()))
                        newAddrs.Add(m.Groups[1].Value);
                }
            }

            if (newAddrs.Count == 0) return;

            var tomlLines     = NormalizedLines(GetEditorText());
            var (ins, skipped) = InjectAddressesIntoFunctions(tomlLines, newAddrs);

            if (ins == 0)
            { AppendOutput($"[Code Gen] {newAddrs.Count} unresolved address(es) already present in [functions].", OutWarn); return; }

            SetEditorText(string.Join("\n", tomlLines));
            buttonSave_Click(this, new RoutedEventArgs());
            string msg = $"[Code Gen] Injected {ins} unresolved address(es) into [functions].";
            if (skipped > 0) msg += $" ({skipped} already present.)";
            AppendOutput(msg, OutWarn);
        }

        // ── TEMPLATE CHIPS ────────────────────────────────────────────────

        private void BuildStarterChips()
        {
            panelStarterTemplates.Children.Clear();
            foreach (var (label, value) in StarterTemplates)
                panelStarterTemplates.Children.Add(MakeChip(label, value, isCustom: false));
        }

        private UIElement MakeChip(string label, string value, bool isCustom)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 6, 4) };
            var btn   = new Button { Content = label, Style = (Style)FindResource("ChipBtn"), Tag = value };
            btn.Click += (_, _) => ApplyTemplate(value);
            var close = new Button { Content = "×", Style = (Style)FindResource("ChipClose"), Tag = value };
            close.Click += (_, _) => (isCustom ? panelCustomTemplates : panelStarterTemplates).Children.Remove(panel);
            panel.Children.Add(btn);
            panel.Children.Add(close);
            return panel;
        }

        private void ApplyTemplate(string pattern)
        {
            // Keep the leading "= " so output is "0xADDR = {}" not "0xADDR {}"
            if (!pattern.TrimStart().StartsWith('='))
                pattern = "= " + pattern.TrimStart();
            var paras    = textBoxTomlEditor.Document.Blocks.OfType<Paragraph>().ToList();
            if (paras.Count == 0) return;

            var selStart = textBoxTomlEditor.Selection.Start;
            var selEnd   = textBoxTomlEditor.Selection.End;
            int fromIdx  = paras.FindIndex(p => p.ContentStart.CompareTo(selStart) <= 0 && p.ContentEnd.CompareTo(selStart) >= 0);
            int toIdx    = paras.FindIndex(p => p.ContentStart.CompareTo(selEnd)   <= 0 && p.ContentEnd.CompareTo(selEnd)   >= 0);
            if (fromIdx < 0) fromIdx = 0;
            if (toIdx   < 0) toIdx   = paras.Count - 1;

            int changed = 0;
            _suppressEditorUpdate = true;
            try
            {
                for (int i = fromIdx; i <= toIdx; i++)
                {
                    var range   = new TextRange(paras[i].ContentStart, paras[i].ContentEnd);
                    string line = range.Text.Trim();
                    int eq      = line.IndexOf('=');
                    string key  = eq >= 0 ? line[..eq].Trim() : line;
                    string addr = key.Replace("0x", "").Replace("0X", "");
                    range.Text  = string.IsNullOrWhiteSpace(range.Text)
                        ? pattern.Replace("%ADDR%", addr)
                        : key + " " + pattern.Replace("%ADDR%", addr);
                    changed++;
                }
            }
            finally { _suppressEditorUpdate = false; }

            HighlightDocument();
            UpdateLineNumbers();
            AppendOutput(changed > 0
                ? $"[Template] Applied to {changed} line(s)."
                : "[Template] No lines to apply. Select lines in the editor first.");
        }

        private void buttonResetTemplates_Click(object sender, RoutedEventArgs e)
        {
            BuildStarterChips();
            panelCustomTemplates.Children.Clear();
            AppendOutput("[Templates] Reset to defaults.");
        }

        private void btnToggleStarter_Click(object sender, RoutedEventArgs e)
            => TogglePanel(panelStarterTemplates, btnToggleStarter, "Starter Templates");

        private void btnToggleCustom_Click(object sender, RoutedEventArgs e)
            => TogglePanel(panelCustomTemplates, btnToggleCustom, "Custom Templates");

        private static void TogglePanel(UIElement panel, Button btn, string label)
        {
            bool show  = panel.Visibility != Visibility.Visible;
            panel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            btn.Content      = (show ? "▾ " : "▸ ") + label;
        }

        private void buttonAddCustomTemplate_Click(object sender, RoutedEventArgs e)
        {
            string? result = PromptInput("New Template", "Enter template value (e.g. = { name, size }):");
            if (!string.IsNullOrWhiteSpace(result))
                panelCustomTemplates.Children.Add(MakeChip(result, result, isCustom: true));
        }

        // ── ADDRESS PARSER TAB ────────────────────────────────────────────

        private void buttonAddrPaste_Click(object sender, RoutedEventArgs e)
        {
            try { if (Clipboard.ContainsText()) textBoxAddress.Text = Clipboard.GetText(); } catch { }
        }

        private void buttonAddrCopy_Click(object sender, RoutedEventArgs e)
        {
            string t = new TextRange(textBoxAddressResult.Document.ContentStart,
                                     textBoxAddressResult.Document.ContentEnd).Text.Trim();
            if (string.IsNullOrEmpty(t)) { MessageBox.Show("Nothing to copy.", "Address Parser", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            try   { Clipboard.SetText(t); AppendOutput("[Address Parser] Output copied to clipboard."); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Copy failed", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void buttonParseAddress_Click(object sender, RoutedEventArgs e)
        {
            string text = textBoxAddress.Text ?? "";
            if (string.IsNullOrWhiteSpace(text)) { MessageBox.Show("Input is empty.", "Address Parser", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            // ── Parse unique addresses from input ─────────────────────────
            var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var parsed = new List<string>();

            foreach (string rawLine in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int fromIdx = rawLine.IndexOf(" from ", StringComparison.OrdinalIgnoreCase);
                if (fromIdx < 0) continue;
                var tokens = rawLine[..fromIdx].Replace("[", " ").Replace("]", " ")
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 0) continue;
                string addr = tokens[^1].Trim();
                if (addr.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && addr.Length > 2
                    && seen.Add(addr.ToLowerInvariant()))
                    parsed.Add(addr);
            }

            textBoxAddressResult.Document.Blocks.Clear();

            if (parsed.Count == 0)
            { MessageBox.Show("No addresses found.", "Address Parser", MessageBoxButton.OK, MessageBoxImage.Information); return; }

            // ── Inject into TOML (shared helper handles dedup + section creation) ──
            var tomlLines         = NormalizedLines(GetEditorText());
            var (ins, skipped)    = InjectAddressesIntoFunctions(tomlLines, parsed);

            // Rebuild existing set for the result renderer (after injection)
            var (funcStart2, funcEnd2) = FindFunctionSection(tomlLines);
            var existingAfter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = funcStart2 + 1; i < funcEnd2; i++)
            {
                string s = tomlLines[i].Trim();
                if (!s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) continue;
                int eq = s.IndexOf('=');
                existingAfter.Add(eq >= 0 ? s[..eq].Trim() : s);
            }

            if (ins > 0)
            {
                SetEditorText(string.Join("\n", tomlLines));
                if (!string.IsNullOrWhiteSpace(_currentTomlPath) && File.Exists(_currentTomlPath))
                {
                    buttonSave_Click(this, new RoutedEventArgs());
                    AppendOutput($"[Address Parser] Inserted {ins} address(es) into [functions] and saved.", OutOk);
                }
                else
                {
                    AppendOutput($"[Address Parser] Inserted {ins} address(es) into [functions] (no file loaded — not saved).", OutWarn);
                }
                if (skipped > 0)
                    AppendOutput($"[Address Parser] Skipped {skipped} duplicate(s) already in [functions].", OutWarn);
            }
            else
            {
                AppendOutput($"[Address Parser] All {parsed.Count} address(es) already exist in [functions] — nothing added.", OutWarn);
            }

            // ── Render colored results ────────────────────────────────────
            foreach (string addr in parsed)
            {
                bool isDupe = !existingAfter.Contains(addr) && skipped > 0;
                var para = new Paragraph { Margin = ZeroMargin, FontFamily = MonoFont, FontSize = 12 };
                para.Inlines.Add(new Run(addr + " ")    { Foreground = isDupe ? OutWarn : OutInfo });
                para.Inlines.Add(new Run("= ")          { Foreground = OutDefault });
                para.Inlines.Add(new Run("{}")          { Foreground = isDupe ? OutWarn : ColBrace });
                if (isDupe)
                    para.Inlines.Add(new Run("  // duplicate") { Foreground = OutDim });
                textBoxAddressResult.Document.Blocks.Add(para);
            }
        }

        // ── OUTPUT LOG ────────────────────────────────────────────────────

        // Colour palette matching image 2
        private static readonly SolidColorBrush OutOk      = new(Color.FromRgb(0x4E, 0xC9, 0x4E)); // green   [ OK ]
        private static readonly SolidColorBrush OutInfo    = new(Color.FromRgb(0x4F, 0xC1, 0xFF)); // cyan    [INFO]
        private static readonly SolidColorBrush OutWarn    = new(Color.FromRgb(0xFF, 0xCC, 0x00)); // yellow  [warning]
        private static readonly SolidColorBrush OutErr     = new(Color.FromRgb(0xF4, 0x47, 0x47)); // red     [ERR]
        private static readonly SolidColorBrush OutDefault = new(Color.FromRgb(0xCC, 0xCC, 0xCC)); // grey    plain
        private static readonly SolidColorBrush OutDim     = new(Color.FromRgb(0x77, 0x77, 0x77)); // dimgrey indented

        private static readonly FontFamily      MonoFont   = new("Consolas");
        private static readonly Thickness       ZeroMargin = new(0);

        // Prefixes that get a [HH:mm:ss] timestamp prepended
        private static readonly string[] InternalPrefixes =
        {
            "[Run Code", "[Initialize", "[Saved]", "[Backup",
            "[Added]",   "[Functions",  "[Template", "[Remove",
            "[Clear",    "[Replace",    "[Address",  "[Output",
            "[Error",    "[SDK",        "[Templates","[setjmp]",
            "[VS Debugger]", "SDK configured"
        };

        private void buttonOutputCopyAll_Click(object sender, RoutedEventArgs e)
        {
            string t = GetOutputText().Trim();
            if (string.IsNullOrEmpty(t)) { AppendOutput("[Output] Tab is empty."); return; }
            try   { Clipboard.SetText(t); AppendOutput("[ OK ]  Output copied to clipboard."); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Copy failed", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void buttonOutputSendToParser_Click(object sender, RoutedEventArgs e)
        {
            string t = GetOutputText().Trim();
            if (string.IsNullOrEmpty(t)) { AppendOutput("[Output] Tab is empty."); return; }
            textBoxAddress.Text        = t;
            tabBtnAddrParser.IsChecked = true;
        }
        private void buttonOutputClear_Click(object sender, RoutedEventArgs e) =>
            textBoxOutput.Document.Blocks.Clear();

        private string GetOutputText() =>
            new TextRange(textBoxOutput.Document.ContentStart, textBoxOutput.Document.ContentEnd).Text;

        // Classify a line and return its brush
        private static SolidColorBrush ClassifyLine(string line)
        {
            string t = line.TrimStart();
            if (t.StartsWith("[ OK ]",   StringComparison.OrdinalIgnoreCase) ||
                t.StartsWith("[OK]",     StringComparison.OrdinalIgnoreCase))  return OutOk;
            if (t.StartsWith("[INFO]",   StringComparison.OrdinalIgnoreCase))  return OutInfo;
            if (t.StartsWith("[warning]",StringComparison.OrdinalIgnoreCase))  return OutWarn;
            if (t.StartsWith("[ERR]",    StringComparison.OrdinalIgnoreCase) ||
                t.StartsWith("[error]",  StringComparison.OrdinalIgnoreCase))  return OutErr;
            if (t.StartsWith("[info]",   StringComparison.OrdinalIgnoreCase))  return OutInfo;
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))       return OutDim;
            return OutDefault;
        }

        private Paragraph MakeOutputPara(string line, SolidColorBrush? color = null) =>
            new(new Run(line))
            {
                Margin     = ZeroMargin,
                Foreground = color ?? ClassifyLine(line),
                FontFamily = MonoFont,
                FontSize   = 12
            };

        // Append one or more lines with per-line colouring.
        // Pass an explicit color to override classification.
        private void AppendOutput(string message, SolidColorBrush? color = null)
        {
            foreach (string raw in message.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw;
                if (Array.Exists(InternalPrefixes, p => line.StartsWith(p, StringComparison.Ordinal)))
                    line = $"[{DateTime.Now:HH:mm:ss}] {line}";
                textBoxOutput.Document.Blocks.Add(MakeOutputPara(line, color));
            }
            textBoxOutput.ScrollToEnd();
        }

        // Called from RunRexglue handlers — raw process lines, no timestamp
        private void AppendRawLine(string line)
        {
            textBoxOutput.Document.Blocks.Add(MakeOutputPara(line));
            textBoxOutput.ScrollToEnd();
        }

        // ── VS DEBUGGER — ctx.ctr.u32 ─────────────────────────────────────
        // Scans the Windows ROT for a VS instance at a breakpoint, evaluates
        // the configured expression, and injects non-zero u32s into [functions].
        //
        // NuGet required: EnvDTE (17.x)
        // .csproj required: <PlatformTarget>x86</PlatformTarget>

        // P/Invoke: use IntPtr to avoid the "Argument 3 may not be passed with 'out'" error
        // that occurs when passing COM interface types directly as out parameters.
        [DllImport("ole32.dll")]
        private static extern int GetRunningObjectTable(uint reserved, out IntPtr pprot);

        [DllImport("ole32.dll")]
        private static extern int CreateBindCtx(uint reserved, out IntPtr ppbc);

        // ── State ─────────────────────────────────────────────────────────
        private DispatcherTimer?                          _vsWatchTimer   = null;
        private System.Threading.CancellationTokenSource? _vsCts          = null;
        private bool   _vsPollingActive = false;
        private string _lastVsSnapshot  = string.Empty;
        private string _vsExpression    = "ctx.ctr.u32";
        private const  int VsArrayLimit = 64;

        // Cached references to VS-debugger UI controls (set on first use)
        private TextBlock?  _tbVsStatus   = null;
        private Ellipse?    _elVsDot      = null;
        private TextBox?    _tbVsInterval = null;
        private System.Windows.Controls.Primitives.ToggleButton? _toggleVsPoll = null;

        private static readonly Regex RxVsScalar =
            new(@"^(0x[0-9a-fA-F]+|\d+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // ── XAML handlers ─────────────────────────────────────────────────
        private void textBoxVsExpression_TextChanged(object sender, TextChangedEventArgs e)
            => _vsExpression = ((TextBox)sender).Text.Trim();

        public async void VsDebugger_FetchOnce(object sender, RoutedEventArgs e)
        {
            SetVsStatus("Connecting to Visual Studio…", OutWarn, active: false);
            try
            {
                var entries = await Task.Run(EvaluateCtxCtrInVs);
                if (entries != null)
                {
                    AppendOutput("[VS Debugger] Stopped debug session in Visual Studio.", OutInfo);
                    ProcessCtrEntries(entries, autoSave: true);
                }
            }
            catch (Exception ex) { SetVsStatus($"Error: {ex.Message}", OutErr, active: false); }
        }

        public void VsDebugger_StartPolling(object sender, RoutedEventArgs e)
        {
            if (_vsPollingActive) return;
            _vsPollingActive = true;
            _vsCts           = new System.Threading.CancellationTokenSource();
            int interval     = GetVsPollInterval();

            _vsWatchTimer       = new DispatcherTimer { Interval = TimeSpan.FromSeconds(interval) };
            _vsWatchTimer.Tick += async (_, _) =>
            {
                if (_vsCts?.IsCancellationRequested == true) { StopVsPollingInternal(); return; }
                try
                {
                    var entries = await Task.Run(EvaluateCtxCtrInVs);
                    if (entries != null) ProcessCtrEntries(entries, autoSave: true);
                }
                catch { /* swallow poll tick errors */ }
            };
            _vsWatchTimer.Start();
            SetVsStatus($"Polling every {interval}s…", OutInfo, active: true);
            AppendOutput($"[VS Debugger] Auto-polling started ({interval}s interval).", OutInfo);
        }

        public void VsDebugger_StopPolling(object sender, RoutedEventArgs e)
            => StopVsPollingInternal();

        private void StopVsPollingInternal()
        {
            _vsWatchTimer?.Stop();
            _vsWatchTimer    = null;
            _vsCts?.Cancel();
            _vsPollingActive = false;
            SetVsStatus("Polling stopped.", OutWarn, active: false);
            _toggleVsPoll ??= FindName("toggleVsPoll") as System.Windows.Controls.Primitives.ToggleButton;
            if (_toggleVsPoll?.IsChecked == true)
                _toggleVsPoll.IsChecked = false;
        }

        // ── Core: connect → evaluate → parse ─────────────────────────────
        private List<(int Index, uint Value)>? EvaluateCtxCtrInVs()
        {
            // Get ROT and bind context via IntPtr then cast to COM interfaces
            if (GetRunningObjectTable(0, out IntPtr rotPtr) != 0 || rotPtr == IntPtr.Zero)
                return null;
            if (CreateBindCtx(0, out IntPtr bcPtr) != 0 || bcPtr == IntPtr.Zero)
                return null;

            var rot = (IRunningObjectTable)Marshal.GetObjectForIUnknown(rotPtr);
            var bc  = (IBindCtx)Marshal.GetObjectForIUnknown(bcPtr);
            Marshal.Release(rotPtr);
            Marshal.Release(bcPtr);

            rot.EnumRunning(out IEnumMoniker enumMoniker);
            var monikers = new IMoniker[1];

            DTE2? dte = null;
            while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0)
            {
                try
                {
                    monikers[0].GetDisplayName(bc, null, out string name);
                    if (!name.StartsWith("!VisualStudio.DTE", StringComparison.OrdinalIgnoreCase))
                        continue;

                    rot.GetObject(monikers[0], out object obj);
                    if (obj is DTE2 d && d.Debugger?.CurrentMode == dbgDebugMode.dbgBreakMode)
                    {
                        dte = d;
                        break;
                    }
                    if (obj != null) Marshal.ReleaseComObject(obj);
                }
                catch { /* stale ROT entry */ }
            }

            if (dte == null)
            {
                Dispatcher.Invoke(() => SetVsStatus(
                    "No VS instance paused at a breakpoint.", OutWarn, active: false));
                return null;
            }

            try
            {
                var dbg    = (Debugger2)dte.Debugger;
                string expr = _vsExpression.Trim();

                var exprObj = dbg.GetExpression(expr, UseAutoExpandRules: true, Timeout: 2000);
                if (!exprObj.IsValidValue)
                {
                    Dispatcher.Invoke(() => SetVsStatus(
                        $"Expression invalid: {exprObj.Value}", OutErr, active: false));
                    return null;
                }

                var result = ParseVsValue(exprObj.Value ?? string.Empty, dbg, expr);
                Dispatcher.Invoke(() => SetVsStatus(
                    $"Got {result.Count} value(s) from VS.", OutOk, active: _vsPollingActive));

                // Stop the debug session now that we have what we need
                try { dbg.TerminateAll(); }
                catch { /* non-fatal — debuggee may have already exited */ }

                return result;
            }
            finally { Marshal.ReleaseComObject(dte); }
        }

        private List<(int Index, uint Value)> ParseVsValue(
            string raw, Debugger2 dbg, string baseExpr)
        {
            var result = new List<(int, uint)>();

            // Scalar: "0x0000001C"
            if (RxVsScalar.IsMatch(raw.Trim()))
            {
                if (TryParseVsU32(raw.Trim(), out uint v)) result.Add((0, v));
                return result;
            }

            // Inline array: "{0x00000001, 0x00000002, ...}"
            if (raw.Contains('{'))
            {
                string inner = raw.Trim().TrimStart('{').TrimEnd('}');
                int idx = 0;
                foreach (string tok in inner.Split(','))
                {
                    if (TryParseVsU32(tok.Trim(), out uint v)) result.Add((idx, v));
                    idx++;
                }
                if (result.Count > 0) return result;
            }

            // Expand element-by-element: baseExpr[0], [1], …
            for (int i = 0; i < VsArrayLimit; i++)
            {
                try
                {
                    var el = dbg.GetExpression($"{baseExpr}[{i}]",
                                               UseAutoExpandRules: false, Timeout: 500);
                    if (!el.IsValidValue) break;
                    if (TryParseVsU32(el.Value?.Trim() ?? string.Empty, out uint v))
                        result.Add((i, v));
                }
                catch { break; }
            }
            return result;
        }

        // ── Inject into TOML ──────────────────────────────────────────────
        private void ProcessCtrEntries(List<(int Index, uint Value)> entries, bool autoSave)
        {
            if (entries.Count == 0)
            { AppendOutput("[VS Debugger] No counter values returned.", OutWarn); return; }

            string snapshot = string.Join(",", entries.Select(e => $"{e.Index}:{e.Value:X8}"));
            if (snapshot == _lastVsSnapshot)
            { SetVsStatus("Values unchanged — skipping re-injection.", OutDim, active: _vsPollingActive); return; }
            _lastVsSnapshot = snapshot;

            var candidates = entries.Where(e => e.Value != 0).ToList();
            if (candidates.Count == 0)
            { AppendOutput("[VS Debugger] All counter values are 0x00000000 — nothing to inject.", OutWarn); return; }

            // Build address list with per-entry comments
            var tomlLines = NormalizedLines(GetEditorText());
            int headerIdx = tomlLines.FindIndex(l =>
                l.Trim().Equals("[functions]", StringComparison.OrdinalIgnoreCase));
            if (headerIdx < 0)
            {
                if (tomlLines.Count > 0 && !string.IsNullOrWhiteSpace(tomlLines[^1]))
                    tomlLines.Add(string.Empty);
                tomlLines.Add("[functions]");
                headerIdx = tomlLines.Count - 1;
            }

            // Inject each candidate with its index comment
            var (_, funcEnd) = FindFunctionSection(tomlLines);
            var existing     = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = headerIdx + 1; i < funcEnd; i++)
            {
                string s = tomlLines[i].Trim();
                if (!s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) continue;
                int eq = s.IndexOf('=');
                existing.Add(eq >= 0 ? s[..eq].Trim() : s);
            }

            int inserted = 0, insertAt = headerIdx + 1;
            foreach (var (idx, val) in candidates)
            {
                string hex = $"0x{val:X8}";
                if (existing.Add(hex.ToLowerInvariant()))
                {
                    tomlLines.Insert(insertAt++, $"{hex} = {{}}  # ctx.ctr.u32[{idx}]");
                    inserted++;
                }
            }
            int skipped = candidates.Count - inserted;

            SetEditorText(string.Join("\n", tomlLines));

            if (autoSave && !string.IsNullOrWhiteSpace(_currentTomlPath) && File.Exists(_currentTomlPath))
                buttonSave_Click(this, new RoutedEventArgs());

            string msg = $"[VS Debugger] Injected {inserted} address(es) into [functions].";
            if (skipped > 0) msg += $" ({skipped} already present.)";
            AppendOutput(msg, inserted > 0 ? OutOk : OutWarn);

            tabBtnCodeGen.IsChecked = true;
            tabBtnCodeGen.RaiseEvent(new RoutedEventArgs(
                System.Windows.Controls.Primitives.ToggleButton.CheckedEvent));
        }

        // ── Helpers ───────────────────────────────────────────────────────
        private static bool TryParseVsU32(string s, out uint value)
        {
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return uint.TryParse(s[2..], System.Globalization.NumberStyles.HexNumber,
                                     null, out value);
            return uint.TryParse(s, out value);
        }

        private int GetVsPollInterval()
        {
            _tbVsInterval ??= FindName("textBoxVsPollInterval") as TextBox;
            return (_tbVsInterval != null && int.TryParse(_tbVsInterval.Text, out int v) && v >= 1) ? v : 3;
        }

        private void SetVsStatus(string message, SolidColorBrush color, bool active)
        {
            _tbVsStatus ??= FindName("textBlockVsStatus") as TextBlock;
            _elVsDot    ??= FindName("ellipseVsDot")      as Ellipse;
            if (_tbVsStatus != null) { _tbVsStatus.Text = message; _tbVsStatus.Foreground = color; }
            if (_elVsDot    != null)
                _elVsDot.Fill = (active || color == OutOk)
                    ? new SolidColorBrush(Color.FromRgb(0x4E, 0xC9, 0x4E))
                    : new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
        }
    }
}
