using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace ReXGlue_REVS
{
    internal static class Commands
    {
        public static readonly Guid CommandSet = new Guid("9e8b3a57-25f5-4b43-b4d0-2a08b5a64c79");
        public const int CmdIdShowWindow = 0x0103;
        public const int CmdIdShowWindowView = 0x0104;
        public const int CmdIdInitProject = 0x0105;
        public const int CmdIdMigrateProject = 0x0106;
        public const int CmdIdWikiHome = 0x0107;
        public const int CmdIdWikiGettingStarted = 0x0108;
        public const int CmdIdWikiToml = 0x0109;
        public const int CmdIdWikiCli = 0x010A;
        public const int CmdIdWikiCodegen = 0x010B;

        /// <summary>Codegen flags for Run Code Gen / Fetch / Auto (tool window updates these).</summary>
        internal static readonly CodegenOptions CurrentCodegenOptions = new CodegenOptions();

        private static AsyncPackage _package;
        private static AutoCycleController _auto;
        private static _dispDebuggerEvents_OnEnterDesignModeEventHandler _onEnterDesignMode;

        internal static AsyncPackage GetPackage() => _package;
        internal static bool GetAutoEnabled() => _auto != null && _auto.Enabled;

        /// <summary>Fired after Auto Cycle state may have changed from a background path (e.g. post-codegen prompt).</summary>
        internal static event Action ToolUiRefreshRequested;

        internal static void NotifyToolUiRefresh() => ToolUiRefreshRequested?.Invoke();

        /// <summary>ReXGlue tool window subscribes to mark newly injected [functions] addresses green in the editor.</summary>
        internal static Action<IReadOnlyList<string>> OnInjectedAddressesForEditorHighlight;

        internal static void NotifyInjectedAddressesForHighlight(IReadOnlyList<string> addresses)
        {
            if (addresses == null || addresses.Count == 0) return;
            try { OnInjectedAddressesForEditorHighlight?.Invoke(addresses); }
            catch { }
        }

        /// <summary>Registered by the tool window so Auto/Fetch can persist in-memory TOML edits before touching disk.</summary>
        internal static Func<string> TryGetToolWindowTomlText;

        /// <summary>Writes the tool window editor content to the solution TOML path when the window is loaded (same as Fetch after Save).</summary>
        internal static async Task FlushToolWindowTomlToDiskIfAvailableAsync()
        {
            if (_package == null) return;
            var getter = TryGetToolWindowTomlText;
            if (getter == null) return;
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            string text;
            try { text = getter(); }
            catch { return; }
            if (text == null) return;
            await SaveTomlContentAsync(text);
        }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            _package = package;
            _auto = new AutoCycleController(package);
            await package.JoinableTaskFactory.SwitchToMainThreadAsync();
            var mcs = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (mcs == null) return;
            mcs.AddCommand(new OleMenuCommand(OnShowWindow, new CommandID(CommandSet, CmdIdShowWindow)));
            mcs.AddCommand(new OleMenuCommand(OnShowWindow, new CommandID(CommandSet, CmdIdShowWindowView)));
            mcs.AddCommand(new OleMenuCommand(OnInitProject, new CommandID(CommandSet, CmdIdInitProject)));
            mcs.AddCommand(new OleMenuCommand(OnMigrateProject, new CommandID(CommandSet, CmdIdMigrateProject)));
            foreach (var (cmdId, suffix) in new[]
            {
                (CmdIdWikiHome, ""),
                (CmdIdWikiGettingStarted, "/Getting-Started"),
                (CmdIdWikiToml, "/rexglue-CLI-Configuration-File"),
                (CmdIdWikiCli, "/rexglue-CLI-Commands"),
                (CmdIdWikiCodegen, "/Codegen-Pipeline-Overview")
            })
            {
                string url = ReXGlueConstants.WikiRoot + suffix;
                mcs.AddCommand(new OleMenuCommand((_, __) => OpenWiki(url), new CommandID(CommandSet, cmdId)));
            }
            var dte = await package.GetServiceAsync(typeof(DTE)) as DTE2;
            if (dte?.Events?.DebuggerEvents != null)
            {
                _onEnterDesignMode = _ => DebuggerWatchHelper.OnDebuggerEnteredDesignMode();
                dte.Events.DebuggerEvents.OnEnterDesignMode += _onEnterDesignMode;
            }
        }

        private static void OnShowWindow(object sender, EventArgs e)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (_package == null) return;
                var window = _package.FindToolWindow(typeof(ReXGlueToolWindow), 0, true);
                if (window?.Frame is IVsWindowFrame frame)
                    frame.Show();
            });
        }

        private static void OnInitProject(object sender, EventArgs e)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (_package == null) return;
                var dlg = new InitProjectDialog();
                if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.CreatedProjectPath))
                {
                    string app = dlg.CreatedAppName;
                    if (string.IsNullOrWhiteSpace(app)) app = Path.GetFileName(dlg.CreatedProjectPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    string tomlPath = InitTomlPath.ResolveAfterInit(dlg.CreatedProjectPath, app);
                    if (File.Exists(tomlPath))
                    {
                        bool saved = await SolutionTomlPathStore.SaveAsync(_package, tomlPath);
                        await WriteOutputAsync(saved ? "[ReXGlue] TOML path saved for this solution: " + tomlPath : "[ReXGlue] Failed to save TOML path.");
                    }
                    else
                        await WriteOutputAsync("[ReXGlue] Init finished; expected config at:\n  " + tomlPath + "\nUse Set TOML Path if different.");
                }
            });
        }

        private static void OnMigrateProject(object sender, EventArgs e)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (_package == null) return;
                using (var fbd = new FolderBrowserDialog())
                {
                    fbd.Description = "Select ReXGlue project root (folder with CMakeLists.txt)";
                    if (fbd.ShowDialog() != DialogResult.OK) return;
                    bool force = MessageBox.Show(
                        "Run migrate with --force? (Skips confirmation when overwriting templates.)",
                        "rexglue migrate",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question) == DialogResult.Yes;
                    string appRoot = fbd.SelectedPath;
                    int code = await RexglueRunner.RunMigrateAsync(appRoot, force).ConfigureAwait(false);
                    if (code == 0 && _package != null)
                        await PostMigrateRefreshCMakeAsync(appRoot);
                }
            });
        }

        private static void OpenWiki(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch { }
        }

        internal static async Task<string> GetTomlPathAsync()
        {
            if (_package == null) return null;
            return await SolutionTomlPathStore.LoadAsync(_package);
        }

        internal static async Task DoSetTomlPathAsync()
        {
            if (_package == null) return;
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "Select ReXGlue TOML config file";
                dlg.Filter = "TOML files (*.toml)|*.toml|All files (*.*)|*.*";
                dlg.Multiselect = false;
                if (dlg.ShowDialog() != DialogResult.OK) return;
                bool ok = await SolutionTomlPathStore.SaveAsync(_package, dlg.FileName);
                await WriteOutputAsync(ok ? "[ReXGlue] TOML path saved for this solution: " + dlg.FileName : "[ReXGlue] Failed to save TOML path.");
            }
        }

        internal static async Task DoFetchOnceAsync()
        {
            if (_package == null) return;
            string tomlPath = await SolutionTomlPathStore.LoadAsync(_package);
            if (string.IsNullOrWhiteSpace(tomlPath))
            {
                await WriteOutputAsync("[ReXGlue] No TOML configured for this solution. Run Set TOML Path first.");
                return;
            }
            await FlushToolWindowTomlToDiskIfAvailableAsync();
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var dte = await _package.GetServiceAsync(typeof(DTE)) as DTE2;
            bool didFetch = false;
            if (dte != null)
            {
                // Paused at breakpoint: inject ctx.ctr.u32 into TOML on disk, then stop (releases locks).
                if (dte.Debugger != null && dte.Debugger.CurrentMode == dbgDebugMode.dbgBreakMode)
                    didFetch = await TryFetchFromCtxCtrAtBreakpointAndStopDebuggerAsync(dte, tomlPath);
                // Running (not paused): still stop before codegen so rexglue can read/write the TOML.
                else if (dte.Debugger != null && dte.Debugger.CurrentMode == dbgDebugMode.dbgRunMode)
                    await StopDebuggerIfRunningAsync(dte);
            }
            // Run codegen (reads tomlPath from disk — caller should SaveTomlContentAsync first when using the tool window editor)
            await RexglueRunner.RunCodegenAsync(_package, tomlPath, CurrentCodegenOptions).ConfigureAwait(false);
            // Only rebuild and start debugger again when we actually did a fetch (we stopped the debugger)
            if (!didFetch) return;
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (dte != null)
                await BuildSolutionAndRestartDebuggerAsync(dte);
        }

        private static async Task<bool> TryFetchFromCtxCtrAtBreakpointAndStopDebuggerAsync(DTE2 dte, string tomlPath)
        {
            if (dte?.Debugger == null || dte.Debugger.CurrentMode != dbgDebugMode.dbgBreakMode)
                return false;

            await FlushToolWindowTomlToDiskIfAvailableAsync();

            // 1) Grab ctx.ctr.u32 at breakpoint and inject into on-disk TOML
            var entries = ReXGlueFetchInjection.PrepareCtxCtrAtBreakpoint(dte);
            if (entries != null && entries.Count > 0 && File.Exists(tomlPath) &&
                ReXGlueFetchInjection.TryInject(tomlPath, entries, out int injected, out int skipped, out var newlyInserted))
            {
                if (newlyInserted != null && newlyInserted.Count > 0)
                    NotifyInjectedAddressesForHighlight(newlyInserted);
                await WriteOutputAsync("[ReXGlue] Fetched " + entries.Count + " value(s) from ctx.ctr.u32; injected " + injected +
                    " address(es) into [functions]." + (skipped > 0 ? " (" + skipped + " already present.)" : ""));
            }

            // 2) Stop debugging, then 3) wait until VS reports design mode before codegen
            await WriteOutputAsync("[ReXGlue] Stopping debugger…");
            try
            {
                if (dte.Debugger.CurrentMode != dbgDebugMode.dbgDesignMode)
                {
                    dte.Debugger.Stop(true);
                    bool stoppedOk = await WaitForDebuggerDesignModeAsync(dte);
                    return stoppedOk;
                }
            }
            catch (Exception ex)
            {
                await WriteOutputAsync("[ReXGlue] Stop debug: " + ex.Message);
            }

            return false;
        }

        /// <summary>Stop when the debuggee is executing (F5 running). Break mode is handled by fetch+stop.</summary>
        private static async Task StopDebuggerIfRunningAsync(DTE2 dte)
        {
            if (dte?.Debugger == null) return;
            if (dte.Debugger.CurrentMode != dbgDebugMode.dbgRunMode) return;
            await WriteOutputAsync("[ReXGlue] Stopping debugger…");
            try
            {
                if (dte.Debugger.CurrentMode != dbgDebugMode.dbgDesignMode)
                {
                    dte.Debugger.Stop(true);
                    bool stoppedOk = await WaitForDebuggerDesignModeAsync(dte);
                    if (!stoppedOk)
                        await WriteOutputAsync("[ReXGlue] Warning: debugger did not reach design mode; continuing.");
                }
            }
            catch (Exception ex)
            {
                await WriteOutputAsync("[ReXGlue] Stop debug: " + ex.Message);
            }
        }

        /// <summary>
        /// After <see cref="Debugger.Stop"/>, polls until <see cref="Debugger.CurrentMode"/> is design mode
        /// so rexglue codegen runs with the debug session fully torn down and file locks released.
        /// </summary>
        internal static async Task<bool> WaitForDebuggerDesignModeAsync(DTE2 dte, int maxWaitMs = 20000)
        {
            if (dte?.Debugger == null) return false;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (dte.Debugger.CurrentMode == dbgDebugMode.dbgDesignMode)
            {
                await WriteOutputAsync("[ReXGlue] Debugger stopped — verified (design mode).");
                return true;
            }

            await WriteOutputAsync("[ReXGlue] Waiting for debugger to fully stop…");
            int waited = 0;
            const int stepMs = 100;
            while (waited < maxWaitMs)
            {
                await Task.Delay(stepMs).ConfigureAwait(false);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (dte.Debugger == null || dte.Debugger.CurrentMode == dbgDebugMode.dbgDesignMode)
                {
                    await WriteOutputAsync("[ReXGlue] Debugger stopped — verified (design mode).");
                    return true;
                }
                waited += stepMs;
            }

            string mode = "(unknown)";
            try { mode = dte.Debugger != null ? dte.Debugger.CurrentMode.ToString() : "(null)"; } catch { }
            await WriteOutputAsync("[ReXGlue] Warning: debugger did not reach design mode within " + (maxWaitMs / 1000) +
                "s; last mode=" + mode);
            return false;
        }

        private static async Task BuildSolutionAndRestartDebuggerAsync(DTE2 dte)
        {
            await WriteOutputAsync("[ReXGlue] Building solution…");
            try
            {
                dte.Solution.SolutionBuild.Build(true); // wait for build to finish
            }
            catch (Exception ex) { await WriteOutputAsync("[ReXGlue] Build: " + ex.Message); }

            await WriteOutputAsync("[ReXGlue] Starting debugger…");
            try { dte.ExecuteCommand("Debug.Start"); } catch (Exception ex) { await WriteOutputAsync("[ReXGlue] Start debug: " + ex.Message); }
        }

        internal static async Task DoToggleAutoAsync()
        {
            if (_auto == null) return;
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            await _auto.ToggleAsync();
            NotifyToolUiRefresh();
        }

        /// <summary>Start debugging and enable Auto Cycle if currently off. Call from UI thread.</summary>
        internal static async Task DoStartDebugAndEnableAutoAsync()
        {
            if (_package == null) return;
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var dte = await _package.GetServiceAsync(typeof(DTE)) as DTE2;
            if (dte != null)
            {
                try { dte.ExecuteCommand("Debug.Start"); } catch (Exception ex) { await WriteOutputAsync("[ReXGlue] Start debug: " + ex.Message); }
                if (!GetAutoEnabled() && _auto != null)
                {
                    await _auto.ToggleAsync();
                    if (GetAutoEnabled())
                        await WriteOutputAsync("[ReXGlue] Auto Cycle turned on.");
                    else
                        await WriteOutputAsync("[ReXGlue] Auto Cycle could not start (no DTE).");
                }
                NotifyToolUiRefresh();
            }
        }

        /// <summary>True when a debug session is active (running or at a breakpoint), not design mode.</summary>
        internal static async Task<bool> IsDebuggerRunningAsync()
        {
            if (_package == null) return false;
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var dte = await _package.GetServiceAsync(typeof(DTE)) as DTE2;
            if (dte?.Debugger == null) return false;
            return dte.Debugger.CurrentMode != dbgDebugMode.dbgDesignMode;
        }

        internal static async Task<int> CountCMakeListsInSolutionAsync()
        {
            if (_package == null) return 0;
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var dte = await _package.GetServiceAsync(typeof(DTE)) as DTE2;
            if (dte?.Solution == null) return 0;
            string full = dte.Solution.FullName;
            if (string.IsNullOrWhiteSpace(full)) return 0;
            string dir = Path.GetDirectoryName(full);
            return CMakeWorkspaceHelper.CountCMakeListsExcludingBuildDirs(dir);
        }

        /// <summary>
        /// Show "multiple CMake roots" only when the workspace has several CMake trees <em>and</em>
        /// the saved TOML path is <strong>not</strong> under the current solution directory (user did not
        /// open the folder that contains their game project / TOML).
        /// </summary>
        internal static async Task<bool> ShouldWarnMultipleCMakeRootsAsync()
        {
            int n = await CountCMakeListsInSolutionAsync();
            if (n <= 1) return false;
            if (_package == null) return false;
            string tomlPath = await SolutionTomlPathStore.LoadAsync(_package);
            if (string.IsNullOrWhiteSpace(tomlPath) || !File.Exists(tomlPath)) return false;
            string tomlDir = Path.GetDirectoryName(tomlPath);
            if (string.IsNullOrWhiteSpace(tomlDir)) return false;
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var dte = await _package.GetServiceAsync(typeof(DTE)) as DTE2;
            if (dte?.Solution == null) return false;
            string full = dte.Solution.FullName;
            if (string.IsNullOrWhiteSpace(full)) return false;
            string solutionDir = Path.GetDirectoryName(full);
            if (string.IsNullOrWhiteSpace(solutionDir)) return false;
            try
            {
                solutionDir = Path.GetFullPath(solutionDir);
            }
            catch
            {
                return false;
            }
            // TOML lives inside the opened workspace → user has the right tree; do not nag.
            if (CMakeWorkspaceHelper.IsDescendantOrSameDirectory(tomlDir, solutionDir)) return false;
            return true;
        }

        private static async Task PostMigrateRefreshCMakeAsync(string appRoot)
        {
            if (_package == null) return;
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (CMakeWorkspaceHelper.TryDeleteOutFolder(appRoot, out string delErr))
                await WriteOutputAsync("[ReXGlue] Removed folder: " + Path.Combine(appRoot, "out"));
            else if (!string.IsNullOrEmpty(delErr))
                await WriteOutputAsync("[ReXGlue] Could not remove out folder: " + delErr);

            var dte = await _package.GetServiceAsync(typeof(DTE)) as DTE2;
            if (dte != null)
            {
                foreach (string cmd in ReXGlueConstants.CMakeDeleteCacheCommands)
                {
                    try
                    {
                        dte.ExecuteCommand(cmd);
                        await WriteOutputAsync("[ReXGlue] Ran VS command: " + cmd + " (if CMake workspace is open).");
                        return;
                    }
                    catch { }
                }
            }
            await WriteOutputAsync("[ReXGlue] If CMake still looks wrong: CMake menu → Delete Cache and Reconfigure, or reopen the folder.");
        }

        internal static async Task DoOpenTomlInEditorAsync()
        {
            if (_package == null) return;
            string path = await SolutionTomlPathStore.LoadAsync(_package);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                await WriteOutputAsync("[ReXGlue] No TOML path set or file missing. Set TOML Path first.");
                return;
            }
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var dte = await _package.GetServiceAsync(typeof(DTE)) as DTE;
            if (dte != null)
                dte.ItemOperations.OpenFile(path);
        }

        internal static async Task DoOpenReXGlueOutputPaneAsync()
        {
            if (_package == null) return;
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var output = await _package.GetServiceAsync(typeof(SVsOutputWindow)) as IVsOutputWindow;
            if (output == null) return;
            Guid paneGuid = ReXGlueConstants.OutputPaneGuid;
            output.CreatePane(ref paneGuid, "ReXGlue", 1, 1);
            output.GetPane(ref paneGuid, out IVsOutputWindowPane pane);
            pane?.Activate();
        }

        internal static async Task<string> LoadTomlContentAsync()
        {
            if (_package == null) return null;
            string path = await SolutionTomlPathStore.LoadAsync(_package);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            try
            {
                return await System.Threading.Tasks.Task.Run(() => File.ReadAllText(path));
            }
            catch { return null; }
        }

        internal static async Task<bool> SaveTomlContentAsync(string content)
        {
            if (_package == null) return false;
            string path = await SolutionTomlPathStore.LoadAsync(_package);
            if (string.IsNullOrWhiteSpace(path)) return false;
            try
            {
                await System.Threading.Tasks.Task.Run(() => File.WriteAllText(path, content ?? ""));
                return true;
            }
            catch (Exception ex)
            {
                await WriteOutputAsync("[ReXGlue] Save failed: " + ex.Message);
                return false;
            }
        }

        internal static async Task<int> GetFunctionsCountAsync()
        {
            if (_package == null) return 0;
            string path = await SolutionTomlPathStore.LoadAsync(_package);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return 0;
            try
            {
                var lines = TomlFunctions.NormalizedLines(File.ReadAllText(path));
                return TomlFunctions.CountFunctionsInSection(lines);
            }
            catch { return 0; }
        }

        internal static async Task<int> DoRemoveDupesAsync()
        {
            if (_package == null) return -1;
            string path = await SolutionTomlPathStore.LoadAsync(_package);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                await WriteOutputAsync("[ReXGlue] No TOML path set or file missing. Set TOML Path first.");
                return -1;
            }
            try
            {
                var lines = TomlFunctions.NormalizedLines(File.ReadAllText(path));
                var result = TomlFunctions.RemoveDuplicateFunctionAddresses(lines);
                if (result.Item2 > 0)
                {
                    File.WriteAllText(path, string.Join("\n", result.Item1));
                    await WriteOutputAsync("[ReXGlue] Removed " + result.Item2 + " duplicate address(es) from [functions].");
                    return result.Item2;
                }
                return 0;
            }
            catch (Exception ex)
            {
                await WriteOutputAsync("[ReXGlue] Remove dupes failed: " + ex.Message);
                return -1;
            }
        }

        internal static async Task<string> SaveTomlBackupAsync(string content)
        {
            if (_package == null) return null;
            string path = await SolutionTomlPathStore.LoadAsync(_package);
            if (string.IsNullOrWhiteSpace(path)) return null;
            string dir = Path.GetDirectoryName(path) ?? ".";
            string baseName = Path.GetFileNameWithoutExtension(path);
            string backupPath = Path.Combine(dir, baseName + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".bak");
            try
            {
                await System.Threading.Tasks.Task.Run(() => File.WriteAllText(backupPath, content ?? ""));
                await WriteOutputAsync("[ReXGlue] Backup saved: " + backupPath);
                return backupPath;
            }
            catch (Exception ex)
            {
                await WriteOutputAsync("[ReXGlue] Backup failed: " + ex.Message);
                return null;
            }
        }

        internal static async Task<Tuple<string, string>> LoadTomlBackupAsync()
        {
            if (_package == null) return null;
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "Load backup TOML";
                dlg.Filter = "Backup (*.bak)|*.bak|Legacy timestamp (*bak_*)|*bak_*|TOML (*.toml)|*.toml|All files (*.*)|*.*";
                dlg.Multiselect = false;
                if (dlg.ShowDialog() != DialogResult.OK) return null;
                string path = dlg.FileName;
                try
                {
                    string content = File.ReadAllText(path);
                    await SolutionTomlPathStore.SaveAsync(_package, path);
                    return Tuple.Create(path, content);
                }
                catch (Exception ex)
                {
                    await WriteOutputAsync("[ReXGlue] Load backup failed: " + ex.Message);
                    return null;
                }
            }
        }

        internal static async Task<string> PromptAddressAsync(string prefill = null)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            using (var form = new Form { Text = "Add function address", Width = 360, Height = 120, FormBorderStyle = FormBorderStyle.FixedDialog })
            {
                var lbl = new Label { Text = "Address (e.g. 0x827E9A60):", Left = 12, Top = 12 };
                var txt = new TextBox { Left = 12, Top = 32, Width = 320, Text = prefill ?? "" };
                var ok = new Button { Text = "OK", Left = 180, Top = 58, Width = 72, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Cancel", Left = 260, Top = 58, Width = 72, DialogResult = DialogResult.Cancel };
                form.AcceptButton = ok; form.CancelButton = cancel;
                form.Controls.AddRange(new Control[] { lbl, txt, ok, cancel });
                return form.ShowDialog() == DialogResult.OK ? txt.Text.Trim() : null;
            }
        }

        /// <summary>When set by the tool window, all output goes here only (no VS Output pane).</summary>
        internal static Action<string> WriteToToolOutput;

        internal static Task WriteOutputAsync(string message)
        {
            if (WriteToToolOutput != null)
            {
                try { WriteToToolOutput(message); } catch { }
                return Task.CompletedTask;
            }
            return _package == null ? Task.CompletedTask : WriteOutputForPackageAsync(_package, message);
        }

        internal static async Task WriteOutputForPackageAsync(AsyncPackage package, string message)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var output = await package.GetServiceAsync(typeof(SVsOutputWindow)) as IVsOutputWindow;
            if (output == null) return;
            Guid paneGuid = ReXGlueConstants.OutputPaneGuid;
            output.CreatePane(ref paneGuid, "ReXGlue", 1, 1);
            output.GetPane(ref paneGuid, out IVsOutputWindowPane pane);
            if (pane != null)
            {
                pane.OutputStringThreadSafe(message + Environment.NewLine);
                pane.Activate();
            }
        }
    }
}
