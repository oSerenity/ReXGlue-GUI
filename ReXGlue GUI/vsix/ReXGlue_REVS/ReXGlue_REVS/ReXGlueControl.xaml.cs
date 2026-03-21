using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.VisualStudio.Shell;

namespace ReXGlue_REVS
{
    public partial class ReXGlueControl : UserControl
    {
        private bool _updatingEditor;
        private bool _highlightPending;
        private bool _promptedForDebugAfterCodegen;
        private static readonly Regex RxClipAddr = new Regex(@"(0x8[0-9a-fA-F]{7})\b", RegexOptions.Compiled);

        // TOML editor colors (match desktop GUI / VS Code Dark+)
        private static readonly SolidColorBrush ColDefault = new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD4));
        private static readonly SolidColorBrush ColComment = new SolidColorBrush(Color.FromRgb(0x6A, 0x99, 0x55));
        private static readonly SolidColorBrush ColSection = new SolidColorBrush(Color.FromRgb(0x56, 0x9C, 0xD6));
        private static readonly SolidColorBrush ColKey = new SolidColorBrush(Color.FromRgb(0x9C, 0xDC, 0xFE));
        private static readonly SolidColorBrush ColEquals = new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD4));
        private static readonly SolidColorBrush ColHex = new SolidColorBrush(Color.FromRgb(0xB5, 0xCE, 0xA8));
        private static readonly SolidColorBrush ColString = new SolidColorBrush(Color.FromRgb(0xCE, 0x91, 0x78));
        private static readonly SolidColorBrush ColBool = new SolidColorBrush(Color.FromRgb(0x56, 0x9C, 0xD6));
        private static readonly SolidColorBrush ColNumber = new SolidColorBrush(Color.FromRgb(0xB5, 0xCE, 0xA8));
        private static readonly SolidColorBrush ColBrace = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00));
        private static readonly SolidColorBrush ColIdent = new SolidColorBrush(Color.FromRgb(0x9C, 0xDC, 0xFE));
        private static readonly Regex RxHex = new Regex(@"^(0[xX][0-9a-fA-F]+)(.*)$", RegexOptions.Compiled);
        private static readonly Regex RxHexChk = new Regex(@"^0[xX][0-9a-fA-F]+", RegexOptions.Compiled);
        private static readonly Regex RxNum = new Regex(@"^-?[0-9]+(\.[0-9]+)?$", RegexOptions.Compiled);
        private static readonly Thickness ZeroMargin = new Thickness(0);

        private static string TryExtractAddressFromClipboard()
        {
            try
            {
                if (!System.Windows.Clipboard.ContainsText()) return null;
                var m = RxClipAddr.Match(System.Windows.Clipboard.GetText());
                return m.Success ? m.Groups[1].Value : null;
            }
            catch { return null; }
        }

        private static int GetCaretLineIndex(string text, int selectionStart)
        {
            if (string.IsNullOrEmpty(text) || selectionStart <= 0) return 0;
            int line = 0;
            for (int i = 0; i < selectionStart && i < text.Length; i++)
                if (text[i] == '\n') line++;
            return line;
        }

        private string GetTomlEditorText()
        {
            if (richTextBoxToml?.Document == null) return "";
            return new TextRange(richTextBoxToml.Document.ContentStart, richTextBoxToml.Document.ContentEnd).Text;
        }

        private void SetTomlEditorText(string text, int? caretOffset = null)
        {
            if (richTextBoxToml?.Document == null) return;
            _updatingEditor = true;
            try
            {
                var lines = (text ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                int count = lines.Length;
                if (count > 0 && count == lines.Length && lines[count - 1].Length == 0) count--;
                if (count <= 0) count = 1;
                richTextBoxToml.Document.Blocks.Clear();
                for (int i = 0; i < count; i++)
                {
                    var para = new Paragraph(new Run(lines[i])) { Margin = ZeroMargin };
                    richTextBoxToml.Document.Blocks.Add(para);
                }
                HighlightDocument();
                if (caretOffset.HasValue)
                    SetCaretOffset(Math.Min(caretOffset.Value, GetTomlEditorText().Length));
            }
            finally { _updatingEditor = false; }
        }

        private int GetCaretOffset()
        {
            if (richTextBoxToml?.Document == null) return 0;
            return richTextBoxToml.Document.ContentStart.GetOffsetToPosition(richTextBoxToml.CaretPosition);
        }

        private void SetCaretOffset(int offset)
        {
            if (richTextBoxToml?.Document == null || offset < 0) return;
            try
            {
                var pos = richTextBoxToml.Document.ContentStart.GetPositionAtOffset(offset, LogicalDirection.Forward);
                if (pos != null) richTextBoxToml.CaretPosition = pos;
            }
            catch { }
        }

        private int GetCaretLineIndexFromEditor()
        {
            if (richTextBoxToml?.Document == null) return 0;
            var caret = richTextBoxToml.CaretPosition;
            int line = 0;
            foreach (var block in richTextBoxToml.Document.Blocks)
            {
                if (block is Paragraph para && para.ContentStart.CompareTo(caret) <= 0 && para.ContentEnd.CompareTo(caret) >= 0)
                    return line;
                line++;
            }
            return Math.Max(0, line - 1);
        }

        private void RichTextBoxToml_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updatingEditor || _highlightPending) return;
            _highlightPending = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _highlightPending = false;
                HighlightDocument();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void HighlightDocument()
        {
            if (_updatingEditor || richTextBoxToml?.Document == null) return;
            _updatingEditor = true;
            try
            {
                foreach (var block in richTextBoxToml.Document.Blocks.ToList())
                {
                    if (!(block is Paragraph para)) continue;
                    string line = new TextRange(para.ContentStart, para.ContentEnd).Text;
                    para.Inlines.Clear();
                    HighlightLine(para, line);
                }
            }
            finally { _updatingEditor = false; }
        }

        private static void HighlightLine(Paragraph para, string line)
        {
            if (line == null) line = "";
            if (line.Length == 0) { para.Inlines.Add(new Run("") { Foreground = ColDefault }); return; }
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("#")) { para.Inlines.Add(new Run(line) { Foreground = ColComment }); return; }
            if (trimmed.StartsWith("[")) { para.Inlines.Add(new Run(line) { Foreground = ColSection }); return; }
            int eq = line.IndexOf('=');
            if (eq > 0)
            {
                para.Inlines.Add(new Run(line.Substring(0, eq)) { Foreground = ColKey });
                para.Inlines.Add(new Run("=") { Foreground = ColEquals });
                AddValueRuns(para, line.Substring(eq + 1));
                return;
            }
            para.Inlines.Add(new Run(line) { Foreground = ColDefault });
        }

        private static void AddValueRuns(Paragraph para, string value)
        {
            if (value == null) value = "";
            int bo = value.IndexOf('{');
            if (bo >= 0)
            {
                if (bo > 0) para.Inlines.Add(new Run(value.Substring(0, bo)) { Foreground = ColDefault });
                int bc = value.LastIndexOf('}');
                string inner = bc > bo ? value.Substring(bo + 1, bc - bo - 1) : value.Substring(bo + 1);
                string trailing = bc > 0 && bc < value.Length - 1 ? value.Substring(bc + 1) : "";
                para.Inlines.Add(new Run("{") { Foreground = ColBrace });
                var parts = inner.Split(',');
                for (int i = 0; i < parts.Length; i++)
                {
                    string p = parts[i];
                    int ns = p.Length - p.TrimStart().Length;
                    int ts = p.Length - p.TrimEnd().Length;
                    if (ns > 0) para.Inlines.Add(new Run(p.Substring(0, ns)) { Foreground = ColDefault });
                    if (ns < p.Length) para.Inlines.Add(new Run(p.Substring(ns).TrimEnd()) { Foreground = ColIdent });
                    if (ts > 0) para.Inlines.Add(new Run(new string(' ', ts)) { Foreground = ColDefault });
                    if (i < parts.Length - 1) para.Inlines.Add(new Run(",") { Foreground = ColBrace });
                }
                para.Inlines.Add(new Run("}") { Foreground = ColBrace });
                if (trailing.Length > 0) para.Inlines.Add(new Run(trailing) { Foreground = ColDefault });
                return;
            }
            string v = value.TrimStart();
            string lead = value.Length > v.Length ? value.Substring(0, value.Length - v.Length) : "";
            if (lead.Length > 0) para.Inlines.Add(new Run(lead) { Foreground = ColDefault });
            if (RxHexChk.IsMatch(v))
            {
                var m = RxHex.Match(v);
                para.Inlines.Add(new Run(m.Groups[1].Value) { Foreground = ColHex });
                if (m.Groups[2].Length > 0) para.Inlines.Add(new Run(m.Groups[2].Value) { Foreground = ColDefault });
                return;
            }
            if (v.StartsWith("\"")) { para.Inlines.Add(new Run(v) { Foreground = ColString }); return; }
            if (v == "true" || v == "false") { para.Inlines.Add(new Run(v) { Foreground = ColBool }); return; }
            if (RxNum.IsMatch(v)) { para.Inlines.Add(new Run(v) { Foreground = ColNumber }); return; }
            para.Inlines.Add(new Run(v) { Foreground = ColDefault });
        }

        public ReXGlueControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnToolUiRefreshRequested()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                bool auto = Commands.GetAutoEnabled();
                if (buttonToggleAuto != null)
                    buttonToggleAuto.Content = "Auto Cycle: " + (auto ? "On" : "Off");
            }), System.Windows.Threading.DispatcherPriority.Normal);
        }

        private void SyncCodegenOptionsUi()
        {
            var o = Commands.CurrentCodegenOptions;
            if (checkCodegenForce != null) checkCodegenForce.IsChecked = o.Force;
            if (checkCodegenExHandlers != null) checkCodegenExHandlers.IsChecked = o.EnableExceptionHandlers;
            if (comboCodegenLogLevel == null) return;
            const int expectedItems = 6; // (default) + trace..error
            if (comboCodegenLogLevel.Items.Count != expectedItems)
            {
                comboCodegenLogLevel.Items.Clear();
                comboCodegenLogLevel.Items.Add("(default)");
                foreach (var lv in new[] { "trace", "debug", "info", "warning", "error" })
                    comboCodegenLogLevel.Items.Add(lv);
            }
            string sel = string.IsNullOrWhiteSpace(o.LogLevel) ? "(default)" : o.LogLevel;
            int idx = comboCodegenLogLevel.Items.IndexOf(sel);
            comboCodegenLogLevel.SelectedIndex = idx >= 0 ? idx : 0;
        }

        private void PushCodegenOptionsFromUi()
        {
            var o = Commands.CurrentCodegenOptions;
            o.Force = checkCodegenForce?.IsChecked == true;
            o.EnableExceptionHandlers = checkCodegenExHandlers?.IsChecked == true;
            if (comboCodegenLogLevel?.SelectedItem is string s && s != "(default)")
                o.LogLevel = s;
            else
                o.LogLevel = null;
        }

        private void CodegenOption_Changed(object sender, RoutedEventArgs e) => PushCodegenOptionsFromUi();

        private void ComboCodegenLogLevel_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => PushCodegenOptionsFromUi();

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Commands.ToolUiRefreshRequested -= OnToolUiRefreshRequested;
            Commands.WriteToToolOutput = null;
        }

        // Output colors (match desktop GUI)
        private static readonly SolidColorBrush OutOk      = new SolidColorBrush(Color.FromRgb(0x4E, 0xC9, 0x4E)); // green
        private static readonly SolidColorBrush OutInfo    = new SolidColorBrush(Color.FromRgb(0x4F, 0xC1, 0xFF)); // cyan
        private static readonly SolidColorBrush OutWarn    = new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x00)); // yellow
        private static readonly SolidColorBrush OutErr     = new SolidColorBrush(Color.FromRgb(0xF4, 0x47, 0x47)); // red
        private static readonly SolidColorBrush OutDefault = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)); // grey
        private static readonly SolidColorBrush OutDim     = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77)); // dim

        private static SolidColorBrush ClassifyLine(string line)
        {
            string t = line.TrimStart();
            if (t.StartsWith("[ OK ]", StringComparison.OrdinalIgnoreCase) || t.StartsWith("[OK]", StringComparison.OrdinalIgnoreCase)) return OutOk;
            if (t.Contains("[Run Code Generation] Done. ExitCode: 0")) return OutOk;
            if (t.Contains("[Run Code Generation] Done. ExitCode: ")) return OutErr;
            if (t.StartsWith("[INFO]", StringComparison.OrdinalIgnoreCase) || t.StartsWith("[info]", StringComparison.OrdinalIgnoreCase)) return OutInfo;
            if (t.StartsWith("[warning]", StringComparison.OrdinalIgnoreCase) || t.StartsWith("[warn]", StringComparison.OrdinalIgnoreCase)) return OutWarn;
            if (t.StartsWith("[ERR]", StringComparison.OrdinalIgnoreCase) || t.StartsWith("[error]", StringComparison.OrdinalIgnoreCase)) return OutErr;
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t')) return OutDim;
            return OutDefault;
        }

        private static Paragraph MakeOutputPara(string line, SolidColorBrush color = null)
        {
            string s = line ?? "";
            var brush = color ?? ClassifyLine(s);
            var run = new Run(s) { Foreground = brush };
            var para = new Paragraph(run) { Margin = new Thickness(0), Padding = new Thickness(0) };
            return para;
        }

        private void AppendToToolOutput(string message)
        {
            if (richTextBoxOutput == null) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (message != null)
                {
                    foreach (string raw in message.Replace("\r\n", "\n").Split('\n'))
                    {
                        string line = raw ?? "";
                        richTextBoxOutput.Document.Blocks.Add(MakeOutputPara(line));
                    }
                    richTextBoxOutput.ScrollToEnd();
                    AutoParseAddressesFromOutput(message);
                    // When codegen completes successfully, ask if user wants to debug and turn on Auto
                    if (!_promptedForDebugAfterCodegen && message.IndexOf("Done. ExitCode: 0", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _promptedForDebugAfterCodegen = true;
                        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                        {
                            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                            try
                            {
                                int n = await Commands.CountCMakeListsInSolutionAsync();
                                if (n > 1)
                                {
                                    MessageBox.Show(
                                        "This workspace contains multiple CMakeLists.txt trees.\n\n" +
                                        "For the most reliable workflow, close this solution and use\n" +
                                        "File → Open → Folder on your game project directory\n" +
                                        "(the folder that contains your app CMakeLists.txt and TOML).",
                                        "ReXGlue — Multiple CMake roots",
                                        MessageBoxButton.OK,
                                        MessageBoxImage.Information);
                                }
                            }
                            catch { }
                            var result = MessageBox.Show(
                                "Code generation completed successfully.\n\nReady to start debugging? (Auto Cycle will be turned on if it's off.)",
                                "ReXGlue",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Question);
                            if (result == MessageBoxResult.Yes)
                                await Commands.DoStartDebugAndEnableAutoAsync();
                            else
                                Commands.NotifyToolUiRefresh();
                        });
                    }
                }
            }), System.Windows.Threading.DispatcherPriority.Normal);
        }

        /// <summary>Parse " from 0x..." style addresses from new output and inject into [functions].</summary>
        private void AutoParseAddressesFromOutput(string newOutput)
        {
            if (string.IsNullOrWhiteSpace(newOutput) || richTextBoxToml == null) return;
            string parsed = AddressParserHelper.Parse(newOutput);
            if (string.IsNullOrWhiteSpace(parsed)) return;
            var addresses = new System.Collections.Generic.List<string>();
            foreach (string line in parsed.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = line.IndexOf('=');
                if (eq > 0 && line.TrimStart().StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    addresses.Add(line.Substring(0, eq).Trim());
            }
            if (addresses.Count == 0) return;
            var lines = TomlFunctions.NormalizedLines(GetTomlEditorText());
            var res = TomlFunctions.InjectAddresses(lines, addresses, null);
            if (res.Item1 > 0)
            {
                _updatingEditor = true;
                try { SetTomlEditorText(string.Join("\n", lines)); } finally { _updatingEditor = false; }
                ThreadHelper.JoinableTaskFactory.RunAsync(async () => await Commands.WriteOutputAsync("[ReXGlue] Auto-parsed " + res.Item1 + " address(es) from output and added to [functions]." + (res.Item2 > 0 ? " (" + res.Item2 + " already present.)" : "")));
            }
        }

        private static bool _hasShownEmptySolutionPrompt;

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Commands.WriteToToolOutput = AppendToToolOutput;
            Commands.ToolUiRefreshRequested -= OnToolUiRefreshRequested;
            Commands.ToolUiRefreshRequested += OnToolUiRefreshRequested;
            SyncCodegenOptionsUi();
            await RefreshAsync();
            // When solution is empty or has no ReXGlue config, ask once per session
            if (!_hasShownEmptySolutionPrompt)
            {
                string path = await Commands.GetTomlPathAsync();
                if (string.IsNullOrWhiteSpace(path))
                {
                    _hasShownEmptySolutionPrompt = true;
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    var result = MessageBox.Show(
                        "No ReXGlue config found for this solution.\n\n[Yes] Set TOML path (existing project)\n[No] Initialize new project\n[Cancel] Not now",
                        "ReXGlue",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Question);
                    if (result == MessageBoxResult.Yes)
                    {
                        await Commands.DoSetTomlPathAsync();
                        await RefreshAsync(reloadEditor: true);
                    }
                    else if (result == MessageBoxResult.No)
                    {
                        var initDlg = new InitProjectDialog();
                        if (initDlg.ShowDialog() == true && !string.IsNullOrEmpty(initDlg.CreatedProjectPath))
                        {
                            string app = initDlg.CreatedAppName;
                            if (string.IsNullOrWhiteSpace(app)) app = System.IO.Path.GetFileName(initDlg.CreatedProjectPath.TrimEnd('\\', '/'));
                            string tomlPath = InitTomlPath.ResolveAfterInit(initDlg.CreatedProjectPath, app);
                            if (System.IO.File.Exists(tomlPath))
                            {
                                bool saved = await SolutionTomlPathStore.SaveAsync(Commands.GetPackage(), tomlPath);
                                await Commands.WriteOutputAsync(saved ? "[ReXGlue] TOML path saved for this solution: " + tomlPath : "[ReXGlue] Failed to save TOML path.");
                            }
                            await RefreshAsync(reloadEditor: true);
                        }
                    }
                }
            }
        }

        private async System.Threading.Tasks.Task RefreshAsync(bool reloadEditor = true)
        {
            if (Commands.GetPackage() == null) return;
            string path = await Commands.GetTomlPathAsync();
            int count = await Commands.GetFunctionsCountAsync();
            bool auto = Commands.GetAutoEnabled();
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            textTomlPath.Text = string.IsNullOrWhiteSpace(path) ? "Not set" : path;
            textTomlPath.Foreground = string.IsNullOrWhiteSpace(path) ? System.Windows.Media.Brushes.Gray : System.Windows.Media.Brushes.LightGray;
            textFunctionCount.Text = "[functions]: " + (path != null && path.Length > 0 ? count.ToString() : "—");
            buttonToggleAuto.Content = "Auto Cycle: " + (auto ? "On" : "Off");
            bool hasPath = !string.IsNullOrWhiteSpace(path);
            buttonReload.IsEnabled = hasPath;
            buttonSave.IsEnabled = hasPath;
            buttonAddSetjmp.IsEnabled = hasPath;
            buttonRemoveDupes.IsEnabled = hasPath;
            buttonClearValues.IsEnabled = hasPath;
            buttonWriteFunctions.IsEnabled = hasPath;
            buttonAddAddr.IsEnabled = hasPath;
            buttonWriteRexcrt.IsEnabled = hasPath;
            buttonSaveBackup.IsEnabled = hasPath;
            buttonLoadBackup.IsEnabled = true;
            buttonRunCodeGen.IsEnabled = hasPath;
            if (reloadEditor && hasPath)
            {
                string content = await Commands.LoadTomlContentAsync();
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                _updatingEditor = true;
                try { SetTomlEditorText(content ?? ""); } finally { _updatingEditor = false; }
            }
            else if (!hasPath)
            {
                _updatingEditor = true;
                try { SetTomlEditorText(""); } finally { _updatingEditor = false; }
            }
        }

        private void ApplyTomlTransform(Func<System.Collections.Generic.List<string>, object> transform)
        {
            var lines = TomlFunctions.NormalizedLines(GetTomlEditorText());
            var result = transform(lines);
            if (result is Tuple<System.Collections.Generic.List<string>, bool> tb)
            {
                if (tb.Item2) { _updatingEditor = true; try { SetTomlEditorText(string.Join("\n", tb.Item1)); } finally { _updatingEditor = false; } }
            }
            else if (result is Tuple<System.Collections.Generic.List<string>, int> ti)
            {
                if (ti.Item2 > 0) { _updatingEditor = true; try { SetTomlEditorText(string.Join("\n", ti.Item1)); } finally { _updatingEditor = false; } }
            }
        }

        private async void ButtonSetToml_Click(object sender, RoutedEventArgs e)
        {
            buttonSetToml.IsEnabled = false;
            try
            {
                await Commands.DoSetTomlPathAsync();
                await RefreshAsync(reloadEditor: true);
            }
            finally { buttonSetToml.IsEnabled = true; }
        }

        private async void ButtonReload_Click(object sender, RoutedEventArgs e)
        {
            buttonReload.IsEnabled = false;
            try
            {
                await RefreshAsync(reloadEditor: true);
            }
            finally { buttonReload.IsEnabled = true; }
        }

        private async void ButtonSave_Click(object sender, RoutedEventArgs e)
        {
            buttonSave.IsEnabled = false;
            try
            {
                bool ok = await Commands.SaveTomlContentAsync(GetTomlEditorText());
                if (ok)
                    await RefreshAsync(reloadEditor: false);
            }
            finally { buttonSave.IsEnabled = true; }
        }

        private async void ButtonFetchOnce_Click(object sender, RoutedEventArgs e)
        {
            _promptedForDebugAfterCodegen = false;
            buttonFetchOnce.IsEnabled = false;
            try
            {
                PushCodegenOptionsFromUi();
                // Persist tool window edits so fetch/codegen and ctx.ctr inject merge into the same on-disk TOML
                await Commands.SaveTomlContentAsync(GetTomlEditorText());
                await Commands.DoFetchOnceAsync();
                // Reload so the editor matches the file rexglue used (includes fetch-injected addresses)
                await RefreshAsync(reloadEditor: true);
            }
            finally { buttonFetchOnce.IsEnabled = true; }
        }

        private async void ButtonRunCodeGen_Click(object sender, RoutedEventArgs e)
        {
            _promptedForDebugAfterCodegen = false;
            buttonRunCodeGen.IsEnabled = false;
            try
            {
                PushCodegenOptionsFromUi();
                await Commands.SaveTomlContentAsync(GetTomlEditorText());
                string path = await Commands.GetTomlPathAsync();
                if (!string.IsNullOrWhiteSpace(path))
                {
                    var o = Commands.CurrentCodegenOptions;
                    string extra = (o.Force ? " --force" : "") + (o.EnableExceptionHandlers ? " --enable_exception_handlers" : "")
                        + (!string.IsNullOrWhiteSpace(o.LogLevel) ? " --log_level " + o.LogLevel : "");
                    await Commands.WriteOutputAsync("[ReXGlue] [Run Code Generation]\n  Config:  " + path + "\n  Command: rexglue codegen \"" + path + "\"" + extra);
                }
                await Commands.DoFetchOnceAsync();
                // Reload so the editor matches the on-disk TOML (saved edits + any fetch-injected addresses)
                await RefreshAsync(reloadEditor: true);
            }
            finally { buttonRunCodeGen.IsEnabled = true; }
        }

        private async void ButtonToggleAuto_Click(object sender, RoutedEventArgs e)
        {
            buttonToggleAuto.IsEnabled = false;
            try
            {
                await Commands.DoToggleAutoAsync();
                await RefreshAsync();
            }
            finally { buttonToggleAuto.IsEnabled = true; }
        }

        private void ButtonClearOutput_Click(object sender, RoutedEventArgs e)
        {
            richTextBoxOutput.Document.Blocks.Clear();
        }

        private void ButtonAddSetjmp_Click(object sender, RoutedEventArgs e)
        {
            var lines = TomlFunctions.NormalizedLines(GetTomlEditorText());
            var result = TomlFunctions.AddSetjmpLongjmp(lines);
            if (result.Item2) { _updatingEditor = true; try { SetTomlEditorText(string.Join("\n", result.Item1)); } finally { _updatingEditor = false; } ThreadHelper.JoinableTaskFactory.RunAsync(async () => await Commands.WriteOutputAsync("[ReXGlue] [Added] setjmp_address and longjmp_address entries.")); }
            else { ThreadHelper.JoinableTaskFactory.RunAsync(async () => await Commands.WriteOutputAsync("[ReXGlue] setjmp/longjmp entries are already present.")); }
        }

        private void ButtonClearValues_Click(object sender, RoutedEventArgs e)
        {
            var lines = TomlFunctions.NormalizedLines(GetTomlEditorText());
            var result = TomlFunctions.ClearValuesInFunctions(lines, true, true, true);
            if (result.Item2 > 0) { _updatingEditor = true; try { SetTomlEditorText(string.Join("\n", result.Item1)); } finally { _updatingEditor = false; } ThreadHelper.JoinableTaskFactory.RunAsync(async () => await Commands.WriteOutputAsync("[ReXGlue] [Clear Values] Cleared name/parent/size from " + result.Item2 + " function(s).")); }
            else { ThreadHelper.JoinableTaskFactory.RunAsync(async () => await Commands.WriteOutputAsync("[ReXGlue] [Clear Values] No matching fields to clear.")); }
        }

        private void ButtonWriteFunctions_Click(object sender, RoutedEventArgs e)
        {
            string text = GetTomlEditorText();
            if (text.IndexOf("[functions]", StringComparison.OrdinalIgnoreCase) >= 0) { ThreadHelper.JoinableTaskFactory.RunAsync(async () => await Commands.WriteOutputAsync("[ReXGlue] [functions] section already exists.")); return; }
            var lines = TomlFunctions.NormalizedLines(text);
            // Always place after longjmp_address (same as Fetch / codegen address inject), not at caret
            var result = TomlFunctions.EnsureFunctionsSection(lines);
            if (result.Item2) { _updatingEditor = true; try { SetTomlEditorText(string.Join("\n", result.Item1)); } finally { _updatingEditor = false; } ThreadHelper.JoinableTaskFactory.RunAsync(async () => await Commands.WriteOutputAsync("[ReXGlue] [Functions] Wrote [functions] after longjmp_address (or at end if missing).")); }
        }

        private void ButtonWriteRexcrt_Click(object sender, RoutedEventArgs e)
        {
            string text = GetTomlEditorText();
            if (text.IndexOf("[rexcrt]", StringComparison.OrdinalIgnoreCase) >= 0) { ThreadHelper.JoinableTaskFactory.RunAsync(async () => await Commands.WriteOutputAsync("[ReXGlue] [rexcrt] section already present.")); return; }
            ApplyTomlTransform(lines => TomlFunctions.EnsureRexcrtSection(lines));
            ThreadHelper.JoinableTaskFactory.RunAsync(async () => await Commands.WriteOutputAsync("[ReXGlue] Wrote [rexcrt] header at end."));
        }

        private async void ButtonAddAddr_Click(object sender, RoutedEventArgs e)
        {
            string prefill = TryExtractAddressFromClipboard();
            if (string.IsNullOrEmpty(prefill)) try { if (System.Windows.Clipboard.ContainsText()) prefill = System.Windows.Clipboard.GetText().Trim(); } catch { }
            string addr = await Commands.PromptAddressAsync(prefill);
            if (string.IsNullOrWhiteSpace(addr)) return;
            if (!addr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                addr = "0x" + addr.TrimStart('0', 'x', 'X');
            var lines = TomlFunctions.NormalizedLines(GetTomlEditorText());
            var res = TomlFunctions.InjectAddresses(lines, new[] { addr }, null);
            if (res.Item1 > 0) { _updatingEditor = true; try { SetTomlEditorText(string.Join("\n", lines)); } finally { _updatingEditor = false; } await Commands.WriteOutputAsync("[ReXGlue] [Functions] Added " + addr + " = {}"); }
            else { await Commands.WriteOutputAsync("[ReXGlue] Address " + addr + " already exists under [functions]."); }
        }

        private void ButtonStarter_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn) || btn.Tag == null) return;
            string insert = btn.Tag.ToString();
            int pos = GetCaretOffset();
            string text = GetTomlEditorText();
            SetTomlEditorText(text.Insert(pos, insert), pos + insert.Length);
            richTextBoxToml.Focus();
        }

        private async void ButtonSaveBackup_Click(object sender, RoutedEventArgs e)
        {
            string path = await Commands.SaveTomlBackupAsync(GetTomlEditorText());
            if (path != null) await Commands.WriteOutputAsync("[ReXGlue] [Backup Saved] " + path);
        }

        private async void ButtonLoadBackup_Click(object sender, RoutedEventArgs e)
        {
            var t = await Commands.LoadTomlBackupAsync();
            if (t != null) { _updatingEditor = true; try { SetTomlEditorText(t.Item2); } finally { _updatingEditor = false; } await RefreshAsync(reloadEditor: false); await Commands.WriteOutputAsync("[ReXGlue] Loaded backup: " + t.Item1); }
        }

        private void ButtonRemoveDupes_Click(object sender, RoutedEventArgs e)
        {
            var lines = TomlFunctions.NormalizedLines(GetTomlEditorText());
            var result = TomlFunctions.RemoveDuplicateFunctionAddresses(lines);
            if (result.Item2 > 0) { _updatingEditor = true; try { SetTomlEditorText(string.Join("\n", result.Item1)); } finally { _updatingEditor = false; } ThreadHelper.JoinableTaskFactory.RunAsync(async () => await Commands.WriteOutputAsync("[ReXGlue] [Remove Dupes] Removed " + result.Item2 + " duplicate(s) from [functions].")); }
            else { ThreadHelper.JoinableTaskFactory.RunAsync(async () => await Commands.WriteOutputAsync("[ReXGlue] [Remove Dupes] No duplicates found in [functions].")); }
        }
    }
}
