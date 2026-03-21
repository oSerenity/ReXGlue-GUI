using System;
using System.IO;
using System.Linq;
using System.Text;

namespace ReXGlue_REVS
{
    internal static class InitTomlPath
    {
        /// <summary>Normalize app name to snake_case file prefix (matches rexglue init output: foobar_config.toml).</summary>
        public static string AppNameToSnakePrefix(string appName)
        {
            if (string.IsNullOrWhiteSpace(appName)) return "";
            var sb = new StringBuilder(appName.Trim().ToLowerInvariant());
            for (int i = 0; i < sb.Length; i++)
            {
                if (sb[i] == ' ' || sb[i] == '-') sb[i] = '_';
            }
            return sb.ToString().Trim('_');
        }

        /// <summary>Pick codegen TOML after init: &lt;snake&gt;_config.toml, else any *_config.toml, else rexglue.toml.</summary>
        public static string ResolveAfterInit(string projectRoot, string appName)
        {
            if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot)) return null;
            string snake = AppNameToSnakePrefix(appName);
            string primary = string.IsNullOrEmpty(snake) ? null : Path.Combine(projectRoot, snake + "_config.toml");
            if (primary != null && File.Exists(primary)) return primary;
            try
            {
                var configs = Directory.GetFiles(projectRoot, "*_config.toml", SearchOption.TopDirectoryOnly);
                if (configs.Length > 0)
                    return configs.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).First();
                string legacy = Path.Combine(projectRoot, "rexglue.toml");
                if (File.Exists(legacy)) return legacy;
            }
            catch { }
            return primary ?? Path.Combine(projectRoot, (snake ?? "app") + "_config.toml");
        }
    }
}
