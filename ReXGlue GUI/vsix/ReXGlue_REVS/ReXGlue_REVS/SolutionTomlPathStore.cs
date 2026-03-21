using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace ReXGlue_REVS
{
    internal static class SolutionTomlPathStore
    {
        private const string FolderName = "ReXGlue";
        private const string FileName = "toml_path.txt";

        public static async Task<string> LoadAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            string file = await GetSettingsFilePathAsync(package);
            if (string.IsNullOrEmpty(file) || !File.Exists(file)) return null;
            try
            {
                string s = File.ReadAllText(file).Trim();
                return string.IsNullOrWhiteSpace(s) ? null : s;
            }
            catch { return null; }
        }

        public static async Task<bool> SaveAsync(AsyncPackage package, string tomlPath)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            string file = await GetSettingsFilePathAsync(package);
            if (string.IsNullOrEmpty(file)) return false;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file, tomlPath.Trim());
                return true;
            }
            catch { return false; }
        }

        private static async Task<string> GetSettingsFilePathAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var sol = await package.GetServiceAsync(typeof(SVsSolution)) as IVsSolution;
            if (sol == null) return null;
            string solutionDir, solutionFile, userOpts;
            sol.GetSolutionInfo(out solutionDir, out solutionFile, out userOpts);
            if (string.IsNullOrWhiteSpace(solutionDir)) return null;
            return Path.Combine(solutionDir, ".vs", FolderName, FileName);
        }
    }
}
