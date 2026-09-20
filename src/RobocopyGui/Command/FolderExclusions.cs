using System;
using System.Collections.Generic;
using System.Linq;

namespace RobocopyGui.Command
{
    /// <summary>
    /// Maps the source-folder checkboxes to /XD entries. The GUI owns only /XD values
    /// that are full paths of immediate children of the source root and that appear in
    /// the scanned folder list; every other /XD value (names like $RECYCLE.BIN, deeper
    /// paths, folders that no longer exist) is left alone.
    /// </summary>
    internal static class FolderExclusions
    {
        public const string Switch = "/XD";

        public static string MakeChildPath(string root, string name)
        {
            return Normalize(root) + "\\" + name;
        }

        /// <summary>Returns the child folder name if <paramref name="path"/> is an immediate child of <paramref name="root"/>, otherwise null.</summary>
        public static string GetChildName(string path, string root)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root))
                return null;

            string normalized = Normalize(path);
            int separator = normalized.LastIndexOf('\\');
            if (separator <= 0 || separator == normalized.Length - 1)
                return null;

            string parent = normalized.Substring(0, separator);
            return string.Equals(parent, Normalize(root), StringComparison.OrdinalIgnoreCase)
                ? normalized.Substring(separator + 1)
                : null;
        }

        /// <summary>Names of immediate child folders of <paramref name="root"/> excluded by /XD full paths.</summary>
        public static HashSet<string> GetExcludedChildren(RobocopyCommand command, string root)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string value in command.GetValues(Switch))
            {
                string name = GetChildName(value, root);
                if (name != null)
                    result.Add(name);
            }
            return result;
        }

        /// <summary>
        /// Rewrites the /XD entries for the listed child folders so that exactly
        /// <paramref name="excludedNames"/> are excluded.
        /// </summary>
        public static void SetExcludedChildren(RobocopyCommand command, string root,
            IEnumerable<string> listedNames, IEnumerable<string> excludedNames)
        {
            var listed = new HashSet<string>(listedNames, StringComparer.OrdinalIgnoreCase);
            var excluded = excludedNames.Where(listed.Contains).ToList();

            // Keep the existing spelling of entries that stay excluded.
            var existing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string value in command.GetValues(Switch))
            {
                string name = GetChildName(value, root);
                if (name != null && !existing.ContainsKey(name))
                    existing[name] = value;
            }

            var values = excluded.Select(n => existing.ContainsKey(n) ? existing[n] : MakeChildPath(root, n)).ToList();

            command.SetValues(Switch, v =>
            {
                string name = GetChildName(v, root);
                return name != null && listed.Contains(name);
            }, values);
        }

        /// <summary>Removes every /XD entry that points at an immediate child of <paramref name="root"/>.</summary>
        public static void RemoveAllChildren(RobocopyCommand command, string root)
        {
            command.SetValues(Switch, v => GetChildName(v, root) != null, Enumerable.Empty<string>());
        }

        private static string Normalize(string path)
        {
            string trimmed = path.Trim().TrimEnd('\\');
            return trimmed.Length == 0 ? path.Trim() : trimmed;
        }
    }
}
