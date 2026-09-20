using System;
using System.IO;
using System.Linq;

namespace RobocopyGui
{
    /// <summary>Lists the immediate child folders of the source root (never recursive).</summary>
    internal static class FolderScanner
    {
        public static string[] GetChildFolders(string root)
        {
            return new DirectoryInfo(root.Trim())
                .EnumerateDirectories()
                .Select(d => d.Name)
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
    }
}
