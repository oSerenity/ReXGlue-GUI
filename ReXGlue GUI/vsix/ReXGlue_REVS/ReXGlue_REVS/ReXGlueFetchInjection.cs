using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EnvDTE;
using EnvDTE80;

namespace ReXGlue_REVS
{
    /// <summary>Inject ctx.ctr.u32 snapshot into TOML [functions] on disk.</summary>
    internal static class ReXGlueFetchInjection
    {
        internal static bool TryInject(string tomlPath, List<Tuple<int, uint>> entries, out int injected, out int skipped, out List<string> newlyInsertedAddresses)
        {
            injected = 0;
            skipped = 0;
            newlyInsertedAddresses = new List<string>();
            var candidates = entries.Where(e => e.Item2 != 0).ToList();
            if (candidates.Count == 0 || string.IsNullOrWhiteSpace(tomlPath) || !File.Exists(tomlPath))
                return false;

            string text = File.ReadAllText(tomlPath);
            var lines = TomlFunctions.NormalizedLines(text);
            var addrList = candidates.Select(c => "0x" + c.Item2.ToString("X8")).ToList();
            var commentMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in candidates)
                commentMap["0x" + c.Item2.ToString("X8")] = "# ctx.ctr.u32[" + c.Item1 + "]";
            Func<string, string> commentFn = a => commentMap.TryGetValue(a, out string cm) ? cm : null;
            var inj = TomlFunctions.InjectAddresses(lines, addrList, commentFn);
            injected = inj.Item1;
            skipped = inj.Item2;
            newlyInsertedAddresses = inj.Item3 ?? newlyInsertedAddresses;
            File.WriteAllText(tomlPath, string.Join("\n", lines));
            return true;
        }

        /// <summary>When paused at a breakpoint, ensure watch + return ctx.ctr.u32 rows; otherwise null.</summary>
        internal static List<Tuple<int, uint>> PrepareCtxCtrAtBreakpoint(DTE2 dte)
        {
            if (dte?.Debugger == null || dte.Debugger.CurrentMode != dbgDebugMode.dbgBreakMode)
                return null;
            DebuggerWatchHelper.EnsureCtxCtrU32InWatch1(dte,
                msg => Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.RunAsync(
                    async () => await Commands.WriteOutputAsync(msg)));
            var entries = AutoCycleController.EvaluateCtxCtr(dte, DebuggerWatchHelper.CtxCtrU32);
            if (entries == null || entries.Count == 0) return entries;

            // Match expected behavior: inject only one resulting address per fetch.
            var chosen = entries
                .Where(e => e.Item2 != 0)
                .OrderBy(e => e.Item1)
                .FirstOrDefault();
            if (chosen.Item2 == 0) return new List<Tuple<int, uint>>();
            return new List<Tuple<int, uint>> { chosen };
        }
    }
}
