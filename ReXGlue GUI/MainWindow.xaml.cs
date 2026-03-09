using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

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
            ("= {}", "= {}"),
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
        }

        //
        //  UTILITY
        //

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

        private static string? PromptInput(string title, string prompt)
        {
            var win = new Window
            {
                Title = title,
                MinWidth = 380,
                Width = 420,
                MinHeight = 200,
                Height = 220,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x24)),
                ResizeMode = ResizeMode.CanResize
            };
            var stack = new StackPanel { Margin = new Thickness(20) };
            stack.Children.Add(new TextBlock
            {
                Text = prompt,
                Foreground = Brushes.LightGray,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            });
            var tb = new TextBox
            {
                Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)),
                Foreground = Brushes.LightGray,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Height = 28,
                Padding = new Thickness(6, 0, 6, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 16)
            };
            stack.Children.Add(tb);
            var btnOk = new Button
            {
                Content = "OK",
                Width = 80,
                Height = 28,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x2E)),
                Foreground = Brushes.LightGray,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44))
            };
            btnOk.Click += (_, _) => win.DialogResult = true;
            stack.Children.Add(btnOk);
            win.Content = stack;
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

        private bool TrySetEnvWithConfirmation(string name, string value)
        {
            string existing = Environment.GetEnvironmentVariable(name, EnvTarget) ?? string.Empty;
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
                string current = Environment.GetEnvironmentVariable("Path", EnvTarget) ?? string.Empty;
                string norm    = NormalizePath(releasePath);
                var entries    = current.Split(';', StringSplitOptions.RemoveEmptyEntries)
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

        //
        //  TAB NAVIGATION
        //

        private void TabBtn_Checked(object sender, RoutedEventArgs e)
        {
            if (panelNewProject == null) return;
            if (sender is not RadioButton rb) return;
            panelNewProject.Visibility = Visibility.Collapsed;
            panelCodeGen.Visibility    = Visibility.Collapsed;
            panelAddrParser.Visibility = Visibility.Collapsed;
            panelOutput.Visibility     = Visibility.Collapsed;
            switch (rb.Tag?.ToString())
            {
                case "NewProject": panelNewProject.Visibility = Visibility.Visible; break;
                case "CodeGen":    panelCodeGen.Visibility    = Visibility.Visible; break;
                case "AddrParser": panelAddrParser.Visibility = Visibility.Visible; break;
                case "Output":     panelOutput.Visibility     = Visibility.Visible; break;
            }
        }

        private void buttonTheme_Click(object sender, RoutedEventArgs e)
            => MessageBox.Show("Theme toggle coming soon.", "Theme", MessageBoxButton.OK, MessageBoxImage.Information);

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape || e.Key == Key.Enter || e.Key == Key.Space) return;
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
            if (e.Key >= Key.A && e.Key <= Key.Z && !e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Shift))
                return (char)('a' + (e.Key - Key.A));
            if (e.Key >= Key.A && e.Key <= Key.Z && e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Shift))
                return (char)('A' + (e.Key - Key.A));
            return '\0';
        }

        private void ShowCreditPopup()
        {
            MessageBox.Show(
                "     ReXGlue GUI\n     Made by MaxDeadBear\n     Vodka Doc\n",
                "♥",
                MessageBoxButton.OK,
                MessageBoxImage.None
            );
        }

        private void buttonRefresh_Click(object sender, RoutedEventArgs e)
        {
            CheckAndConfigureSdk();
            PopulateNewProjectRoot();
        }

        //
        //  SDK SETUP OVERLAY
        //

        private void CheckAndConfigureSdk()
        {
            RefreshStatusIndicators();
            string rexsdk  = Environment.GetEnvironmentVariable(REXSDK_ENV,  EnvTarget) ?? string.Empty;
            string baseSdk = Environment.GetEnvironmentVariable(BASESDK_ENV, EnvTarget) ?? string.Empty;
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
            string rexsdk   = Environment.GetEnvironmentVariable(REXSDK_ENV,  EnvTarget) ?? string.Empty;
            string baseSdk  = Environment.GetEnvironmentVariable(BASESDK_ENV, EnvTarget) ?? string.Empty;
            string userPath = Environment.GetEnvironmentVariable("Path",       EnvTarget) ?? string.Empty;
            SetStatus(statusRexsdk,  rexsdk,  Directory.Exists(rexsdk));
            SetStatus(statusBaseSdk, baseSdk, Directory.Exists(baseSdk));
            bool pathOk = userPath.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Any(p => p.IndexOf("rexglue-sdk", StringComparison.OrdinalIgnoreCase) >= 0
                       && p.IndexOf("Release",     StringComparison.OrdinalIgnoreCase) >= 0);
            statusPathEntry.Text  = pathOk ? "● Set" : "● Not set";
            statusPathEntry.Style = (Style)FindResource(pathOk ? "StatusOk" : "StatusWarn");
        }

        private void SetStatus(TextBlock tb, string value, bool exists)
        {
            if (string.IsNullOrWhiteSpace(value)) { tb.Text = "● Not set";      tb.Style = (Style)FindResource("StatusWarn"); }
            else if (!exists)                     { tb.Text = "● Invalid path"; tb.Style = (Style)FindResource("StatusWarn"); }
            else                                  { tb.Text = $"● {value}";     tb.Style = (Style)FindResource("StatusOk"); }
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

        private void buttonSetupApply_Click(object sender, RoutedEventArgs e)
        {
            string baseFolder = NormalizePath(textBoxSetupBaseFolder.Text);
            if (string.IsNullOrWhiteSpace(baseFolder))
            { MessageBox.Show("Please select a base folder.", "Missing Folder", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            string sdkRoot = Path.Combine(baseFolder, "rexglue-sdk");
            if (!Directory.Exists(sdkRoot))
            {
                var result = MessageBox.Show(
                    $"'rexglue-sdk' not found inside:\n{baseFolder}\n\n" +
                    "Would you like to clone the SDK from GitHub into this folder now?\n\n" +
                    "Command:\n  git clone https://github.com/rexglue/rexglue-sdk",
                    "Clone SDK from GitHub?",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes) return;

                try
                {
                    Directory.CreateDirectory(baseFolder);
                    var psi = new ProcessStartInfo
                    {
                        FileName               = "git",
                        Arguments              = "clone https://github.com/rexglue/rexglue-sdk",
                        WorkingDirectory       = baseFolder,
                        UseShellExecute        = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError  = true,
                        CreateNoWindow         = true
                    };

                    var proc = Process.Start(psi);
                    if (proc == null)
                    {
                        MessageBox.Show("Failed to start 'git' process. Ensure Git is installed and available in PATH.",
                            "Git Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    string stdout = proc.StandardOutput.ReadToEnd();
                    string stderr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit();

                    AppendOutput($"[SDK Setup] git clone exit code {proc.ExitCode}\n{stdout}" +
                                 (string.IsNullOrWhiteSpace(stderr) ? string.Empty : "\n[stderr]\n" + stderr));

                    if (proc.ExitCode != 0 || !Directory.Exists(sdkRoot))
                    {
                        MessageBox.Show("Cloning 'rexglue-sdk' failed. Check the Output tab for details.",
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

        //
        //  NEW PROJECT TAB
        //

        private void PopulateNewProjectRoot()
        {
            try
            {
                string baseSdk = Environment.GetEnvironmentVariable(BASESDK_ENV, EnvTarget) ?? string.Empty;
                string root    = string.Empty;
                if (!string.IsNullOrWhiteSpace(baseSdk) && Directory.Exists(baseSdk))
                    root = Path.Combine(baseSdk, "rexglue-sdk");
                else
                {
                    string rexsdk   = Environment.GetEnvironmentVariable(REXSDK_ENV, EnvTarget) ?? string.Empty;
                    string? derived = DeriveBaseFromRexsdk(rexsdk);
                    if (!string.IsNullOrWhiteSpace(derived) && Directory.Exists(derived))
                        root = Path.Combine(derived, "rexglue-sdk");
                }
                if (!string.IsNullOrWhiteSpace(root)) textBoxNewProjectRoot.Text = NormalizePath(root);
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
            string root = NormalizePath(textBoxNewProjectRoot.Text), app = textBoxAppName.Text?.Trim() ?? string.Empty;
            textFullPath.Text = (!string.IsNullOrWhiteSpace(root) && !string.IsNullOrWhiteSpace(app))
                ? $"Full path: {Path.Combine(root, app)}" : "Full path: (fill in root and name above)";
        }

        private void buttonInitProject_Click(object sender, RoutedEventArgs e)
        {
            string root = NormalizePath(textBoxNewProjectRoot.Text), app = textBoxAppName.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            { MessageBox.Show("Please select a valid Root Folder.", "Missing Root", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (string.IsNullOrWhiteSpace(app))
            { MessageBox.Show("Please enter an Application Name.", "Missing Name", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            string fullPath = Path.Combine(root, app);
            // Update Code Generation Dir field to the new project path
            textBoxCodeGenDir.Text = fullPath;
            try
            {
                Directory.CreateDirectory(fullPath);

                string args = $"init --app_name \"{app}\" --app_root \"{fullPath}\"";
                Process? proc = RunRexglue(root, args);

                if (proc != null)
                {
                    proc.WaitForExit();

                    AppendOutput(
                        $"[Initialize Project]{Environment.NewLine}" +
                        $"  Command: rexglue {args}{Environment.NewLine}" +
                        $"  ExitCode: {proc.ExitCode}{Environment.NewLine}"
                    );

                    try { Directory.CreateDirectory(Path.Combine(fullPath, "assets")); }
                    catch { }
                    MessageBox.Show(
                        $"Project initialized at:\n{fullPath}",
                        "Initialize Project",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                }
                else
                {
                    AppendOutput("[Initialize Project] Failed to start rexglue process.");
                    MessageBox.Show(
                        "Failed to start 'rexglue' process. Ensure it is installed and available in PATH.",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                }
            }
            catch (Exception ex)
            {
                AppendOutput($"[Initialize Project Error] {ex.Message}");
                MessageBox.Show(
                    $"Initialization failed:\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }
        private Process? RunRexglue(string root, string args)
        {
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(args))
                return null;

            var psi = new ProcessStartInfo
            {
                FileName = "rexglue",
                Arguments = args,
                WorkingDirectory = root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var proc = new Process
            {
                StartInfo = psi,
                EnableRaisingEvents = true
            };

            proc.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Dispatcher.Invoke(() =>
                    {
                        AppendOutput(e.Data + Environment.NewLine);
                    });
                }
            };

            proc.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Dispatcher.Invoke(() =>
                    {
                        AppendOutput("[ERR] " + e.Data + Environment.NewLine);
                    });
                }
            };

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            return proc;
        }

        //
        //  FUNCTIONS SECTION HELPERS
        //

        private void buttonWriteFunctions_Click(object sender, RoutedEventArgs e)
        {
            string text = GetEditorText();
            if (text.Contains("[functions]"))
            {
                MessageBox.Show("[functions] section already exists.", "Already Present",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Normalize to \n and split into logical lines
            string normalized = text.Replace("\r\n", "\n");
            var lines         = normalized.Split('\n').ToList();

            // Default insertion at end
            int insertIndex = lines.Count;

            // Try to map caret paragraph to a line index
            var caretPara = textBoxTomlEditor.CaretPosition.Paragraph;
            if (caretPara != null)
            {
                var paras = textBoxTomlEditor.Document.Blocks.OfType<Paragraph>().ToList();
                int idx   = paras.IndexOf(caretPara);
                if (idx >= 0 && idx < lines.Count)
                    insertIndex = idx;
            }

            if (lines.Count == 0)
            {
                lines.Add("[functions]");
            }
            else if (insertIndex >= lines.Count)
            {
                // Append at end
                if (string.IsNullOrWhiteSpace(lines[^1]))
                    lines[^1] = "[functions]";
                else
                    lines.Add("[functions]");
            }
            else
            {
                // Insert or replace at the caret line
                if (string.IsNullOrWhiteSpace(lines[insertIndex]))
                    lines[insertIndex] = "[functions]";
                else
                    lines.Insert(insertIndex, "[functions]");
            }

            SetEditorText(string.Join("\n", lines));
            AppendOutput("[Functions] Wrote [functions] header at caret line.");
        }

        private void buttonAddFunctionAddress_Click(object sender, RoutedEventArgs e)
        {
            string? addr = PromptInput("Add Function Address", "Enter function address (e.g. 0x827E9A60):");
            if (string.IsNullOrWhiteSpace(addr)) return;

            addr = addr.Trim();
            if (!addr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                addr = "0x" + addr.TrimStart('0', 'x', 'X');

            string text   = GetEditorText();
            string nlText = text.Replace("\r\n", "\n");
            var lines     = nlText.Split('\n').ToList();

            int headerIdx = lines.FindIndex(l => l.Trim() == "[functions]");
            if (headerIdx < 0)
            {
                if (lines.Count > 0 && lines[^1].Length > 0)
                    lines.Add(string.Empty);
                lines.Add("[functions]");
                headerIdx = lines.Count - 1;
            }

            if (lines.Any(l => l.TrimStart().StartsWith(addr, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show($"Address {addr} already exists under [functions].", "Already Present",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string newLine = $"{addr} = {{}}";
            lines.Insert(headerIdx + 1, newLine);

            SetEditorText(string.Join("\n", lines));
            AppendOutput($"[Functions] Added {newLine}");
        }

        private void buttonWriteRexcrt_Click(object sender, RoutedEventArgs e)
        {
            string text = GetEditorText();
            if (text.Contains("setjmp_address") || text.Contains("longjmp_address"))
            {
                MessageBox.Show("setjmp/longjmp entries are already present.", "Already Present",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Normalize to \n and split into logical lines
            string normalized = text.Replace("\r\n", "\n");
            var lines         = normalized.Split('\n').ToList();

            // Lines to insert (one per TOML line)
            const string rexcrtLine = "[rexcrt]";

            int insertIndex = lines.Count;

           
                // Insert starting exactly where the caret is
                lines.Insert(insertIndex, rexcrtLine);
            

            SetEditorText(string.Join("\n", lines));
            AppendOutput("Wrote setjmp/longjmp defaults at caret line.");
        }

        //
        //  EDITOR STATE
        //

        private string          _currentTomlPath      = string.Empty;
        private bool            _suppressEditorUpdate = false;
        private bool            _highlightPending     = false;
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

        //
        //  KEYBOARD SHORTCUTS
        //

        private void textBoxTomlEditor_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            bool ctrl  = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            bool alt   = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);

            // Ctrl+key shortcuts
            if (ctrl && !alt)
            {
                switch (e.Key)
                {
                    case Key.S:           buttonSave_Click(sender, e); e.Handled = true; return;
                    case Key.F:           OpenFindBar(replace: false); e.Handled = true; return;
                    case Key.H:           OpenFindBar(replace: true);  e.Handled = true; return;
                    case Key.G:           GoToLine();                  e.Handled = true; return;
                    case Key.D:           DuplicateLine();             e.Handled = true; return;
                    case Key.OemQuestion:
                    case Key.Divide:      ToggleLineComment();         e.Handled = true; return;
                    case Key.Home:
                        textBoxTomlEditor.CaretPosition = textBoxTomlEditor.Document.ContentStart;
                        e.Handled = true; return;
                    case Key.End:
                        textBoxTomlEditor.CaretPosition = textBoxTomlEditor.Document.ContentEnd;
                        e.Handled = true; return;
                    // Ctrl+A, Z, Y, C, X, V  — handled natively by RichTextBox
                }
            }

            // Alt+Up/Down — move line
            if (alt && !ctrl)
            {
                switch (e.Key)
                {
                    case Key.Up:   MoveLineUp();   e.Handled = true; return;
                    case Key.Down: MoveLineDown(); e.Handled = true; return;
                }
            }

            // Escape — close find bar
            if (e.Key == Key.Escape && findBar.Visibility == Visibility.Visible)
            { CloseFindBar(); e.Handled = true; return; }

            // Tab — indent / unindent selection
            if (e.Key == Key.Tab && !textBoxTomlEditor.Selection.IsEmpty)
            { IndentSelection(shift ? -1 : 1); e.Handled = true; }
        }

        //
        //  STATUS BAR — CURSOR POSITION
        //

        private void textBoxTomlEditor_SelectionChanged(object sender, RoutedEventArgs e)
            => UpdateCursorStatus();

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

        //
        //  FIND / REPLACE
        //

        private void OpenFindBar(bool replace)
        {
            findBar.Visibility          = Visibility.Visible;
            labelReplace.Visibility     = replace ? Visibility.Visible : Visibility.Collapsed;
            textBoxReplace.Visibility   = replace ? Visibility.Visible : Visibility.Collapsed;
            buttonReplaceOne.Visibility = replace ? Visibility.Visible : Visibility.Collapsed;
            buttonReplaceAll.Visibility = replace ? Visibility.Visible : Visibility.Collapsed;
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
            if (e.Key == Key.Enter)
            { if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) FindPrev(); else FindNext(); e.Handled = true; }
            else if (e.Key == Key.Escape)
            { CloseFindBar(); e.Handled = true; }
        }

        private void textBoxReplace_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            { buttonReplaceOne_Click(sender, e); e.Handled = true; }
            else if (e.Key == Key.Escape)
            { CloseFindBar(); e.Handled = true; }
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
            string text = GetEditorText();
            int count   = Regex.Matches(text, Regex.Escape(find), RegexOptions.IgnoreCase).Count;
            SetEditorText(Regex.Replace(text, Regex.Escape(find), textBoxReplace.Text, RegexOptions.IgnoreCase));
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
            while (pos != null)
            {
                var found = FindNextOccurrence(pos, textBoxTomlEditor.Document.ContentEnd, query);
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

        //
        //  EDITING COMMANDS
        //

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
            var range  = new TextRange(para.ContentStart, para.ContentEnd);
            string line = range.Text;
            range.Text  = line.TrimStart().StartsWith('#')
                ? line.TrimStart().Substring(1).TrimStart()
                : "# " + line;
            HighlightDocument();
        }

        private void DuplicateLine()
        {
            var para = textBoxTomlEditor.CaretPosition.Paragraph;
            if (para == null) return;
            string text = new TextRange(para.ContentStart, para.ContentEnd).Text;
            var newPara = new Paragraph { Margin = new Thickness(0) };
            newPara.Inlines.Add(new Run(text));
            para.SiblingBlocks.InsertAfter(para, newPara);
            HighlightDocument();
        }

        private void MoveLineUp()
        {
            var para = textBoxTomlEditor.CaretPosition.Paragraph;
            if (para?.PreviousBlock == null) return;
            var prev = para.PreviousBlock;
            string a = new TextRange(para.ContentStart, para.ContentEnd).Text;
            string b = new TextRange(prev.ContentStart, prev.ContentEnd).Text;
            new TextRange(para.ContentStart, para.ContentEnd).Text = b;
            new TextRange(prev.ContentStart, prev.ContentEnd).Text = a;
            textBoxTomlEditor.CaretPosition = prev.ContentStart;
            HighlightDocument();
        }

        private void MoveLineDown()
        {
            var para = textBoxTomlEditor.CaretPosition.Paragraph;
            if (para?.NextBlock == null) return;
            var next = para.NextBlock;
            string a = new TextRange(para.ContentStart, para.ContentEnd).Text;
            string b = new TextRange(next.ContentStart, next.ContentEnd).Text;
            new TextRange(para.ContentStart, para.ContentEnd).Text = b;
            new TextRange(next.ContentStart, next.ContentEnd).Text = a;
            textBoxTomlEditor.CaretPosition = next.ContentStart;
            HighlightDocument();
        }

        private void IndentSelection(int direction)
        {
            var sel   = textBoxTomlEditor.Selection;
            var start = sel.Start.Paragraph;
            var end   = sel.End.Paragraph;
            if (start == null) return;
            var para = start;
            while (para != null)
            {
                var range = new TextRange(para.ContentStart, para.ContentEnd);
                range.Text = direction > 0
                    ? "\t" + range.Text
                    : (range.Text.Length > 0 && range.Text[0] == '\t' ? range.Text[1..] : range.Text);
                if (para == end) break;
                para = para.NextBlock as Paragraph;
            }
            HighlightDocument();
        }

        //
        //  EDITOR TEXT HELPERS
        //

        private string GetEditorText()
        {
            var doc = textBoxTomlEditor.Document;
            return new TextRange(doc.ContentStart, doc.ContentEnd).Text;
        }

        private void SetEditorText(string text)
        {
            _suppressEditorUpdate = true;
            try
            {
                textBoxTomlEditor.Document.Blocks.Clear();
                textBoxTomlEditor.Document.PageWidth = 4000;
                foreach (string line in text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n'))
                {
                    var para = new Paragraph { Margin = new Thickness(0) };
                    para.Inlines.Add(new Run(line) { Foreground = ColDefault });
                    textBoxTomlEditor.Document.Blocks.Add(para);
                }
            }
            finally { _suppressEditorUpdate = false; }
            HighlightDocument();
            UpdateLineNumbers();
        }

        //
        //  SYNTAX HIGHLIGHTING
        //

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
                int bc         = value.LastIndexOf('}');
                string inner   = bc > bo ? value[(bo + 1)..bc] : value[(bo + 1)..];
                string trailing = bc > 0 && bc < value.Length - 1 ? value[(bc + 1)..] : string.Empty;
                para.Inlines.Add(new Run("{") { Foreground = ColBrace });
                var parts = inner.Split(',');
                for (int i = 0; i < parts.Length; i++)
                {
                    string p = parts[i];
                    int ns = 0; while (ns < p.Length && p[ns] == ' ') ns++;
                    if (ns > 0) para.Inlines.Add(new Run(p[..ns]) { Foreground = ColDefault });
                    if (ns < p.Length) para.Inlines.Add(new Run(p[ns..].TrimEnd()) { Foreground = ColIdent });
                    int ts = p.Length - p.TrimEnd().Length;
                    if (ts > 0) para.Inlines.Add(new Run(new string(' ', ts)) { Foreground = ColDefault });
                    if (i < parts.Length - 1) para.Inlines.Add(new Run(",") { Foreground = ColBrace });
                }
                para.Inlines.Add(new Run("}") { Foreground = ColBrace });
                if (!string.IsNullOrEmpty(trailing)) para.Inlines.Add(new Run(trailing) { Foreground = ColDefault });
                return;
            }
            string v = value.TrimStart(), lead = value[..^value.TrimStart().Length];
            if (lead.Length > 0) para.Inlines.Add(new Run(lead) { Foreground = ColDefault });
            if (Regex.IsMatch(v, @"^0[xX][0-9a-fA-F]+"))
            {
                var m = Regex.Match(v, @"^(0[xX][0-9a-fA-F]+)(.*)$");
                para.Inlines.Add(new Run(m.Groups[1].Value) { Foreground = ColHex });
                if (m.Groups[2].Length > 0) para.Inlines.Add(new Run(m.Groups[2].Value) { Foreground = ColDefault });
                return;
            }
            if (v.StartsWith('"'))             { para.Inlines.Add(new Run(v) { Foreground = ColString }); return; }
            if (v is "true" or "false")        { para.Inlines.Add(new Run(v) { Foreground = ColBool });   return; }
            if (Regex.IsMatch(v, @"^-?[0-9]+(\.[0-9]+)?$")) { para.Inlines.Add(new Run(v) { Foreground = ColNumber }); return; }
            para.Inlines.Add(new Run(v) { Foreground = ColDefault });
        }

        private void textBoxTomlEditor_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressEditorUpdate || _highlightPending) return;
            _highlightPending = true;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
            {
                _highlightPending = false;
                HighlightDocument();
            }));
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

        //
        //  FILE OPERATIONS

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
            if (string.IsNullOrWhiteSpace(_currentTomlPath))
            { MessageBox.Show("No file loaded.", "Nothing to Save", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            try
            {
                File.WriteAllText(_currentTomlPath, GetEditorText());
                AppendOutput($"[Saved] {_currentTomlPath}");
                textStatusLeft.Text = Path.GetFileName(_currentTomlPath);
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

        private void buttonRemoveDupes_Click(object sender, RoutedEventArgs e)
        {
            string nl = GetEditorText().Replace("\r\n", "\n");
            var lines = nl.Split('\n').ToList();
            int funcStart = lines.FindIndex(l => l.Trim().Equals("[functions]", StringComparison.OrdinalIgnoreCase));
            if (funcStart < 0) { MessageBox.Show("No [functions] section found.", "Remove Dupes", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            int funcEnd = lines.Count;
            for (int i = funcStart + 1; i < lines.Count; i++)
            {
                string s = lines[i].Trim();
                if (s.StartsWith("[", StringComparison.Ordinal) && !s.StartsWith("#", StringComparison.Ordinal)) { funcEnd = i; break; }
            }
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var newLines = new List<string>();
            int removed = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                if (i > funcStart && i < funcEnd)
                {
                    string s = line.Trim();
                    if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    {
                        int eq = s.IndexOf('=');
                        string key = (eq >= 0 ? s[..eq].Trim() : s).ToLowerInvariant();
                        if (seen.Contains(key)) { removed++; continue; }
                        seen.Add(key);
                    }
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
                btn.ContextMenu.IsOpen = true;
            }
        }

        private void ClearValuesMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem mi || mi.Tag is not string tag) return;
            bool clearName = tag == "All" || tag == "Name";
            bool clearParent = tag == "All" || tag == "Parent";
            bool clearSize = tag == "All" || tag == "Size";
            ClearFunctionValues(clearName, clearParent, clearSize);
        }

        private void ClearFunctionValues(bool clearName, bool clearParent, bool clearSize)
        {
            string nl = GetEditorText().Replace("\r\n", "\n");
            var lines = nl.Split('\n').ToList();
            int funcStart = lines.FindIndex(l => l.Trim().Equals("[functions]", StringComparison.OrdinalIgnoreCase));
            if (funcStart < 0) { MessageBox.Show("No [functions] section found.", "Clear Values", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            int funcEnd = lines.Count;
            for (int i = funcStart + 1; i < lines.Count; i++)
            {
                string s = lines[i].Trim();
                if (s.StartsWith("[", StringComparison.Ordinal) && !s.StartsWith("#", StringComparison.Ordinal)) { funcEnd = i; break; }
            }
            int cleared = 0;
            var newLines = new List<string>();
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                if (i > funcStart && i < funcEnd)
                {
                    string s = line.Trim();
                    if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    {
                        int eq = s.IndexOf('=');
                        if (eq >= 0)
                        {
                            string addr = s[..eq].Trim();
                            string valStr = s[(eq + 1)..].Trim();
                            if (clearName && clearParent && clearSize) { newLines.Add(addr + " = {}"); cleared++; continue; }
                            var inner = ParseBraceFields(valStr);
                            bool changed = false;
                            if (clearName && inner.Remove("name")) changed = true;
                            if (clearParent && inner.Remove("parent")) changed = true;
                            if (clearSize && inner.Remove("size")) changed = true;
                            if (changed) { newLines.Add(addr + " = " + RebuildBraceFields(inner)); cleared++; continue; }
                        }
                    }
                }
                newLines.Add(line);
            }
            if (cleared == 0) { AppendOutput("[Clear Values] No matching fields to clear."); return; }
            SetEditorText(string.Join("\n", newLines));
            var parts = new List<string>(); if (clearName) parts.Add("name"); if (clearParent) parts.Add("parent"); if (clearSize) parts.Add("size");
            AppendOutput($"[Clear Values] Cleared {string.Join("/", parts)} from {cleared} function(s).");
        }

        private static Dictionary<string, string> ParseBraceFields(string val)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string trimmed = val.Trim();
            if (trimmed.StartsWith("{")) trimmed = trimmed[1..];
            if (trimmed.EndsWith("}")) trimmed = trimmed[..^1];
            trimmed = trimmed.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) return result;
            foreach (string pair in trimmed.Split(','))
            {
                var kv = pair.Split('=', 2, StringSplitOptions.TrimEntries);
                if (kv.Length == 2) result[kv[0].Trim()] = kv[1].Trim();
                else if (kv.Length == 1 && !string.IsNullOrWhiteSpace(kv[0])) result[kv[0].Trim()] = "";
            }
            return result;
        }

        private static string RebuildBraceFields(Dictionary<string, string> fields)
        {
            if (fields.Count == 0) return "{}";
            return "{ " + string.Join(", ", fields.Select(kv => string.IsNullOrEmpty(kv.Value) ? kv.Key : kv.Key + " = " + kv.Value)) + " }";
        }

        private void buttonSaveBackup_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_currentTomlPath))
            { MessageBox.Show("No file loaded.", "Nothing to Back Up", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            var now = DateTime.Now;
            string stamp = $"{now:yyyyMMdd}_{now:HHmmss}";
            string backup = _currentTomlPath + ".bak_" + stamp;
            try { File.WriteAllText(backup, GetEditorText()); AppendOutput($"[Backup Saved] {backup}"); }
            catch (Exception ex) { MessageBox.Show($"Backup failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void buttonLoadBackup_Click(object sender, RoutedEventArgs e)
        {
            string? s = BrowseForFile("Load backup TOML", "TOML backup (*.bak;*.toml)|*.bak;*.toml|All files|*.*");
            if (s != null) LoadTomlFile(s);
        }

        private void buttonRunCodeGen_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_currentTomlPath) && File.Exists(_currentTomlPath))
                buttonSave_Click(sender, e);
            string rexsdk = Environment.GetEnvironmentVariable(REXSDK_ENV, EnvTarget) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rexsdk))
            { MessageBox.Show("REXSDK is not set. Run SDK Setup first.", "Not Configured", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            AppendOutput($"[Run Code Generation]\n  Config: {_currentTomlPath}\n  (rexglue codegen placeholder)");
            tabBtnOutput.IsChecked = true;
        }

        //
        //  TEMPLATE CHIPS
        //

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
            panel.Children.Add(btn);
            var close = new Button { Content = "×", Style = (Style)FindResource("ChipClose"), Tag = value };
            close.Click += (_, _) =>
            {
                if (isCustom) panelCustomTemplates.Children.Remove(panel);
                else          panelStarterTemplates.Children.Remove(panel);
            };
            panel.Children.Add(close);
            return panel;
        }

        private void ApplyTemplate(string pattern)
        {
            pattern = pattern.TrimStart('=').Trim();
            var doc = textBoxTomlEditor.Document;
            var paras = doc.Blocks.OfType<Paragraph>().ToList();
            if (paras.Count == 0) return;
            var selStart = textBoxTomlEditor.Selection.Start;
            var selEnd = textBoxTomlEditor.Selection.End;
            int fromIdx = 0, toIdx = paras.Count - 1;
            for (int i = 0; i < paras.Count; i++)
            {
                if (paras[i].ContentStart.CompareTo(selStart) <= 0 && paras[i].ContentEnd.CompareTo(selStart) >= 0) fromIdx = i;
            }
            for (int i = 0; i < paras.Count; i++)
            {
                if (paras[i].ContentStart.CompareTo(selEnd) <= 0 && paras[i].ContentEnd.CompareTo(selEnd) >= 0) { toIdx = i; break; }
            }
            int changed = 0;
            _suppressEditorUpdate = true;
            try
            {
                for (int i = fromIdx; i <= toIdx; i++)
                {
                    var para = paras[i];
                    var range = new TextRange(para.ContentStart, para.ContentEnd);
                    string line = range.Text;
                    string key = line.Trim();
                    string addrClean = "";
                    int eq = key.IndexOf('=');
                    if (eq >= 0) { key = key[..eq].Trim(); addrClean = key.Replace("0x", "").Replace("0X", ""); }
                    string filled = pattern.Replace("%ADDR%", addrClean);
                    string newLine = string.IsNullOrWhiteSpace(line) ? filled : (key + " " + filled);
                    range.Text = newLine;
                    changed++;
                }
            }
            finally { _suppressEditorUpdate = false; }
            HighlightDocument();
            UpdateLineNumbers();
            if (changed > 0) AppendOutput($"[Template] Applied to {changed} line(s).");
            else AppendOutput("[Template] No lines to apply. Select lines in the editor first.");
        }

        private void buttonResetTemplates_Click(object sender, RoutedEventArgs e)
        {
            BuildStarterChips();
            panelCustomTemplates.Children.Clear();
            AppendOutput("[Templates] Reset to defaults.");
        }

        private void btnToggleStarter_Click(object sender, RoutedEventArgs e)
        {
            panelStarterTemplates.Visibility = panelStarterTemplates.Visibility == Visibility.Visible
                ? Visibility.Collapsed : Visibility.Visible;
            btnToggleStarter.Content = panelStarterTemplates.Visibility == Visibility.Visible
                ? "▾ Starter Templates" : "▸ Starter Templates";
        }

        private void btnToggleCustom_Click(object sender, RoutedEventArgs e)
        {
            panelCustomTemplates.Visibility = panelCustomTemplates.Visibility == Visibility.Visible
                ? Visibility.Collapsed : Visibility.Visible;
            btnToggleCustom.Content = panelCustomTemplates.Visibility == Visibility.Visible
                ? "▾ Custom Templates" : "▸ Custom Templates";
        }

        private void buttonAddCustomTemplate_Click(object sender, RoutedEventArgs e)
        {
            string? result = PromptInput("New Template", "Enter template value (e.g. = { name, size }):");
            if (!string.IsNullOrWhiteSpace(result))
                panelCustomTemplates.Children.Add(MakeChip(result, result, isCustom: true));
        }

        //
        //  ADDRESS PARSER TAB
        //

        private void buttonAddrPaste_Click(object sender, RoutedEventArgs e)
        {
            try { if (Clipboard.ContainsText()) textBoxAddress.Text = Clipboard.GetText(); }
            catch { }
        }

        private void buttonAddrCopy_Click(object sender, RoutedEventArgs e)
        {
            string t = textBoxAddressResult.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(t)) { MessageBox.Show("Nothing to copy.", "Address Parser", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            try { Clipboard.SetText(t); AppendOutput("[Address Parser] Output copied to clipboard."); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Copy failed", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void buttonParseAddress_Click(object sender, RoutedEventArgs e)
        {
            string text = textBoxAddress.Text ?? "";
            if (string.IsNullOrWhiteSpace(text)) { MessageBox.Show("Input is empty.", "Address Parser", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var results = new List<string>();
            foreach (string rawLine in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string lineLower = rawLine.ToLowerInvariant();
                int fromIdx = lineLower.IndexOf(" from ", StringComparison.Ordinal);
                if (fromIdx < 0) continue;
                string before = rawLine[..fromIdx].Replace("[", " ").Replace("]", " ");
                var tokens = before.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 0) continue;
                string addr = tokens[^1].Trim();
                if (addr.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && addr.Length > 2)
                {
                    string lowerAddr = addr.ToLowerInvariant();
                    if (seen.Add(lowerAddr)) results.Add(addr + " = {}");
                }
            }
            if (results.Count == 0) { textBoxAddressResult.Text = ""; MessageBox.Show("No addresses found.", "Address Parser", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            textBoxAddressResult.Text = string.Join("\n", results);
            AppendOutput($"[Address Parser] Found {results.Count} unique address(es).");
        }

        //
        //  OUTPUT LOG
        //

        private void buttonOutputCopyAll_Click(object sender, RoutedEventArgs e)
        {
            string t = textBoxOutput.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(t)) { AppendOutput("[Output] Tab is empty."); return; }
            try { Clipboard.SetText(t); AppendOutput("[Output] Copied to clipboard."); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Copy failed", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void buttonOutputSendToParser_Click(object sender, RoutedEventArgs e)
        {
            string t = textBoxOutput.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(t)) { AppendOutput("[Output] Tab is empty."); return; }
            textBoxAddress.Text = t;
            tabBtnAddrParser.IsChecked = true;
            AppendOutput("[Output] Sent to Address Parser.");
        }

        private void buttonOutputClear_Click(object sender, RoutedEventArgs e)
        {
            textBoxOutput.Clear();
        }

        private void AppendOutput(string message)
        {
            textBoxOutput.Text += $"[{DateTime.Now:HH:mm:ss}] {message}\n\n";
            textBoxOutput.ScrollToEnd();
        }
    }
}
