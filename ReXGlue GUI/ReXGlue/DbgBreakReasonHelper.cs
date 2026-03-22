using System;
using EnvDTE;

namespace ReXGlue.Debugger
{
    /// <summary>
    /// Classifies <see cref="dbgEventReason"/> from <c>DebuggerEvents.OnEnterBreakMode</c>
    /// so Auto Cycle / auto-fetch only run on real breakpoint hits, not on step, exception, or Break All.
    /// </summary>
    internal static class DbgBreakReasonHelper
    {
        /// <summary>
        /// Returns true only when the debugger paused because a breakpoint was hit
        /// (line, tracepoint, function/data breakpoints report as this in VS).
        /// </summary>
        internal static bool IsBreakpointHit(dbgEventReason reason)
        {
            return reason == dbgEventReason.dbgEventReasonBreakpoint;
        }

        /// <summary>
        /// Auto Cycle should mirror manual Fetch while paused: run for breakpoint-like stops, but not for
        /// single-step noise, context switches, or exception breaks (use Fetch there if needed).
        /// Some VS/native combinations report values other than <see cref="dbgEventReason.dbgEventReasonBreakpoint"/>
        /// for line breaks; the default branch allows those while still excluding step/context/exception.
        /// </summary>
        internal static bool ShouldRunAutoCycleOnBreakReason(dbgEventReason reason)
        {
            switch (reason)
            {
                case dbgEventReason.dbgEventReasonStep:
                case dbgEventReason.dbgEventReasonContextSwitch:
                case dbgEventReason.dbgEventReasonExceptionThrown:
                case dbgEventReason.dbgEventReasonExceptionNotHandled:
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>
        /// Human-readable label for output window (enum name or numeric fallback).
        /// </summary>
        internal static string Format(dbgEventReason reason)
        {
            try
            {
                return Enum.IsDefined(typeof(dbgEventReason), reason)
                    ? reason.ToString()
                    : "0x" + ((int)reason).ToString("X");
            }
            catch
            {
                return "0x" + ((int)reason).ToString("X");
            }
        }

        /// <summary>
        /// Whether to emit a skip line when Auto is not running for this reason.
        /// Suppresses noise for single-stepping (fires very often).
        /// </summary>
        internal static bool ShouldLogSkippedAuto(dbgEventReason reason)
        {
            return reason != dbgEventReason.dbgEventReasonStep
                && reason != dbgEventReason.dbgEventReasonContextSwitch;
        }

        /// <summary>When Auto skips due to <see cref="ShouldRunAutoCycleOnBreakReason"/>, avoid spam for step/context.</summary>
        internal static bool ShouldLogAutoCycleFilteredReason(dbgEventReason reason)
        {
            return reason != dbgEventReason.dbgEventReasonStep
                && reason != dbgEventReason.dbgEventReasonContextSwitch;
        }
    }
}
