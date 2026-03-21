using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace ReXGlue_REVS
{
    [Guid("a8b04c52-1c5e-4a2d-9f3b-7e8d6c5a4b09")]
    public sealed class ReXGlueToolWindow : ToolWindowPane
    {
        public ReXGlueToolWindow() : base(null)
        {
            Caption = "ReXGlue";
            Content = new ReXGlueControl();
        }
    }
}
