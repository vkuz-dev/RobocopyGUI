using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RobocopyGui.Execution
{
    /// <summary>
    /// Runs robocopy.exe as a hidden child process and collects its native output.
    /// Output is queued from background threads and drained by the UI.
    /// </summary>
    internal sealed class RobocopyRunner : IDisposable
    {
        private readonly ConcurrentQueue<string> _output = new ConcurrentQueue<string>();
        private Process _process;

        public Task<int> Completion { get; private set; }

        public int ProcessId { get; private set; }

        /// <summary>Resolves the robocopy executable named in the command.</summary>
        public static string ResolveExecutable(string executable)
        {
            if (!string.IsNullOrEmpty(executable) && executable.IndexOf('\\') >= 0)
                return Path.HasExtension(executable) ? executable : executable + ".exe";

            string systemCopy = Path.Combine(Environment.SystemDirectory, "robocopy.exe");
            return File.Exists(systemCopy) ? systemCopy : "robocopy.exe";
        }

        public void Start(string executable, string arguments)
        {
            // Redirected robocopy output is written in the OEM code page.
            var encoding = Encoding.GetEncoding(GetOEMCP());

            var startInfo = new ProcessStartInfo(ResolveExecutable(executable), arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = false,
                StandardOutputEncoding = encoding,
                StandardErrorEncoding = encoding,
            };

            _process = Process.Start(startInfo);
            ProcessId = _process.Id;

            var stdout = StartPump(_process.StandardOutput);
            var stderr = StartPump(_process.StandardError);
            var process = _process;

            Completion = Task.Factory.StartNew(() =>
            {
                process.WaitForExit();
                stdout.Join();
                stderr.Join();
                return process.ExitCode;
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        /// <summary>Returns all output received since the last call (CRLF line endings).</summary>
        public string DrainOutput()
        {
            var sb = new StringBuilder();
            string chunk;
            while (_output.TryDequeue(out chunk))
                sb.Append(chunk);
            return sb.ToString();
        }

        /// <summary>Asks robocopy to stop by sending Ctrl+C to its hidden console.</summary>
        public bool RequestGracefulStop()
        {
            return IsRunning && ConsoleInterrupt.SendCtrlC(ProcessId);
        }

        /// <summary>Terminates robocopy immediately.</summary>
        public void Kill()
        {
            try
            {
                if (IsRunning)
                    _process.Kill();
            }
            catch (InvalidOperationException)
            {
                // Already exited.
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Exiting at this very moment.
            }
        }

        public bool IsRunning
        {
            get
            {
                try
                {
                    return _process != null && !_process.HasExited;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }
        }

        public void Dispose()
        {
            _process?.Dispose();
        }

        private Thread StartPump(StreamReader reader)
        {
            var thread = new Thread(() => Pump(reader)) { IsBackground = true, Name = "robocopy output" };
            thread.Start();
            return thread;
        }

        private void Pump(StreamReader reader)
        {
            var buffer = new char[8192];
            var normalizer = new LineEndingNormalizer();
            try
            {
                int read;
                while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    string text = normalizer.Normalize(buffer, read);
                    if (text.Length > 0)
                        _output.Enqueue(text);
                }
            }
            catch (IOException)
            {
                // Pipe broken because the process was terminated.
            }
            catch (ObjectDisposedException)
            {
            }

            string tail = normalizer.Flush();
            if (tail.Length > 0)
                _output.Enqueue(tail);
        }

        [DllImport("kernel32.dll")]
        private static extern int GetOEMCP();
    }

    /// <summary>
    /// Converts CR, LF and CRLF line endings to CRLF, including across chunk boundaries.
    /// </summary>
    internal sealed class LineEndingNormalizer
    {
        private bool _pendingCarriageReturn;

        public string Normalize(char[] buffer, int count)
        {
            var sb = new StringBuilder(count + 16);
            for (int i = 0; i < count; i++)
            {
                char c = buffer[i];
                if (_pendingCarriageReturn)
                {
                    _pendingCarriageReturn = false;
                    sb.Append("\r\n");
                    if (c == '\n')
                        continue;
                }

                if (c == '\r')
                    _pendingCarriageReturn = true;
                else if (c == '\n')
                    sb.Append("\r\n");
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }

        public string Flush()
        {
            if (!_pendingCarriageReturn)
                return string.Empty;
            _pendingCarriageReturn = false;
            return "\r\n";
        }
    }
}
