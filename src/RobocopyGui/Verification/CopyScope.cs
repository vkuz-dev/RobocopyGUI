using System;
using System.Collections.Generic;
using System.Linq;
using RobocopyGui.Command;

namespace RobocopyGui.Verification
{
    /// <summary>
    /// The subset of robocopy selection rules that size verification applies to both
    /// source and destination: recursion (/S /E /MIR /LEV), file specs and /IF,
    /// /XF, /XD and junction exclusion (/XJ /XJD /XJF). Other selection switches
    /// (/MAX, /XA, /XO, ...) are not modelled; verification is a sanity check only.
    /// </summary>
    internal sealed class CopyScope
    {
        private readonly List<string> _includeNames = new List<string>();
        private readonly List<string> _excludeFileNames = new List<string>();
        private readonly List<string> _excludeFilePaths = new List<string>();
        private readonly List<string> _excludeDirNames = new List<string>();
        private readonly List<string> _excludeDirPaths = new List<string>();

        public string SourceRoot { get; private set; }

        public string DestinationRoot { get; private set; }

        /// <summary>Number of directory levels to include; the root is level 1.</summary>
        public int MaxLevels { get; private set; }

        public bool ExcludeJunctionDirectories { get; private set; }

        public bool ExcludeSymbolicLinkFiles { get; private set; }

        public static CopyScope FromCommand(RobocopyCommand command)
        {
            var scope = new CopyScope
            {
                SourceRoot = command.Source ?? string.Empty,
                DestinationRoot = command.Destination ?? string.Empty,
            };

            bool recursive = command.HasSwitch("/S") || command.HasSwitch("/E") || command.HasSwitch("/MIR");
            int? levels = command.GetIntArgument("/LEV");
            scope.MaxLevels = !recursive ? 1 : levels.HasValue && levels.Value > 0 ? levels.Value : int.MaxValue;

            scope._includeNames.AddRange(command.FileSpecs.Concat(command.GetValues("/IF")));

            foreach (string pattern in command.GetValues("/XF"))
                scope.AddPattern(pattern, scope._excludeFileNames, scope._excludeFilePaths);
            foreach (string pattern in command.GetValues("/XD"))
                scope.AddPattern(pattern, scope._excludeDirNames, scope._excludeDirPaths);

            scope.ExcludeJunctionDirectories = command.HasSwitch("/XJ") || command.HasSwitch("/XJD");
            scope.ExcludeSymbolicLinkFiles = command.HasSwitch("/XJ") || command.HasSwitch("/XJF");
            return scope;
        }

        /// <param name="name">Directory name.</param>
        /// <param name="relativePath">Path relative to the enumerated root, e.g. "Finance\2024".</param>
        public bool IsDirectoryExcluded(string name, string relativePath)
        {
            return _excludeDirNames.Any(p => Wildcard.IsMatch(p, name))
                || _excludeDirPaths.Any(p => Wildcard.IsMatch(p, relativePath));
        }

        public bool IsFileIncluded(string name, string relativePath)
        {
            if (_includeNames.Count > 0 && !_includeNames.Any(p => Wildcard.IsMatch(p, name)))
                return false;
            return !_excludeFileNames.Any(p => Wildcard.IsMatch(p, name))
                && !_excludeFilePaths.Any(p => Wildcard.IsMatch(p, relativePath));
        }

        /// <summary>
        /// Plain names match anywhere in the tree. Paths are made relative to the source
        /// (or destination) root so the same rule applies on both sides.
        /// </summary>
        private void AddPattern(string pattern, List<string> names, List<string> relativePaths)
        {
            pattern = pattern.Trim().TrimEnd('\\');
            if (pattern.Length == 0)
                return;

            if (pattern.IndexOf('\\') < 0)
            {
                names.Add(pattern);
                return;
            }

            string relative = MakeRelative(pattern, SourceRoot) ?? MakeRelative(pattern, DestinationRoot);
            if (relative != null)
                relativePaths.Add(relative);
            else if (!IsRooted(pattern))
                relativePaths.Add(pattern);
            // A rooted path outside both roots can never match.
        }

        private static string MakeRelative(string path, string root)
        {
            if (string.IsNullOrWhiteSpace(root))
                return null;
            string prefix = root.Trim().TrimEnd('\\') + "\\";
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && path.Length > prefix.Length
                ? path.Substring(prefix.Length)
                : null;
        }

        private static bool IsRooted(string path)
        {
            return path.StartsWith("\\", StringComparison.Ordinal) || (path.Length >= 2 && path[1] == ':');
        }
    }

    /// <summary>Case-insensitive '*' and '?' matching, as used by robocopy file and directory filters.</summary>
    internal static class Wildcard
    {
        public static bool IsMatch(string pattern, string text)
        {
            if (pattern == "*" || pattern == "*.*")
                return true;

            int p = 0, t = 0, star = -1, mark = 0;
            while (t < text.Length)
            {
                if (p < pattern.Length && (pattern[p] == '?' || char.ToUpperInvariant(pattern[p]) == char.ToUpperInvariant(text[t])))
                {
                    p++;
                    t++;
                }
                else if (p < pattern.Length && pattern[p] == '*')
                {
                    star = p++;
                    mark = t;
                }
                else if (star >= 0)
                {
                    p = star + 1;
                    t = ++mark;
                }
                else
                {
                    return false;
                }
            }

            while (p < pattern.Length && pattern[p] == '*')
                p++;
            return p == pattern.Length;
        }
    }
}
