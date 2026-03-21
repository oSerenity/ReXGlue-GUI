using System;
using System.IO;

namespace ReXGlue_REVS
{
    internal static class CMakeWorkspaceHelper
    {
        private static readonly string[] PathFragmentsToSkip = new[]
        {
            "\\out\\", "\\.vs\\", "\\cmakefiles\\", "\\_deps\\", "\\.cache\\",
            "\\third_party\\", "\\vcpkg\\", "\\node_modules\\"
        };

        /// <summary>Count CMakeLists.txt under the solution tree, excluding typical build/cache trees.</summary>
        public static int CountCMakeListsExcludingBuildDirs(string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory)) return 0;
            int count = 0;
            try
            {
                foreach (string file in Directory.EnumerateFiles(rootDirectory, "CMakeLists.txt", SearchOption.AllDirectories))
                {
                    string lower = file.ToLowerInvariant();
                    bool skip = false;
                    foreach (string frag in PathFragmentsToSkip)
                    {
                        if (lower.Contains(frag)) { skip = true; break; }
                    }
                    if (!skip) count++;
                }
            }
            catch { }
            return count;
        }

        /// <summary>Best-effort remove rexglue-style preset output folder after migrate.</summary>
        public static bool TryDeleteOutFolder(string projectRoot, out string errorMessage)
        {
            errorMessage = null;
            if (string.IsNullOrWhiteSpace(projectRoot)) return false;
            string outDir = Path.Combine(projectRoot, "out");
            if (!Directory.Exists(outDir)) return false;
            try
            {
                Directory.Delete(outDir, true);
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

    }
}
