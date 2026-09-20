using System;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using RobocopyGui.Command;
using RobocopyGui.Execution;
using RobocopyGui.Ui;
using RobocopyGui.Verification;

namespace RobocopyGui
{
    /// <summary>
    /// Job lifecycle. Start approves the current configuration and locks it; the
    /// approved command is snapshotted and cannot change until the job ends.
    /// </summary>
    internal partial class MainForm
    {
        private enum JobState
        {
            Idle,
            Scheduled,
            Preparing,
            Running,
            Stopping,
            Verifying,
        }

        private enum StopReason
        {
            None,
            User,
            Schedule,
        }

        private static readonly TimeSpan GracefulStopTimeout = TimeSpan.FromMinutes(2);

        private JobState _state = JobState.Idle;
        private StopReason _stopReason;
        private bool _closing;

        // The approved configuration (snapshot taken when Start is pressed).
        private string _approvedCommand;
        private bool _approvedVerify;
        private string _approvedLogDirectory;
        private TimeSpan? _approvedStopTime;

        private DateTime _scheduledStartAt;
        private DateTime _runStartedAt;
        private DateTime? _stopDeadline;
        private DateTime? _gracefulStopRequestedAt;

        private RobocopyRunner _runner;
        private ExecutionLog _log;
        private CancellationTokenSource _cancellation;
        private string _lastLogPath;

        // ------------------------------------------------------------------
        // Start / Run now / Stop
        // ------------------------------------------------------------------

        private void StartClicked()
        {
            var command = CurrentCommand();
            string problem = ValidateCommand(command);
            if (problem == null && txtLogDirectory.Text.Trim().Length == 0)
                problem = "Choose a folder for the execution logs (Advanced).";
            if (problem != null)
            {
                MessageBox.Show(this, problem, "Cannot start", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _approvedCommand = txtCommand.Text.Trim();
            _approvedVerify = chkVerify.Checked;
            _approvedLogDirectory = txtLogDirectory.Text.Trim();
            _approvedStopTime = chkStop.Checked ? TimeOf(dtpStop) : (TimeSpan?)null;
            lblExitCode.Text = string.Empty;
            lblVerification.Text = string.Empty;

            if (!chkStart.Checked)
            {
                BeginExecution();
                return;
            }

            _scheduledStartAt = Scheduling.NextOccurrence(TimeOf(dtpStart), DateTime.Now);
            SetState(JobState.Scheduled);
            AppendGuiLine("Job approved. Scheduled to start " + Scheduling.Describe(_scheduledStartAt, DateTime.Now) + ": " + _approvedCommand);
            UpdateProgressStatus();
        }

        private void RunNowClicked()
        {
            if (_state != JobState.Scheduled)
                return;
            AppendGuiLine("Run now – starting before the scheduled time.");
            BeginExecution();
        }

        private void StopClicked()
        {
            switch (_state)
            {
                case JobState.Scheduled:
                    if (!ConfirmDialog.Show(this, "Stop", "Stop the scheduled job?",
                        "Robocopy has not started yet. The start " + Scheduling.Describe(_scheduledStartAt, DateTime.Now) +
                        " will be cancelled and the configuration unlocked.", "Stop"))
                        return;
                    if (_state != JobState.Scheduled)
                        return; // started while the dialog was open
                    AppendGuiLine("Scheduled start cancelled by user.");
                    SetState(JobState.Idle);
                    SetStatusLine("Scheduled start cancelled", SystemColors.ControlText);
                    return;

                case JobState.Verifying:
                    if (!ConfirmDialog.Show(this, "Stop", "Stop verification?",
                        "Robocopy has already finished. Only the destination size check will be cancelled.", "Stop"))
                        return;
                    if (_state != JobState.Verifying)
                        return;
                    _stopReason = StopReason.User;
                    _cancellation?.Cancel();
                    return;

                case JobState.Preparing:
                case JobState.Running:
                case JobState.Stopping:
                    if (!ConfirmDialog.Show(this, "Stop", "Stop Robocopy?",
                        "The current copy operation will be terminated.\r\nYou can run the job again later.", "Stop"))
                        return;
                    if (_state != JobState.Preparing && _state != JobState.Running && _state != JobState.Stopping)
                        return;
                    _stopReason = StopReason.User;
                    AppendGuiLine("Stop requested by user – terminating robocopy.");
                    _cancellation?.Cancel();
                    _runner?.Kill();
                    return;
            }
        }

        // ------------------------------------------------------------------
        // Execution
        // ------------------------------------------------------------------

        private async void BeginExecution()
        {
            try
            {
                await ExecuteAsync();
            }
            catch (Exception ex)
            {
                if (_closing)
                    return;
                AppendGuiLine("Unexpected error: " + ex.Message);
                Finish("Failed – unexpected error", ErrorColor);
            }
        }

        private async Task ExecuteAsync()
        {
            _stopReason = StopReason.None;
            _gracefulStopRequestedAt = null;
            _cancellation = new CancellationTokenSource();
            _runStartedAt = DateTime.Now;
            _stopDeadline = _approvedStopTime.HasValue
                ? Scheduling.NextOccurrence(_approvedStopTime.Value, _runStartedAt)
                : (DateTime?)null;
            SetState(JobState.Preparing);

            try
            {
                _log = ExecutionLog.Create(_approvedLogDirectory, _runStartedAt);
                _lastLogPath = _log.Path;
            }
            catch (Exception ex)
            {
                AppendGuiLine("The execution log could not be created in \"" + _approvedLogDirectory + "\": " + ex.Message);
                Finish("Not started – the log file could not be created", ErrorColor);
                return;
            }

            var command = RobocopyCommand.Parse(_approvedCommand);
            bool dryRun = command.HasSwitch("/L");
            AppendGuiLine("==== Execution started " + _runStartedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " ====");
            AppendGuiLine("Command: " + _approvedCommand);
            AppendGuiLine("Log file: " + _log.Path);
            if (_stopDeadline.HasValue)
                AppendGuiLine("Scheduled stop " + Scheduling.Describe(_stopDeadline.Value, _runStartedAt) + ".");

            // Verification step 1: total the source once, before copying.
            var scope = CopyScope.FromCommand(command);
            SizeResult sourceSize = null;
            if (_approvedVerify && !dryRun)
            {
                try
                {
                    sourceSize = await CalculateSizeAsync(command.Source, scope, "source");
                    AppendGuiLine("Source size: " + DescribeSize(sourceSize));
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    AppendGuiLine("Source size could not be calculated (" + ex.Message + "). Verification will be skipped.");
                }
            }

            if (_closing)
                return;
            if (_stopReason != StopReason.None)
            {
                FinishStopped("before robocopy started");
                return;
            }

            SetState(JobState.Running);
            _runner = new RobocopyRunner();
            try
            {
                _runner.Start(command.Executable, command.Arguments);
            }
            catch (Exception ex)
            {
                AppendGuiLine("Robocopy could not be started: " + ex.Message);
                Finish("Failed – robocopy could not be started", ErrorColor);
                return;
            }

            int exitCode = await _runner.Completion;
            ConsoleInterrupt.Restore();
            if (_closing)
                return;
            DrainOutput();

            string exitText = FormatExitCode(exitCode);
            lblExitCode.Text = "Exit code: " + exitText;
            AppendGuiLine("Robocopy exited. Exit code: " + exitText);

            if (_stopReason != StopReason.None)
            {
                FinishStopped(null);
                return;
            }

            string status = RobocopyExitCode.StatusText(exitCode) + (dryRun ? " (dry run)" : string.Empty);
            Color color = StatusColor(RobocopyExitCode.Classify(exitCode));

            // Verification step 2: total the destination once, after a successful run.
            if (!_approvedVerify)
                SetVerification("Verification: off", SystemColors.GrayText, log: false);
            else if (dryRun)
                SetVerification("Verification: skipped – dry run, nothing was copied", SystemColors.GrayText);
            else if (!RobocopyExitCode.IsSuccess(exitCode))
                SetVerification("Verification: skipped – robocopy did not complete successfully", SystemColors.GrayText);
            else if (sourceSize == null)
                SetVerification("Verification: skipped – the source size could not be calculated", SystemColors.GrayText);
            else
                await VerifyDestinationAsync(command.Destination, scope, sourceSize);

            if (_closing)
                return;
            Finish(status, color);
        }

        private async Task VerifyDestinationAsync(string destination, CopyScope scope, SizeResult sourceSize)
        {
            SetState(JobState.Verifying);
            SizeResult destinationSize;
            try
            {
                destinationSize = await CalculateSizeAsync(destination, scope, "destination");
            }
            catch (OperationCanceledException)
            {
                string why = _stopReason == StopReason.Schedule ? "scheduled stop time reached" : "stopped by user";
                SetVerification("Verification: cancelled – " + why, WarningColor);
                return;
            }
            catch (Exception ex)
            {
                SetVerification("Verification: the destination could not be read – " + ex.Message, WarningColor);
                return;
            }

            long difference = destinationSize.Bytes - sourceSize.Bytes;
            bool match = difference == 0;
            AppendGuiLine("Verification");
            AppendGuiLine("  Source:      " + DescribeSize(sourceSize));
            AppendGuiLine("  Destination: " + DescribeSize(destinationSize));
            AppendGuiLine("  Difference:  " + difference.ToString("N0", CultureInfo.CurrentCulture) + " bytes");
            AppendGuiLine("  Result:      " + (match
                ? "MATCH"
                : "MISMATCH – the totals differ. See the robocopy output above for skipped, locked, excluded or extra files."));

            SetVerification(string.Format(CultureInfo.CurrentCulture,
                    "Verification: {0} – source {1:N0} bytes · destination {2:N0} bytes · difference {3:N0} bytes",
                    match ? "MATCH" : "⚠ MISMATCH", sourceSize.Bytes, destinationSize.Bytes, difference),
                match ? SuccessColor : WarningColor, log: false);
        }

        private Task<SizeResult> CalculateSizeAsync(string root, CopyScope scope, string label)
        {
            var token = _cancellation.Token;
            SetStatusLine("Calculating " + label + " size…", SystemColors.ControlText);
            var progress = new Progress<SizeResult>(p =>
            {
                if (_state == JobState.Preparing || _state == JobState.Verifying)
                    SetStatusLine(string.Format(CultureInfo.CurrentCulture, "Calculating {0} size… {1:N0} files, {2}", label, p.Files, FormatBytes(p.Bytes)), SystemColors.ControlText);
            });
            return Task.Run(() => SizeCalculator.Calculate(root, scope, token, progress), token);
        }

        private void FinishStopped(string detail)
        {
            bool bySchedule = _stopReason == StopReason.Schedule;
            string status = bySchedule ? "Stopped by schedule" : "Stopped by user";
            if (detail != null)
                AppendGuiLine(status + " " + detail + ".");
            if (bySchedule)
                AppendGuiLine("The scheduled stop time was reached. The configuration is unchanged – press Start to run the same job again. " +
                              "Robocopy skips files that are already up to date; with /Z, interrupted files resume.");
            SetVerification("Verification: skipped – the run was stopped", SystemColors.GrayText);
            Finish(status, WarningColor);
        }

        private void Finish(string status, Color color)
        {
            var finished = DateTime.Now;
            AppendGuiLine("Result: " + status + " – duration " + Scheduling.FormatDuration(finished - _runStartedAt));

            _log?.Dispose();
            _log = null;
            _runner?.Dispose();
            _runner = null;
            _cancellation?.Dispose();
            _cancellation = null;
            _stopDeadline = null;
            _gracefulStopRequestedAt = null;

            SetState(JobState.Idle);
            SetStatusLine(status + " · finished " + finished.ToString("HH:mm", CultureInfo.InvariantCulture), color);
        }

        // ------------------------------------------------------------------
        // Timers
        // ------------------------------------------------------------------

        private void OnScheduleTick()
        {
            var now = DateTime.Now;
            switch (_state)
            {
                case JobState.Scheduled:
                    if (now >= _scheduledStartAt)
                    {
                        AppendGuiLine("Scheduled start time reached.");
                        BeginExecution();
                        return;
                    }
                    break;

                case JobState.Preparing:
                case JobState.Running:
                case JobState.Verifying:
                    if (_stopDeadline.HasValue && now >= _stopDeadline.Value && _stopReason == StopReason.None)
                    {
                        ScheduledStop();
                        return;
                    }
                    break;

                case JobState.Stopping:
                    if (_gracefulStopRequestedAt.HasValue && now - _gracefulStopRequestedAt.Value > GracefulStopTimeout)
                    {
                        _gracefulStopRequestedAt = null;
                        AppendGuiLine("Robocopy did not exit within " + GracefulStopTimeout.TotalMinutes + " minutes after Ctrl+C – terminating it.");
                        _runner?.Kill();
                    }
                    break;
            }

            UpdateProgressStatus();
        }

        /// <summary>Scheduled stop: ask robocopy to exit on its own (Ctrl+C) rather than killing it.</summary>
        private void ScheduledStop()
        {
            _stopReason = StopReason.Schedule;
            AppendGuiLine("Scheduled stop time reached (" + _stopDeadline.Value.ToString("HH:mm", CultureInfo.InvariantCulture) + ").");

            if (_state != JobState.Running)
            {
                _cancellation?.Cancel(); // source scan or verification
                return;
            }

            SetState(JobState.Stopping);
            AppendGuiLine("Requesting graceful robocopy termination (Ctrl+C)…");
            if (_runner.RequestGracefulStop())
            {
                _gracefulStopRequestedAt = DateTime.Now;
            }
            else
            {
                AppendGuiLine("Ctrl+C could not be delivered – terminating robocopy.");
                _runner.Kill();
            }
            UpdateProgressStatus();
        }

        private void UpdateProgressStatus()
        {
            var now = DateTime.Now;
            switch (_state)
            {
                case JobState.Scheduled:
                    SetStatusLine("Scheduled – starts " + Scheduling.Describe(_scheduledStartAt, now) +
                                  " (in " + Scheduling.FormatDuration(_scheduledStartAt - now) + ")", SystemColors.ControlText);
                    break;
                case JobState.Running:
                    string stop = _stopDeadline.HasValue ? " · stops " + Scheduling.Describe(_stopDeadline.Value, now) : string.Empty;
                    SetStatusLine("Running – started " + _runStartedAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture) +
                                  " (" + Scheduling.FormatDuration(now - _runStartedAt) + ")" + stop, SystemColors.ControlText);
                    break;
                case JobState.Stopping:
                    SetStatusLine("Stopping – waiting for robocopy to exit after the scheduled stop…", WarningColor);
                    break;
            }
        }

        // ------------------------------------------------------------------
        // State, status and output
        // ------------------------------------------------------------------

        private void SetState(JobState state)
        {
            _state = state;
            bool idle = state == JobState.Idle;

            // Keep focus on Start/Stop so disabling the settings doesn't push it into the command box.
            if (!idle)
                ActiveControl = btnStart;

            foreach (var control in new[] { pnlSource, pnlDestination, grpFolders, grpParameters, grpFilters, grpSchedule })
                control.Enabled = idle;
            txtCommand.ReadOnly = !idle;
            txtCommand.BackColor = idle ? SystemColors.Window : SystemColors.Control;
            btnImport.Enabled = idle;
            btnStart.Text = idle ? "Start" : "Stop";
            btnRunNow.Visible = state == JobState.Scheduled;

            Text = idle ? "Robocopy GUI" : "Robocopy GUI – " + state;
        }

        private void SetStatusLine(string text, Color color)
        {
            lblStatus.Text = "Status: " + text;
            lblStatus.ForeColor = color;

            // NotifyIcon.Text is limited to 63 characters.
            string tray = "Robocopy GUI – " + text;
            trayIcon.Text = tray.Length > 63 ? tray.Substring(0, 60) + "..." : tray;
        }

        private void SetVerification(string text, Color color, bool log = true)
        {
            lblVerification.Text = text;
            lblVerification.ForeColor = color;
            if (log)
                AppendGuiLine(text);
        }

        private static Color StatusColor(ExitCodeKind kind)
        {
            switch (kind)
            {
                case ExitCodeKind.Success: return SuccessColor;
                case ExitCodeKind.SuccessWithDifferences: return SuccessColor;
                case ExitCodeKind.CopyErrors: return WarningColor;
                default: return ErrorColor;
            }
        }

        private static string FormatExitCode(int code)
        {
            string number = code >= 0 && code <= 31
                ? code.ToString(CultureInfo.InvariantCulture)
                : code.ToString(CultureInfo.InvariantCulture) + " (0x" + code.ToString("X8", CultureInfo.InvariantCulture) + ")";
            return number + " – " + RobocopyExitCode.Describe(code);
        }

        private static string DescribeSize(SizeResult size)
        {
            string text = size.Bytes < 1024
                ? string.Format(CultureInfo.CurrentCulture, "{0:N0} bytes in {1:N0} files", size.Bytes, size.Files)
                : string.Format(CultureInfo.CurrentCulture, "{0:N0} bytes ({1}) in {2:N0} files", size.Bytes, FormatBytes(size.Bytes), size.Files);
            if (size.UnreadableDirectories > 0)
                text += string.Format(CultureInfo.CurrentCulture, "; {0:N0} folders could not be read", size.UnreadableDirectories);
            return text;
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = { "bytes", "KB", "MB", "GB", "TB", "PB" };
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return unit == 0
                ? bytes.ToString("N0", CultureInfo.CurrentCulture) + " bytes"
                : value.ToString("0.00", CultureInfo.CurrentCulture) + " " + units[unit];
        }

        /// <summary>Moves queued robocopy output into the log file and the output view.</summary>
        private void DrainOutput()
        {
            if (_runner == null)
                return;
            string text = _runner.DrainOutput();
            if (text.Length == 0)
                return;
            _log?.Write(text);
            _log?.Flush();
            txtOutput.AppendOutput(text);
        }

        /// <summary>Writes a line from this application (not robocopy) to the output and the log.</summary>
        private void AppendGuiLine(string message)
        {
            DrainOutput();
            string line = (txtOutput.TextLength > 0 && !txtOutput.EndsWithNewLine ? "\r\n" : string.Empty)
                          + ">> " + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  " + message + "\r\n";
            _log?.Write(line);
            _log?.Flush();
            txtOutput.AppendOutput(line);
        }

        // ------------------------------------------------------------------
        // Closing
        // ------------------------------------------------------------------

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_state != JobState.Idle && e.CloseReason == CloseReason.UserClosing && !ConfirmClose())
            {
                e.Cancel = true;
                base.OnFormClosing(e);
                return;
            }

            if (_state != JobState.Idle)
                AbandonJob();
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            trayIcon.Visible = false;
            base.OnFormClosed(e);
        }

        private bool ConfirmClose()
        {
            switch (_state)
            {
                case JobState.Scheduled:
                    return ConfirmDialog.Show(this, "Close", "Close Robocopy GUI?",
                        "A job is scheduled to start " + Scheduling.Describe(_scheduledStartAt, DateTime.Now) +
                        ". If the application closes, the scheduled job will not run.", "Close");
                case JobState.Verifying:
                    return ConfirmDialog.Show(this, "Close", "Close Robocopy GUI?",
                        "Robocopy has finished and the destination size is being verified. Closing cancels the verification.", "Close");
                default:
                    return ConfirmDialog.Show(this, "Close", "Close Robocopy GUI?",
                        "Robocopy is currently running. Closing the application will terminate it.\r\nYou can run the job again later.", "Close");
            }
        }

        /// <summary>Terminates or cancels the current job because the application is exiting.</summary>
        private void AbandonJob()
        {
            _closing = true;
            _cancellation?.Cancel();
            if (_runner != null && _runner.IsRunning)
            {
                _runner.Kill();
                _runner.Completion?.Wait(3000);
            }

            AppendGuiLine(_state == JobState.Scheduled
                ? "Application closed – the scheduled job was cancelled."
                : "Application closed – robocopy was terminated.");
            _log?.Dispose();
            _log = null;
            ConsoleInterrupt.Restore();
        }
    }
}
