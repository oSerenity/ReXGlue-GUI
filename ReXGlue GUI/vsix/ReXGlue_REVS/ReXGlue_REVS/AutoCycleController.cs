using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;

namespace ReXGlue_REVS
{
    internal sealed class AutoCycleController
    {
        private readonly AsyncPackage _package;
        private DTE2 _dte;
        private DebuggerEvents _dbgEvents;
        private _dispDebuggerEvents_OnEnterBreakModeEventHandler _onEnterBreak;
        private bool _enabled;
        private string _lastSnapshot = "";
        private int _inFlight = 0;

        private static readonly Regex RxVsScalar = new Regex(@"^(0x[0-9a-fA-F]+|\d+)$", RegexOptions.Compiled);

        public AutoCycleController(AsyncPackage package) { _package = package; }

        public bool Enabled { get { return _enabled; } }

        public async Task ToggleAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_enabled) await StopAsync();
            else await StartAsync();
        }

        private async Task StartAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            _dte = await _package.GetServiceAsync(typeof(DTE)) as DTE2;
            if (_dte == null)
            {
                await Commands.WriteOutputAsync( "[ReXGlue] No DTE found. Auto cannot start.");
                return;
            }
            _dbgEvents = _dte.Events.DebuggerEvents;
            _onEnterBreak = OnEnterBreakMode;
            _dbgEvents.OnEnterBreakMode += _onEnterBreak;
            _enabled = true;
            await Commands.WriteOutputAsync( "[ReXGlue] Auto enabled (waiting for breakpoint).");
        }

        private async Task StopAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            try
            {
                if (_dbgEvents != null && _onEnterBreak != null)
                    _dbgEvents.OnEnterBreakMode -= _onEnterBreak;
            }
            catch { }
            _enabled = false;
            _lastSnapshot = "";
            _dbgEvents = null;
            _onEnterBreak = null;
            _dte = null;
            await Commands.WriteOutputAsync( "[ReXGlue] Auto disabled.");
        }

        private void OnEnterBreakMode(dbgEventReason Reason, ref dbgExecutionAction ExecutionAction)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (!_enabled || _dte == null) return;
                DebuggerWatchHelper.EnsureCtxCtrU32InWatch1(_dte, msg => ThreadHelper.JoinableTaskFactory.RunAsync(async () => await Commands.WriteOutputAsync(msg)));
                var entries = EvaluateCtxCtr(_dte, DebuggerWatchHelper.CtxCtrU32);
                if (entries == null || entries.Count == 0) return;
                string snapshot = string.Join(",", entries.Select(e => e.Item1 + ":" + e.Item2.ToString("X8")));
                if (snapshot == _lastSnapshot) return;
                _lastSnapshot = snapshot;
                await Commands.WriteOutputAsync( "[ReXGlue] Break: got " + entries.Count + " value(s) from ctx.ctr.u32.");
                await RunCycleAsync(entries);
            });
        }

        private async Task RunCycleAsync(List<Tuple<int, uint>> entries)
        {
            if (System.Threading.Interlocked.Exchange(ref _inFlight, 1) == 1) return;
            try
            {
                if (_dte == null) return;
                string tomlPath = await SolutionTomlPathStore.LoadAsync(_package);
                if (string.IsNullOrWhiteSpace(tomlPath) || !File.Exists(tomlPath))
                {
                    await Commands.WriteOutputAsync( "[ReXGlue] TOML path not set or file missing. Run: ReXGlue: Set TOML Path (Solution)");
                    return;
                }
                var candidates = entries.Where(e => e.Item2 != 0).ToList();
                if (candidates.Count == 0)
                {
                    await Commands.WriteOutputAsync( "[ReXGlue] ctx values are all zero — nothing to inject.");
                    return;
                }
                string text = File.ReadAllText(tomlPath);
                List<string> lines = TomlFunctions.NormalizedLines(text);
                var addrList = candidates.Select(c => "0x" + c.Item2.ToString("X8")).ToList();
                var commentMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var c in candidates)
                    commentMap["0x" + c.Item2.ToString("X8")] = "# ctx.ctr.u32[" + c.Item1 + "]";
                Func<string, string> commentFn = a => commentMap.TryGetValue(a, out string c) ? c : null;
                var inj = TomlFunctions.InjectAddresses(lines, addrList, commentFn);
                File.WriteAllText(tomlPath, string.Join("\n", lines));
                await Commands.WriteOutputAsync( "[ReXGlue] Injected " + inj.Item1 + " address(es) into [functions]." + (inj.Item2 > 0 ? " (" + inj.Item2 + " already present.)" : ""));

                // Stop debugging before codegen (releases file locks; matches usage: stop → codegen → repeat)
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                await Commands.WriteOutputAsync( "[ReXGlue] Stopping debugger…");
                try
                {
                    if (_dte != null && _dte.Debugger != null && _dte.Debugger.CurrentMode != dbgDebugMode.dbgDesignMode)
                        _dte.Debugger.Stop(true);
                }
                catch (Exception stopEx)
                {
                    await Commands.WriteOutputAsync( "[ReXGlue] Stop debug: " + stopEx.Message);
                }
                await Commands.WaitForDebuggerDesignModeAsync(_dte);
                await Commands.WriteOutputAsync( "[ReXGlue] Running codegen…");

                int exit = await RexglueRunner.RunCodegenAsync(_package, tomlPath, Commands.CurrentCodegenOptions).ConfigureAwait(false);
                if (exit != 0) return;

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (_dte == null) return;
                await Commands.WriteOutputAsync( "[ReXGlue] Building solution…");
                try { _dte.Solution.SolutionBuild.Build(true); } catch (Exception buildEx) { await Commands.WriteOutputAsync( "[ReXGlue] Build: " + buildEx.Message); }
                await Commands.WriteOutputAsync( "[ReXGlue] Starting debugger…");
                try { _dte.ExecuteCommand("Debug.Start"); } catch (Exception startEx) { await Commands.WriteOutputAsync( "[ReXGlue] Start debug: " + startEx.Message); }
            }
            catch (Exception ex)
            {
                await Commands.WriteOutputAsync( "[ReXGlue] Auto cycle failed: " + ex.Message);
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _inFlight, 0);
            }
        }

        internal static List<Tuple<int, uint>> EvaluateCtxCtr(DTE2 dte, string expression)
        {
            if (dte.Debugger == null || dte.Debugger.CurrentMode != dbgDebugMode.dbgBreakMode) return null;
            var dbg = (Debugger2)dte.Debugger;
            var exprObj = dbg.GetExpression(expression, true, 2000);
            if (exprObj == null || !exprObj.IsValidValue) return null;
            return ParseVsValue(exprObj.Value ?? "", dbg, expression);
        }

        private static List<Tuple<int, uint>> ParseVsValue(string raw, Debugger2 dbg, string baseExpr)
        {
            var result = new List<Tuple<int, uint>>();
            string t = raw.Trim();
            if (RxVsScalar.IsMatch(t))
            {
                uint v;
                if (TryParseU32(t, out v)) result.Add(Tuple.Create(0, v));
                return result;
            }
            if (t.IndexOf('{') >= 0)
            {
                string inner = t.Trim().TrimStart('{').TrimEnd('}');
                int idx = 0;
                foreach (string tok in inner.Split(','))
                {
                    uint v;
                    if (TryParseU32(tok.Trim(), out v)) result.Add(Tuple.Create(idx, v));
                    idx++;
                }
                if (result.Count > 0) return result;
            }
            for (int i = 0; i < 64; i++)
            {
                try
                {
                    var el = dbg.GetExpression(baseExpr + "[" + i + "]", false, 500);
                    if (el == null || !el.IsValidValue) break;
                    uint v;
                    if (TryParseU32((el.Value ?? "").Trim(), out v)) result.Add(Tuple.Create(i, v));
                }
                catch { break; }
            }
            return result;
        }

        private static bool TryParseU32(string s, out uint value)
        {
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return uint.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
            return uint.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
    }
}
