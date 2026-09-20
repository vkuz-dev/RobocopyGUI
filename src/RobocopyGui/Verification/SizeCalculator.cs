using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Threading;

namespace RobocopyGui.Verification
{
    internal sealed class SizeResult
    {
        public long Bytes { get; set; }

        public long Files { get; set; }

        /// <summary>Directories that could not be listed (access denied, vanished, ...).</summary>
        public int UnreadableDirectories { get; set; }
    }

    /// <summary>
    /// Enumerates a directory tree once and sums file sizes in exact bytes,
    /// applying the same <see cref="CopyScope"/> rules to source and destination.
    /// </summary>
    internal static class SizeCalculator
    {
        private const int ProgressInterval = 2000;

        private sealed class PendingDirectory
        {
            public string FullPath;
            public string RelativePath;
            public int Level;
        }

        public static SizeResult Calculate(string root, CopyScope scope, CancellationToken cancellation, IProgress<SizeResult> progress)
        {
            string extendedRoot = ToExtendedPath(root);
            if (!Directory.Exists(extendedRoot))
                throw new DirectoryNotFoundException("Folder not found or not accessible: " + root);

            var result = new SizeResult();
            var pending = new Stack<PendingDirectory>();
            pending.Push(new PendingDirectory { FullPath = extendedRoot, RelativePath = string.Empty, Level = 1 });

            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                try
                {
                    foreach (var entry in new DirectoryInfo(directory.FullPath).EnumerateFileSystemInfos())
                    {
                        cancellation.ThrowIfCancellationRequested();

                        string relative = directory.RelativePath.Length == 0 ? entry.Name : directory.RelativePath + "\\" + entry.Name;
                        bool isReparsePoint = (entry.Attributes & FileAttributes.ReparsePoint) != 0;

                        var subdirectory = entry as DirectoryInfo;
                        if (subdirectory != null)
                        {
                            if (directory.Level >= scope.MaxLevels
                                || (isReparsePoint && scope.ExcludeJunctionDirectories)
                                || scope.IsDirectoryExcluded(entry.Name, relative))
                                continue;

                            pending.Push(new PendingDirectory { FullPath = subdirectory.FullName, RelativePath = relative, Level = directory.Level + 1 });
                            continue;
                        }

                        if ((isReparsePoint && scope.ExcludeSymbolicLinkFiles) || !scope.IsFileIncluded(entry.Name, relative))
                            continue;

                        result.Bytes += ((FileInfo)entry).Length;
                        result.Files++;
                        if (progress != null && result.Files % ProgressInterval == 0)
                            progress.Report(new SizeResult { Bytes = result.Bytes, Files = result.Files, UnreadableDirectories = result.UnreadableDirectories });
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException)
                {
                    if (directory.Level == 1)
                        throw;
                    result.UnreadableDirectories++;
                }
            }

            return result;
        }

        /// <summary>Adds the \\?\ prefix so that paths longer than MAX_PATH can be enumerated.</summary>
        private static string ToExtendedPath(string path)
        {
            string full = Path.GetFullPath(path.Trim());
            if (full.StartsWith(@"\\?\", StringComparison.Ordinal))
                return full;
            if (full.StartsWith(@"\\", StringComparison.Ordinal))
                return @"\\?\UNC\" + full.Substring(2);
            return @"\\?\" + full;
        }
    }
}
