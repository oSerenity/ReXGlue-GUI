using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ReXGlue_REVS
{
    internal static class TomlFunctions
    {
        public static List<string> NormalizedLines(string text)
        {
            return text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
        }

        public static Tuple<int, int> FindFunctionsSection(IReadOnlyList<string> lines)
        {
            int start = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Trim().Equals("[functions]", StringComparison.OrdinalIgnoreCase))
                {
                    start = i;
                    break;
                }
            }
            if (start < 0) return Tuple.Create(-1, -1);

            int end = lines.Count;
            for (int i = start + 1; i < lines.Count; i++)
            {
                string s = lines[i].Trim();
                if (s.StartsWith("[", StringComparison.Ordinal) && !s.StartsWith("#", StringComparison.Ordinal))
                {
                    end = i;
                    break;
                }
            }
            return Tuple.Create(start, end);
        }

        public static int CountFunctionsInSection(IReadOnlyList<string> lines)
        {
            var se = FindFunctionsSection(lines);
            if (se.Item1 < 0) return 0;
            int count = 0;
            for (int i = se.Item1 + 1; i < se.Item2; i++)
            {
                if (lines[i].Trim().StartsWith("0x", StringComparison.OrdinalIgnoreCase)) count++;
            }
            return count;
        }

        /// <summary>Index at which to insert [functions] so it appears under longjmp_address. Returns list.Count if not found.</summary>
        private static int IndexAfterLongjmp(IReadOnlyList<string> lines)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                string t = lines[i].Trim();
                if (t.StartsWith("longjmp_address", StringComparison.OrdinalIgnoreCase))
                    return i + 1;
            }
            return lines.Count;
        }

        /// <returns>Item1 = inserted count, Item2 = skipped (already present), Item3 = addresses actually inserted.</returns>
        public static Tuple<int, int, List<string>> InjectAddresses(
            List<string> tomlLines,
            IEnumerable<string> addresses,
            Func<string, string> commentForAddress = null)
        {
            int headerIdx = tomlLines.FindIndex(l => l.Trim().Equals("[functions]", StringComparison.OrdinalIgnoreCase));
            if (headerIdx < 0)
            {
                int insertAt = IndexAfterLongjmp(tomlLines);
                tomlLines.Insert(insertAt, string.Empty);
                tomlLines.Insert(insertAt + 1, "[functions]");
                headerIdx = insertAt + 1;
            }

            var se = FindFunctionsSection(tomlLines);
            int funcEnd = se.Item2;
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = headerIdx + 1; i < funcEnd; i++)
            {
                string s = tomlLines[i].Trim();
                if (!s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) continue;
                int eq = s.IndexOf('=');
                existing.Add(eq >= 0 ? s.Substring(0, eq).Trim() : s);
            }

            int inserted = 0, skipped = 0, nextInsertIdx = headerIdx + 1;
            var insertedAddrs = new List<string>();
            foreach (string addr in addresses)
            {
                if (existing.Add(addr))
                {
                    string c = commentForAddress != null ? commentForAddress(addr) : null;
                    string entry = c != null ? addr + " = {}  " + c : addr + " = {}";
                    tomlLines.Insert(nextInsertIdx++, entry);
                    insertedAddrs.Add(addr);
                    inserted++;
                }
                else skipped++;
            }
            return Tuple.Create(inserted, skipped, insertedAddrs);
        }

        private static readonly Regex RxCtxCtrEndComment = new Regex(@"\s+#\s*ctx\.ctr\.u32\s*\[\s*\d+\s*\]\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>Remove trailing <c># ctx.ctr.u32[n]</c> comments (Fetch/Auto inject markers) from every line.</summary>
        public static Tuple<List<string>, int> RemoveCtxCtrInjectComments(List<string> lines)
        {
            var result = new List<string>(lines.Count);
            int removed = 0;
            foreach (string line in lines)
            {
                string next = RxCtxCtrEndComment.Replace(line, "");
                if (!string.Equals(next, line, StringComparison.Ordinal)) removed++;
                result.Add(next);
            }
            return Tuple.Create(result, removed);
        }

        public static Tuple<List<string>, int> RemoveDuplicateFunctionAddresses(List<string> lines)
        {
            var se = FindFunctionsSection(lines);
            if (se.Item1 < 0) return Tuple.Create(lines, 0);
            int funcStart = se.Item1, funcEnd = se.Item2;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var newLines = new List<string>(lines.Count);
            int removed = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                if (i > funcStart && i < funcEnd && line.TrimStart().StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    int eq = line.IndexOf('=');
                    string key = (eq >= 0 ? line.Substring(0, eq).Trim() : line.Trim()).ToLowerInvariant();
                    if (!seen.Add(key)) { removed++; continue; }
                }
                newLines.Add(line);
            }
            return Tuple.Create(newLines, removed);
        }

        /// <summary>Append setjmp_address and longjmp_address if not present. Returns (newLines, added).</summary>
        public static Tuple<List<string>, bool> AddSetjmpLongjmp(List<string> lines)
        {
            string joined = string.Join("\n", lines);
            if (joined.IndexOf("setjmp_address", StringComparison.OrdinalIgnoreCase) >= 0 ||
                joined.IndexOf("longjmp_address", StringComparison.OrdinalIgnoreCase) >= 0)
                return Tuple.Create(lines, false);
            var list = new List<string>(lines);
            if (list.Count > 0 && !string.IsNullOrWhiteSpace(list[list.Count - 1]))
                list.Add(string.Empty);
            list.Add("setjmp_address  = 0x00000000");
            list.Add("longjmp_address = 0x00000000");
            return Tuple.Create(list, true);
        }

        /// <summary>Insert [functions] if not present, immediately after longjmp_address (or at end if no longjmp line).</summary>
        public static Tuple<List<string>, bool> EnsureFunctionsSection(List<string> lines)
        {
            if (lines.Any(l => l.Trim().Equals("[functions]", StringComparison.OrdinalIgnoreCase)))
                return Tuple.Create(lines, false);
            var list = new List<string>(lines);
            int insertAt = IndexAfterLongjmp(list);
            list.Insert(insertAt, string.Empty);
            list.Insert(insertAt + 1, "[functions]");
            return Tuple.Create(list, true);
        }

        /// <summary>Append [rexcrt] if not present. Returns (newLines, true if inserted).</summary>
        public static Tuple<List<string>, bool> EnsureRexcrtSection(List<string> lines)
        {
            if (lines.Any(l => l.Trim().Equals("[rexcrt]", StringComparison.OrdinalIgnoreCase)))
                return Tuple.Create(lines, false);
            var list = new List<string>(lines);
            if (list.Count > 0 && !string.IsNullOrWhiteSpace(list[list.Count - 1]))
                list.Add(string.Empty);
            list.Add("[rexcrt]");
            return Tuple.Create(list, true);
        }

        /// <summary>Clear name/parent/size in [functions] entries. Returns (newLines, clearedCount).</summary>
        public static Tuple<List<string>, int> ClearValuesInFunctions(List<string> lines, bool clearName, bool clearParent, bool clearSize)
        {
            var se = FindFunctionsSection(lines);
            if (se.Item1 < 0) return Tuple.Create(lines, 0);
            int funcStart = se.Item1, funcEnd = se.Item2;
            var newLines = new List<string>(lines.Count);
            int cleared = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                if (i > funcStart && i < funcEnd && line.TrimStart().StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    int eq = line.IndexOf('=');
                    if (eq >= 0)
                    {
                        string addr = line.Substring(0, eq).Trim();
                        string valStr = line.Substring(eq + 1).Trim();
                        if (clearName && clearParent && clearSize) { newLines.Add(addr + " = {}"); cleared++; continue; }
                        var inner = ParseBraceFields(valStr);
                        bool changed = (clearName && inner.Remove("name")) | (clearParent && inner.Remove("parent")) | (clearSize && inner.Remove("size"));
                        if (changed) { newLines.Add(addr + " = " + RebuildBraceFields(inner)); cleared++; continue; }
                    }
                }
                newLines.Add(line);
            }
            return Tuple.Create(newLines, cleared);
        }

        private static Dictionary<string, string> ParseBraceFields(string val)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string t = (val ?? "").Trim().TrimStart('{').TrimEnd('}').Trim();
            if (string.IsNullOrWhiteSpace(t)) return result;
            foreach (string pair in t.Split(','))
            {
                var kv = pair.Split(new[] { '=' }, 2);
                string k = kv.Length >= 1 ? kv[0].Trim() : "";
                string v = kv.Length == 2 ? kv[1].Trim() : "";
                if (!string.IsNullOrWhiteSpace(k)) result[k] = v;
            }
            return result;
        }

        private static string RebuildBraceFields(Dictionary<string, string> fields)
        {
            if (fields == null || fields.Count == 0) return "{}";
            return "{ " + string.Join(", ", fields.Select(kv => string.IsNullOrEmpty(kv.Value) ? kv.Key : kv.Key + " = " + kv.Value)) + " }";
        }
    }

    /// <summary>Parse " from 0x..." lines and output ADDR = {} lines.</summary>
    internal static class AddressParserHelper
    {
        public static string Parse(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var parsed = new List<string>();
            foreach (string rawLine in input.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int fromIdx = rawLine.IndexOf(" from ", StringComparison.OrdinalIgnoreCase);
                if (fromIdx < 0) continue;
                string part = rawLine.Substring(0, fromIdx).Replace("[", " ").Replace("]", " ");
                var tokens = part.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 0) continue;
                string addr = tokens[tokens.Length - 1].Trim();
                if (addr.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && addr.Length > 2 && seen.Add(addr))
                    parsed.Add(addr);
            }
            return string.Join(Environment.NewLine, parsed.Select(a => a + " = {}"));
        }
    }
}
