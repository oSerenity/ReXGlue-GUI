using System;

namespace ReXGlue_REVS
{
    internal static class ReXGlueConstants
    {
        internal static readonly Guid OutputPaneGuid = new Guid("f99b52d0-98a9-4b9c-9f5f-4c8d36a3f129");

        internal static readonly string[] CMakeDeleteCacheCommands =
        {
            "CMake.DeleteCacheAndReconfigure",
            "CMake.Cache.DeleteAndReconfigure",
            "Project.CMake.DeleteCacheAndReconfigure"
        };

        internal const string WikiRoot = "https://github.com/rexglue/rexglue-sdk/wiki";
    }
}
