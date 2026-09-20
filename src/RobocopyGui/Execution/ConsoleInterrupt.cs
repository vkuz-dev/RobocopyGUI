using System;
using System.Runtime.InteropServices;

namespace RobocopyGui.Execution
{
    /// <summary>
    /// Sends Ctrl+C to a console process that runs without a visible window, which is
    /// the gentlest way to ask robocopy to stop. This GUI process attaches to the
    /// child's hidden console, ignores the event itself, raises it, and detaches.
    /// </summary>
    internal static class ConsoleInterrupt
    {
        private const uint CtrlCEvent = 0;

        private static bool _ignoring;

        public static bool SendCtrlC(int processId)
        {
            FreeConsole();
            if (!AttachConsole((uint)processId))
                return false;

            try
            {
                // Must stay in effect until the event has been delivered, otherwise this
                // process would receive the Ctrl+C too. Undone by Restore() once the
                // child has exited.
                SetConsoleCtrlHandler(IntPtr.Zero, true);
                _ignoring = true;
                return GenerateConsoleCtrlEvent(CtrlCEvent, 0);
            }
            finally
            {
                FreeConsole();
            }
        }

        /// <summary>
        /// Re-enables normal Ctrl+C handling. Call after the child has exited: the
        /// "ignore Ctrl+C" state is inherited by processes started later, and a
        /// robocopy that ignores Ctrl+C could not be stopped gracefully.
        /// </summary>
        public static void Restore()
        {
            if (!_ignoring)
                return;
            SetConsoleCtrlHandler(IntPtr.Zero, false);
            _ignoring = false;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleCtrlHandler(IntPtr handlerRoutine, bool add);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);
    }
}
