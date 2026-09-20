using System;
using System.IO;
using System.Threading;
using RobocopyGui.Command;
using RobocopyGui.Execution;
using RobocopyGui.Verification;
using Xunit;

namespace RobocopyGui.Tests
{
    public class ExitCodeTests
    {
        [Theory]
        [InlineData(0, (int)ExitCodeKind.Success, "Completed successfully")]
        [InlineData(1, (int)ExitCodeKind.Success, "Completed successfully")]
        [InlineData(3, (int)ExitCodeKind.SuccessWithDifferences, "Completed with differences")]
        [InlineData(7, (int)ExitCodeKind.SuccessWithDifferences, "Completed with differences")]
        [InlineData(8, (int)ExitCodeKind.CopyErrors, "Completed with errors")]
        [InlineData(16, (int)ExitCodeKind.SeriousError, "Failed")]
        [InlineData(-1, (int)ExitCodeKind.Terminated, "Terminated")]
        [InlineData(RobocopyExitCode.ControlCExit, (int)ExitCodeKind.Terminated, "Terminated")]
        public void Classifies_documented_codes(int code, int kind, string status)
        {
            Assert.Equal((ExitCodeKind)kind, RobocopyExitCode.Classify(code));
            Assert.Equal(status, RobocopyExitCode.StatusText(code));
            Assert.Equal(code >= 0 && code < 8, RobocopyExitCode.IsSuccess(code));
        }

        [Fact]
        public void Describes_combined_bits()
        {
            string text = RobocopyExitCode.Describe(3);
            Assert.Contains("copied successfully", text);
            Assert.Contains("Extra files", text);
        }
    }

    public class JobSettingsTests
    {
        [Fact]
        public void Round_trips_command_and_app_settings()
        {
            var settings = new JobSettings
            {
                Command = "robocopy \"\\\\SERVER-A\\Data\" \"\\\\SERVER-B\\Data\" /E /MT:16 /MY_CUSTOM_SWITCH",
                FolderMode = FolderMode.ExcludeSelected,
                StartEnabled = true,
                StartTime = new TimeSpan(22, 0, 0),
                StopEnabled = true,
                StopTime = new TimeSpan(7, 0, 0),
                VerificationEnabled = false,
                LogDirectory = "E:\\Logs",
            };

            string json = settings.ToJson();
            Assert.Contains("/MY_CUSTOM_SWITCH", json); // '/' is not escaped
            Assert.Contains("\"start\": \"22:00\"", json);

            var back = JobSettings.FromJson(json);
            Assert.Equal(settings.Command, back.Command);
            Assert.Equal(FolderMode.ExcludeSelected, back.FolderMode);
            Assert.True(back.StartEnabled);
            Assert.Equal(settings.StartTime, back.StartTime);
            Assert.True(back.StopEnabled);
            Assert.Equal(settings.StopTime, back.StopTime);
            Assert.False(back.VerificationEnabled);
            Assert.Equal("E:\\Logs", back.LogDirectory);
        }

        [Fact]
        public void Minimal_file_uses_defaults()
        {
            var settings = JobSettings.FromJson("{ \"command\": \"robocopy a b /E\" }");
            Assert.Equal("robocopy a b /E", settings.Command);
            Assert.True(settings.VerificationEnabled);
            Assert.False(settings.StartEnabled);
            Assert.Null(settings.LogDirectory);
        }

        [Fact]
        public void Missing_command_is_rejected()
        {
            Assert.Throws<InvalidDataException>(() => JobSettings.FromJson("{ \"schedule\": {} }"));
        }
    }

    public class SchedulingTests
    {
        [Fact]
        public void Next_occurrence_is_today_or_tomorrow()
        {
            var now = new DateTime(2026, 9, 19, 21, 30, 0);
            Assert.Equal(new DateTime(2026, 9, 19, 22, 0, 0), Scheduling.NextOccurrence(new TimeSpan(22, 0, 0), now));
            Assert.Equal(new DateTime(2026, 9, 20, 7, 0, 0), Scheduling.NextOccurrence(new TimeSpan(7, 0, 0), now));
            Assert.Equal(new DateTime(2026, 9, 20, 21, 30, 0), Scheduling.NextOccurrence(new TimeSpan(21, 30, 0), now));
        }
    }

    public class LineEndingTests
    {
        [Fact]
        public void Normalizes_across_chunks()
        {
            var n = new LineEndingNormalizer();
            string a = n.Normalize("one\r".ToCharArray(), 4);
            string b = n.Normalize("\ntwo\rthree\n".ToCharArray(), 11);
            Assert.Equal("one\r\ntwo\r\nthree\r\n", a + b + n.Flush());
        }
    }

    public class VerificationTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "RobocopyGuiTests_" + Guid.NewGuid().ToString("N"));

        public VerificationTests()
        {
            Write("root.txt", 10);
            Write("Accounting\\a.docx", 100);
            Write("Accounting\\a.tmp", 1000);
            Write("Accounting\\Deep\\b.docx", 200);
            Write("Temp\\t.docx", 5000);
        }

        public void Dispose()
        {
            Directory.Delete(_root, true);
        }

        private void Write(string relative, int bytes)
        {
            string path = Path.Combine(_root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, new byte[bytes]);
        }

        private long Size(string options)
        {
            var command = RobocopyCommand.Parse("robocopy " + CommandLine.Quote(_root) + " D:\\Dest " + options);
            return SizeCalculator.Calculate(_root, CopyScope.FromCommand(command), CancellationToken.None, null).Bytes;
        }

        [Fact]
        public void Applies_recursion_and_filters()
        {
            Assert.Equal(10, Size("/COPY:DAT"));
            Assert.Equal(6310, Size("/E"));
            Assert.Equal(310, Size("/E /XD " + CommandLine.Quote(Path.Combine(_root, "Temp")) + " /XF *.tmp"));
            Assert.Equal(5300, Size("*.docx /E"));
            Assert.Equal(5100, Size("*.docx /E /XD Deep"));
            Assert.Equal(1110, Size("/E /LEV:2 /XD Temp"));
        }

        [Fact]
        public void Wildcards()
        {
            Assert.True(Wildcard.IsMatch("*.*", "README"));
            Assert.True(Wildcard.IsMatch("~$*", "~$Budget.xlsx"));
            Assert.True(Wildcard.IsMatch("*.DOCX", "a.docx"));
            Assert.False(Wildcard.IsMatch("*.doc", "a.docx"));
            Assert.True(Wildcard.IsMatch("a?c", "abc"));
        }
    }
}
