using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace RobocopyGui.Execution
{
    /// <summary>
    /// One log file per execution: robocopy_yyyy-MM-dd_HHmmss.log in the chosen folder.
    /// Contains the native robocopy output plus a few clearly marked GUI lines.
    /// </summary>
    internal sealed class ExecutionLog : IDisposable
    {
        private readonly StreamWriter _writer;

        private ExecutionLog(string path, StreamWriter writer)
        {
            Path = path;
            _writer = writer;
        }

        public string Path { get; }

        public static string DefaultDirectory
        {
            get
            {
                return System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RobocopyGUI", "Logs");
            }
        }

        public static ExecutionLog Create(string directory, DateTime started)
        {
            Directory.CreateDirectory(directory);

            string stamp = started.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture);
            for (int attempt = 1; ; attempt++)
            {
                string name = attempt == 1
                    ? "robocopy_" + stamp + ".log"
                    : "robocopy_" + stamp + "_" + attempt + ".log";
                string path = System.IO.Path.Combine(directory, name);
                try
                {
                    var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                    return new ExecutionLog(path, new StreamWriter(stream, new UTF8Encoding(true)));
                }
                catch (IOException) when (File.Exists(path) && attempt < 100)
                {
                    // Another execution started in the same second; try the next suffix.
                }
            }
        }

        /// <summary>Writes native robocopy output unchanged.</summary>
        public void Write(string text)
        {
            if (!string.IsNullOrEmpty(text))
                _writer.Write(text);
        }

        public void Flush()
        {
            _writer.Flush();
        }

        public void Dispose()
        {
            _writer.Dispose();
        }
    }
}
