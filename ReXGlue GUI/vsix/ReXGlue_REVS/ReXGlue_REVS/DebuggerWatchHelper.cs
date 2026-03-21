using System;
using System.Collections;
using System.Reflection;
using EnvDTE;
using EnvDTE80;

namespace ReXGlue_REVS
{
    /// <summary>
    /// Ensures ctx.ctr.u32 appears in Watch 1 when debugging (Fetch / Auto use it).
    /// </summary>
    internal static class DebuggerWatchHelper
    {
        internal const string CtxCtrU32 = "ctx.ctr.u32";

        /// <summary>Reset when the debugger returns to design mode (new F5 session can add watch again if needed).</summary>
        internal static void OnDebuggerEnteredDesignMode()
        {
            _fallbackAddUsedThisDebugRun = false;
        }

        private static bool _fallbackAddUsedThisDebugRun;

        /// <summary>Activate Watch 1, add ctx.ctr.u32 if not already listed.</summary>
        internal static void EnsureCtxCtrU32InWatch1(DTE2 dte, Action<string> log = null)
        {
            if (dte?.Debugger == null || dte.Debugger.CurrentMode != dbgDebugMode.dbgBreakMode) return;
            try
            {
                ActivateWatch1(dte);
                switch (TryFindInWatchers(dte, CtxCtrU32))
                {
                    case WatchLookup.Found:
                        return;
                    case WatchLookup.NotFound:
                        dte.ExecuteCommand("Debug.AddWatch", CtxCtrU32);
                        log?.Invoke("[ReXGlue] Added " + CtxCtrU32 + " to Watch 1.");
                        return;
                    case WatchLookup.Unavailable:
                        if (_fallbackAddUsedThisDebugRun) return;
                        dte.ExecuteCommand("Debug.AddWatch", CtxCtrU32);
                        _fallbackAddUsedThisDebugRun = true;
                        log?.Invoke("[ReXGlue] Added " + CtxCtrU32 + " to Watch (could not read Watch list to verify).");
                        return;
                }
            }
            catch (Exception ex)
            {
                log?.Invoke("[ReXGlue] Could not add Watch: " + ex.Message);
            }
        }

        private enum WatchLookup { Found, NotFound, Unavailable }

        private static WatchLookup TryFindInWatchers(DTE2 dte, string expression)
        {
            string want = (expression ?? "").Trim();
            if (want.Length == 0) return WatchLookup.Unavailable;
            try
            {
                object dbg = dte.Debugger;
                for (Type t = dbg.GetType(); t != null; t = t.BaseType)
                {
                    PropertyInfo p = t.GetProperty("Watchers", BindingFlags.Public | BindingFlags.Instance);
                    if (p == null) continue;
                    object coll = p.GetValue(dbg);
                    if (!(coll is IEnumerable enumerable))
                        return WatchLookup.Unavailable;
                    foreach (object item in enumerable)
                    {
                        if (item == null) continue;
                        PropertyInfo nameProp = item.GetType().GetProperty("Name");
                        string n = nameProp?.GetValue(item) as string;
                        if (string.Equals((n ?? "").Trim(), want, StringComparison.OrdinalIgnoreCase))
                            return WatchLookup.Found;
                    }
                    return WatchLookup.NotFound;
                }
            }
            catch { }
            return WatchLookup.Unavailable;
        }

        private static void ActivateWatch1(DTE2 dte)
        {
            try
            {
                foreach (Window w in dte.Windows)
                {
                    if (string.Equals(w.Caption, "Watch 1", StringComparison.OrdinalIgnoreCase))
                    {
                        w.Activate();
                        return;
                    }
                }
            }
            catch { }
            try
            {
                dte.Windows.Item("{90243340-BD7A-11D0-93EF-00A0C90F2734}").Activate();
            }
            catch { }
        }
    }
}
