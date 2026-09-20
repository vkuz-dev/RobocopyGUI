using System.Collections.Generic;

namespace RobocopyGui.Execution
{
    internal enum ExitCodeKind
    {
        /// <summary>0-1: nothing to do, or files copied without problems.</summary>
        Success,

        /// <summary>2-7: completed; extra and/or mismatched items were detected.</summary>
        SuccessWithDifferences,

        /// <summary>8-15: some files or directories could not be copied.</summary>
        CopyErrors,

        /// <summary>16 and above: serious error, nothing was copied.</summary>
        SeriousError,

        /// <summary>The process was terminated (Ctrl+C, killed, crashed).</summary>
        Terminated,
    }

    /// <summary>
    /// Robocopy's documented exit code bit flags. Codes below 8 are successful runs.
    /// </summary>
    internal static class RobocopyExitCode
    {
        public const int ControlCExit = unchecked((int)0xC000013A);

        public static ExitCodeKind Classify(int code)
        {
            if (code < 0 || code > 31)
                return ExitCodeKind.Terminated;
            if (code >= 16)
                return ExitCodeKind.SeriousError;
            if (code >= 8)
                return ExitCodeKind.CopyErrors;
            if (code >= 2)
                return ExitCodeKind.SuccessWithDifferences;
            return ExitCodeKind.Success;
        }

        public static bool IsSuccess(int code)
        {
            var kind = Classify(code);
            return kind == ExitCodeKind.Success || kind == ExitCodeKind.SuccessWithDifferences;
        }

        public static string StatusText(int code)
        {
            switch (Classify(code))
            {
                case ExitCodeKind.Success: return "Completed successfully";
                case ExitCodeKind.SuccessWithDifferences: return "Completed with differences";
                case ExitCodeKind.CopyErrors: return "Completed with errors";
                case ExitCodeKind.SeriousError: return "Failed";
                default: return "Terminated";
            }
        }

        /// <summary>Human-readable explanation of an exit code, built from its bit flags.</summary>
        public static string Describe(int code)
        {
            if (code == ControlCExit)
                return "Robocopy was interrupted (Ctrl+C / 0xC000013A).";
            if (code < 0 || code > 31)
                return string.Format("Robocopy was terminated (0x{0:X8}).", code);
            if (code == 0)
                return "No files were copied and no failures occurred; source and destination are already in sync.";

            var parts = new List<string>();
            if ((code & 16) != 0)
                parts.Add("Serious error: Robocopy did not copy any files (usage error, or insufficient access to the source or destination).");
            if ((code & 8) != 0)
                parts.Add("Some files or directories could not be copied (copy errors occurred and the retry limit was exceeded).");
            if ((code & 4) != 0)
                parts.Add("Mismatched files or directories were detected.");
            if ((code & 2) != 0)
                parts.Add("Extra files or directories were detected in the destination.");
            if ((code & 1) != 0)
                parts.Add("One or more files were copied successfully.");
            else if (code < 8)
                parts.Add("No files were copied.");
            return string.Join(" ", parts);
        }
    }
}
