using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.Win32;

namespace ReXGlue_REVS
{
    internal sealed class CodegenOptions
    {
        public bool Force { get; set; }
        public bool EnableExceptionHandlers { get; set; }
        public string LogLevel { get; set; }

        internal string BuildCodegenArgumentSuffix()
        {
            string s = "";
            if (Force) s += " --force";
            if (EnableExceptionHandlers) s += " --enable_exception_handlers";
            if (!string.IsNullOrWhiteSpace(LogLevel))
                s += " --log_level \"" + LogLevel.Trim().Replace("\"", "") + "\"";
            return s;
        }
    }

    internal static class RexglueRunner
    {
        private const string RegPath = @"Software\Kitware\CMake\Packages\rexglue";

        internal static string ResolveRexglueExe()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegPath))
                {
                    if (key != null)
                    {
                        string dir = null;
                        foreach (string name in new[] { "", "Location", "Path", "Directory" })
                        {
                            if (key.GetValue(name) is string s && !string.IsNullOrWhiteSpace(s))
                            {
                                dir = s.Trim();
                                break;
                            }
                        }
                        if (!string.IsNullOrEmpty(dir))
                        {
                            string exe = dir.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? dir : Path.Combine(dir, "rexglue.exe");
                            if (File.Exists(exe)) return exe;
                            string binExe = Path.Combine(dir, "bin", "rexglue.exe");
                            if (File.Exists(binExe)) return binExe;
                        }
                    }
                }
            }
            catch { }

            try
            {
                foreach (string part in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string exe = Path.Combine(part.Trim(), "rexglue.exe");
                    if (File.Exists(exe)) return exe;
                }
            }
            catch { }

            return "rexglue";
        }

        private static bool IsFullPath(string exe)
        {
            return !string.IsNullOrEmpty(exe) &&
                   (exe.IndexOf(Path.DirectorySeparatorChar) >= 0 ||
                    exe.IndexOf(Path.AltDirectorySeparatorChar) >= 0 ||
                    (exe.Length >= 2 && exe[1] == ':'));
        }

        private static string Q(string path) => (path ?? "").Replace("\"", "\\\"");

        /// <summary>Runs rexglue with given argument string (e.g. codegen "x.toml" --force).</summary>
        private static async Task<int> RunRexglueAsync(string workingDirectory, string rexglueArgs, string userVisibleCommandLine)
        {
            string exe = ResolveRexglueExe();
            bool direct = IsFullPath(exe);
            await Commands.WriteOutputAsync(userVisibleCommandLine);

            var psi = new ProcessStartInfo
            {
                FileName = direct ? exe : "cmd.exe",
                Arguments = direct ? rexglueArgs : "/c rexglue " + rexglueArgs,
                WorkingDirectory = workingDirectory ?? "",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (var proc = new Process { StartInfo = psi, EnableRaisingEvents = true })
            {
                var tcs = new TaskCompletionSource<int>();
                proc.OutputDataReceived += (_, e) =>
                {
                    if (e.Data != null) _ = Task.Run(() => Commands.WriteOutputAsync(e.Data));
                };
                proc.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data != null) _ = Task.Run(() => Commands.WriteOutputAsync("[ERR] " + e.Data));
                };
                proc.Exited += (_, __) => tcs.TrySetResult(proc.ExitCode);

                try
                {
                    if (!proc.Start())
                    {
                        await Commands.WriteOutputAsync("[ReXGlue] Failed to start rexglue.");
                        return -1;
                    }
                }
                catch (Exception ex)
                {
                    await Commands.WriteOutputAsync("[ReXGlue] Failed to start rexglue: " + ex.Message);
                    return -1;
                }

                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                return await tcs.Task.ConfigureAwait(false);
            }
        }

        public static async Task<int> RunCodegenAsync(AsyncPackage package, string tomlPath, CodegenOptions opts = null)
        {
            opts = opts ?? new CodegenOptions();
            string workDir = Path.GetDirectoryName(tomlPath) ?? "";
            string args = "codegen \"" + Q(tomlPath) + "\"" + opts.BuildCodegenArgumentSuffix();
            string exe = ResolveRexglueExe();
            string echo = (IsFullPath(exe) ? "\"" + exe + "\" " : "rexglue ") + args;
            int exit = await RunRexglueAsync(workDir, args, "[ReXGlue] " + echo);
            await Commands.WriteOutputAsync(string.Format("[{0:HH:mm:ss}] [Run Code Generation] Done. ExitCode: {1}", DateTime.Now, exit));
            return exit;
        }

        public static async Task<bool> RunInitAsync(string rootFolder, string appName)
        {
            if (string.IsNullOrWhiteSpace(rootFolder) || string.IsNullOrWhiteSpace(appName))
                return false;
            string fullPath = Path.Combine(rootFolder, appName);
            try { Directory.CreateDirectory(fullPath); } catch { }

            string args = "init --app_name \"" + Q(appName) + "\" --app_root \"" + Q(fullPath) + "\"";
            string exe = ResolveRexglueExe();
            string echo = "[Initialize Project]\n  Command: " + (IsFullPath(exe) ? "\"" + exe + "\" " : "rexglue ") + args;
            int exit = await RunRexglueAsync(rootFolder, args, echo);

            await Commands.WriteOutputAsync("[Initialize Project] ExitCode: " + exit);
            try { Directory.CreateDirectory(Path.Combine(fullPath, "assets")); } catch { }

            if (exit == 0)
            {
                try
                {
                    WriteLaunchVsJson(fullPath, appName);
                    await Commands.WriteOutputAsync("[Initialize Project] Created .vs/launch.vs.json for " + AppNameToExeName(appName));
                }
                catch (Exception ex) { await Commands.WriteOutputAsync("[Launch config] " + ex.Message); }
            }

            return exit == 0;
        }

        private static string AppNameToExeName(string appName)
        {
            if (string.IsNullOrWhiteSpace(appName)) return "app.exe";
            var sb = new System.Text.StringBuilder(appName.Trim().ToLowerInvariant());
            for (int i = 0; i < sb.Length; i++)
                if (sb[i] == ' ' || sb[i] == '-') sb[i] = '_';
            if (sb.Length == 0) return "app.exe";
            return sb.ToString().TrimEnd('_') + ".exe";
        }

        private static void WriteLaunchVsJson(string projectRoot, string appName)
        {
            string vsDir = Path.Combine(projectRoot, ".vs");
            Directory.CreateDirectory(vsDir);
            string exeName = AppNameToExeName(appName);
            string assetsPathEscaped = Path.Combine(projectRoot, "assets").Replace("\\", "\\\\");
            string json =
                "{\n" +
                "  \"version\": \"0.2.1\",\n" +
                "  \"defaults\": {},\n" +
                "  \"configurations\": [\n" +
                "    {\n" +
                "      \"type\": \"default\",\n" +
                "      \"project\": \"CMakeLists.txt\",\n" +
                "      \"projectTarget\": \"" + exeName + "\",\n" +
                "      \"name\": \"" + exeName + "\",\n" +
                "      \"args\": [\n" +
                "        \"" + assetsPathEscaped + "\",\n" +
                "        \"--enable_console=true\",\n" +
                "        \"--log_level=debug\",\n" +
                "        \"--vsync=false\",\n" +
                "        \"--log_file=logs/debug.log\"\n" +
                "      ]\n" +
                "    }\n" +
                "  ]\n" +
                "}\n";
            File.WriteAllText(Path.Combine(vsDir, "launch.vs.json"), json);
        }

        public static async Task<int> RunMigrateAsync(string appRoot, bool force)
        {
            if (string.IsNullOrWhiteSpace(appRoot) || !Directory.Exists(appRoot))
            {
                await Commands.WriteOutputAsync("[ReXGlue] migrate: invalid app root.");
                return -1;
            }
            string args = "migrate --app_root \"" + Q(appRoot) + "\"" + (force ? " --force" : "");
            string exe = ResolveRexglueExe();
            string echo = "[Migrate]\n  " + (IsFullPath(exe) ? "\"" + exe + "\" " : "rexglue ") + args;
            int exit = await RunRexglueAsync(appRoot, args, echo);
            await Commands.WriteOutputAsync("[Migrate] ExitCode: " + exit);
            return exit;
        }
    }
}
