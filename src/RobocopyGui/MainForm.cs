using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using RobocopyGui.Command;
using RobocopyGui.Execution;
using RobocopyGui.Ui;

namespace RobocopyGui
{
    /// <summary>
    /// The main window. The command text box is the single source of truth:
    /// GUI controls write only their own switches into it (EditCommand), and every
    /// change to the command is parsed back into the controls (SyncControlsFromCommand).
    /// </summary>
    internal partial class MainForm : Form
    {
        private static readonly Color SuccessColor = Color.FromArgb(0, 120, 0);
        private static readonly Color WarningColor = Color.FromArgb(185, 100, 0);
        private static readonly Color ErrorColor = Color.Firebrick;

        /// <summary>True while controls are being updated from the command, so their events don't write back.</summary>
        private bool _syncing;

        private FolderMode _folderMode = FolderMode.IncludeSelected;

        /// <summary>The source root the folder list belongs to; null if nothing is listed.</summary>
        private string _scannedRoot;
        private string[] _folderNames = new string[0];
        private int _scanVersion;

        private FormWindowState _restoreState = FormWindowState.Normal;

        public MainForm()
        {
            BuildLayout();
            LoadIcons();
            WireEvents();
            ApplySettings(new JobSettings());
            SetState(JobState.Idle);
            SetStatusLine("Ready", SystemColors.ControlText);
            scheduleTimer.Start();
            outputTimer.Start();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            FitConfigPanel();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                components.Dispose();
            base.Dispose(disposing);
        }

        private void WireEvents()
        {
            txtSource.TextChanged += (s, e) => EditCommand(c => c.SetPositional(0, txtSource.Text));
            txtDestination.TextChanged += (s, e) => EditCommand(c => c.SetPositional(1, txtDestination.Text));
            txtSource.Leave += (s, e) => CommitSource(removeOldExclusions: true, force: false);
            txtSource.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Enter)
                    return;
                e.SuppressKeyPress = true;
                CommitSource(removeOldExclusions: true, force: false);
            };
            btnBrowseSource.Click += (s, e) => BrowseSource();
            btnBrowseDestination.Click += (s, e) => BrowseDestination();

            cboFolderMode.SelectedIndexChanged += (s, e) =>
            {
                if (_syncing)
                    return;
                _folderMode = (FolderMode)cboFolderMode.SelectedIndex;
                SyncFolderChecks(CurrentCommand());
            };
            lstFolders.ItemCheck += (s, e) =>
            {
                // ItemCheck fires before the state changes; apply once it has.
                if (!_syncing)
                    BeginInvoke(new Action(ApplyFolderChecks));
            };
            btnSelectAll.Click += (s, e) => SetAllFolderChecks(true);
            btnSelectNone.Click += (s, e) => SetAllFolderChecks(false);
            btnRefreshFolders.Click += (s, e) => CommitSource(removeOldExclusions: false, force: true);

            txtInclude.TextChanged += (s, e) => EditCommand(c => c.SetFileSpecs(SplitPatterns(txtInclude.Text)));
            txtExclude.TextChanged += (s, e) => EditCommand(c => c.SetValues("/XF", v => true, SplitPatterns(txtExclude.Text)));

            chkMirror.CheckedChanged += (s, e) => EditCommand(c => c.SetSwitch("/MIR", chkMirror.Checked));
            chkSecurity.CheckedChanged += (s, e) => EditCommand(c => c.SetCopyFlags(WithFlag(c.CopyFlags, 'S', chkSecurity.Checked)));
            chkRestartable.CheckedChanged += (s, e) => EditCommand(c =>
            {
                if (!chkRestartable.Checked)
                {
                    c.SetSwitch("/Z", false);
                    c.SetSwitch("/ZB", false);
                }
                else if (!c.HasSwitch("/ZB"))
                {
                    c.SetSwitch("/Z", true);
                }
            });
            chkMultithread.CheckedChanged += (s, e) =>
            {
                numThreads.Enabled = chkMultithread.Checked;
                EditCommand(ApplyThreads);
            };
            numThreads.ValueChanged += (s, e) => EditCommand(ApplyThreads);
            chkDryRun.CheckedChanged += (s, e) => EditCommand(c => c.SetSwitch("/L", chkDryRun.Checked));
            btnAdvanced.Click += (s, e) => ToggleAdvanced();

            cboSubfolders.SelectedIndexChanged += (s, e) => EditCommand(c =>
                c.ReplaceSwitches(new[] { "/S", "/E" }, cboSubfolders.SelectedIndex == 2 ? "/E" : cboSubfolders.SelectedIndex == 1 ? "/S" : null));
            numRetries.ValueChanged += (s, e) => EditCommand(c => c.SetSwitch("/R", ((int)numRetries.Value).ToString()));
            numWait.ValueChanged += (s, e) => EditCommand(c => c.SetSwitch("/W", ((int)numWait.Value).ToString()));
            txtCopyFlags.TextChanged += (s, e) => EditCommand(c => c.SetCopyFlags(txtCopyFlags.Text.Trim()));
            txtDirCopyFlags.TextChanged += (s, e) => EditCommand(c => c.SetDirectoryCopyFlags(txtDirCopyFlags.Text.Trim()));
            chkExcludeJunctions.CheckedChanged += (s, e) => EditCommand(c => c.SetSwitch("/XJ", chkExcludeJunctions.Checked));
            chkNoProgress.CheckedChanged += (s, e) => EditCommand(c => c.SetSwitch("/NP", chkNoProgress.Checked));
            chkNoFileList.CheckedChanged += (s, e) => EditCommand(c => c.SetSwitch("/NFL", chkNoFileList.Checked));
            chkNoDirList.CheckedChanged += (s, e) => EditCommand(c => c.SetSwitch("/NDL", chkNoDirList.Checked));
            btnBrowseLog.Click += (s, e) => BrowseLogDirectory();

            chkStart.CheckedChanged += (s, e) => dtpStart.Enabled = chkStart.Checked;
            chkStop.CheckedChanged += (s, e) => dtpStop.Enabled = chkStop.Checked;

            txtCommand.TextChanged += (s, e) => OnCommandTextChanged();
            txtCommand.Leave += (s, e) => CommitSource(removeOldExclusions: false, force: false);
            txtCommand.KeyDown += (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.A)
                {
                    txtCommand.SelectAll();
                    e.SuppressKeyPress = true;
                }
                else if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true; // the command is a single line
                }
            };
            chkWrap.CheckedChanged += (s, e) =>
            {
                txtCommand.WordWrap = chkWrap.Checked;
                txtCommand.ScrollBars = chkWrap.Checked ? ScrollBars.Vertical : ScrollBars.Both;
            };

            btnImport.Click += (s, e) => ImportSettings();
            btnExport.Click += (s, e) => ExportSettings();
            btnStart.Click += (s, e) =>
            {
                if (_state == JobState.Idle)
                    StartClicked();
                else
                    StopClicked();
            };
            btnRunNow.Click += (s, e) => RunNowClicked();

            chkAutoScroll.CheckedChanged += (s, e) => txtOutput.AutoScroll = chkAutoScroll.Checked;
            btnClearOutput.Click += (s, e) => txtOutput.Clear();
            btnOpenLogs.Click += (s, e) => OpenLogFolder();

            scheduleTimer.Tick += (s, e) => OnScheduleTick();
            outputTimer.Tick += (s, e) => DrainOutput();
            trayIcon.MouseClick += (s, e) => RestoreFromTray();
        }

        // ------------------------------------------------------------------
        // Command <-> controls
        // ------------------------------------------------------------------

        private RobocopyCommand CurrentCommand()
        {
            return RobocopyCommand.Parse(txtCommand.Text);
        }

        /// <summary>Applies a GUI change to the command, touching only the switches the edit names.</summary>
        private void EditCommand(Action<RobocopyCommand> edit)
        {
            if (_syncing || _state != JobState.Idle)
                return;

            var command = CurrentCommand();
            edit(command);
            string text = command.ToString();
            if (text != txtCommand.Text)
                txtCommand.Text = text;
        }

        private void OnCommandTextChanged()
        {
            string text = txtCommand.Text;
            if (text.IndexOf('\r') >= 0 || text.IndexOf('\n') >= 0)
            {
                // Keep one logical line (e.g. after pasting); wrapping is visual only.
                int caret = txtCommand.SelectionStart;
                txtCommand.Text = text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
                txtCommand.SelectionStart = Math.Min(caret, txtCommand.TextLength);
                return; // the assignment raises TextChanged again
            }

            SyncControlsFromCommand();
        }

        private void SyncControlsFromCommand()
        {
            var command = CurrentCommand();
            bool wasSyncing = _syncing;
            _syncing = true;
            try
            {
                SetText(txtSource, command.Source ?? string.Empty);
                SetText(txtDestination, command.Destination ?? string.Empty);

                var specs = command.FileSpecs;
                if (!SplitPatterns(txtInclude.Text).SequenceEqual(specs))
                    txtInclude.Text = string.Join(";", specs);
                var excludes = command.GetValues("/XF");
                if (!SplitPatterns(txtExclude.Text).SequenceEqual(excludes))
                    txtExclude.Text = string.Join(";", excludes);

                string copyFlags = command.CopyFlags;
                chkMirror.Checked = command.HasSwitch("/MIR");
                chkSecurity.Checked = copyFlags.IndexOf('S') >= 0;
                chkRestartable.Checked = command.HasSwitch("/Z") || command.HasSwitch("/ZB");

                string threads = command.GetSwitchArgument("/MT");
                chkMultithread.Checked = threads != null;
                int threadCount;
                if (threads == string.Empty)
                    SetNumber(numThreads, RobocopyCommand.DefaultThreadsWhenUnspecified);
                else if (int.TryParse(threads, out threadCount))
                    SetNumber(numThreads, threadCount);
                numThreads.Enabled = chkMultithread.Checked;

                chkDryRun.Checked = command.HasSwitch("/L");
                cboSubfolders.SelectedIndex = command.HasSwitch("/E") ? 2 : command.HasSwitch("/S") ? 1 : 0;
                SetNumber(numRetries, command.GetIntArgument("/R") ?? RobocopyCommand.DefaultRetries);
                SetNumber(numWait, command.GetIntArgument("/W") ?? RobocopyCommand.DefaultWaitSeconds);
                if (!string.Equals(txtCopyFlags.Text.Trim(), copyFlags, StringComparison.OrdinalIgnoreCase))
                    txtCopyFlags.Text = copyFlags;
                string dirFlags = command.DirectoryCopyFlags;
                if (!string.Equals(txtDirCopyFlags.Text.Trim(), dirFlags, StringComparison.OrdinalIgnoreCase))
                    txtDirCopyFlags.Text = dirFlags;
                chkExcludeJunctions.Checked = command.HasSwitch("/XJ");
                chkNoProgress.Checked = command.HasSwitch("/NP");
                chkNoFileList.Checked = command.HasSwitch("/NFL");
                chkNoDirList.Checked = command.HasSwitch("/NDL");

                SyncFolderChecks(command);
            }
            finally
            {
                _syncing = wasSyncing;
            }

            UpdateCommandHint(command);
        }

        private void UpdateCommandHint(RobocopyCommand command)
        {
            string problem = ValidateCommand(command);
            lblCommandHint.Text = problem ?? "The command is the configuration. Edit it freely – switches the GUI does not know are kept as written.";
            lblCommandHint.ForeColor = problem == null ? SystemColors.GrayText : WarningColor;
        }

        private static string ValidateCommand(RobocopyCommand command)
        {
            if (!command.HasExecutable)
                return "The command must start with robocopy.";
            if (string.IsNullOrWhiteSpace(command.Source))
                return "Choose a source folder.";
            if (string.IsNullOrWhiteSpace(command.Destination))
                return "Choose a destination folder.";
            return null;
        }

        private void ApplyThreads(RobocopyCommand command)
        {
            command.SetSwitch("/MT", chkMultithread.Checked ? ((int)numThreads.Value).ToString() : null);
        }

        /// <summary>Adds or removes one /COPY flag letter, keeping robocopy's usual DATSOUX order.</summary>
        private static string WithFlag(string flags, char flag, bool present)
        {
            const string order = "DATSOUX";
            var set = new HashSet<char>(flags.ToUpperInvariant());
            if (present)
                set.Add(flag);
            else
                set.Remove(flag);

            var sb = new StringBuilder();
            foreach (char c in order)
                if (set.Contains(c))
                    sb.Append(c);
            foreach (char c in flags.ToUpperInvariant())
                if (order.IndexOf(c) < 0 && set.Contains(c) && sb.ToString().IndexOf(c) < 0)
                    sb.Append(c);
            return sb.ToString();
        }

        private static List<string> SplitPatterns(string text)
        {
            return (text ?? string.Empty).Split(';').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        }

        private static void SetText(TextBox box, string text)
        {
            if (box.Text != text)
                box.Text = text;
        }

        private static void SetNumber(NumericUpDown box, int value)
        {
            box.Value = Math.Max(box.Minimum, Math.Min(box.Maximum, value));
        }

        // ------------------------------------------------------------------
        // Source folders
        // ------------------------------------------------------------------

        private static bool SamePath(string a, string b)
        {
            return string.Equals((a ?? string.Empty).Trim().TrimEnd('\\'), (b ?? string.Empty).Trim().TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Rescans the folder list when the source changed. When the change came from the
        /// Source box or Browse, exclusions for the previously listed folders are dropped.
        /// Edits typed into the command itself are never rewritten.
        /// </summary>
        private void CommitSource(bool removeOldExclusions, bool force)
        {
            if (_state != JobState.Idle)
                return;

            string source = CurrentCommand().Source ?? string.Empty;
            bool changed = !SamePath(source, _scannedRoot);
            if (!changed && !force)
                return;

            if (changed && removeOldExclusions && _scannedRoot != null && _folderNames.Length > 0)
            {
                string oldRoot = _scannedRoot;
                var oldNames = _folderNames;
                EditCommand(c => FolderExclusions.SetExcludedChildren(c, oldRoot, oldNames, Enumerable.Empty<string>()));
            }

            ScanFolders(source);
        }

        private async void ScanFolders(string root)
        {
            int version = ++_scanVersion;
            root = (root ?? string.Empty).Trim();
            _scannedRoot = root.Length == 0 ? null : root;
            _folderNames = new string[0];
            lstFolders.Items.Clear();

            if (_scannedRoot == null)
            {
                lblFolderStatus.Text = "Choose a source folder to list its subfolders.";
                return;
            }

            lblFolderStatus.Text = "Scanning " + root + " …";
            string error = null;
            string[] names;
            try
            {
                names = await Task.Run(() => FolderScanner.GetChildFolders(root));
            }
            catch (Exception ex)
            {
                names = new string[0];
                error = ex.Message;
            }

            if (version != _scanVersion || IsDisposed)
                return;

            _folderNames = names;
            bool wasSyncing = _syncing;
            _syncing = true;
            lstFolders.Items.AddRange(names.Cast<object>().ToArray());
            _syncing = wasSyncing;

            SyncFolderChecks(CurrentCommand());
            if (error != null)
                lblFolderStatus.Text = "Could not list folders: " + error;
        }

        private void SyncFolderChecks(RobocopyCommand command)
        {
            bool listed = _scannedRoot != null && SamePath(command.Source, _scannedRoot);
            lstFolders.Enabled = listed;
            if (_scannedRoot == null)
                return;
            if (!listed)
            {
                lblFolderStatus.Text = "Source changed – press Enter in the Source box or Refresh to list its folders.";
                return;
            }

            var excluded = FolderExclusions.GetExcludedChildren(command, _scannedRoot);
            bool wasSyncing = _syncing;
            _syncing = true;
            for (int i = 0; i < lstFolders.Items.Count; i++)
            {
                bool isExcluded = excluded.Contains((string)lstFolders.Items[i]);
                bool check = _folderMode == FolderMode.IncludeSelected ? !isExcluded : isExcluded;
                if (lstFolders.GetItemChecked(i) != check)
                    lstFolders.SetItemChecked(i, check);
            }
            _syncing = wasSyncing;

            int excludedCount = _folderNames.Count(excluded.Contains);
            lblFolderStatus.Text = _folderNames.Length == 0
                ? "No subfolders."
                : string.Format("{0} subfolders · {1} copied · {2} excluded", _folderNames.Length, _folderNames.Length - excludedCount, excludedCount);
        }

        private void ApplyFolderChecks()
        {
            if (_syncing || _state != JobState.Idle || _scannedRoot == null || !SamePath(CurrentCommand().Source, _scannedRoot))
                return;

            var checkedNames = new HashSet<string>(lstFolders.CheckedItems.Cast<string>(), StringComparer.OrdinalIgnoreCase);
            var excluded = _folderMode == FolderMode.IncludeSelected
                ? _folderNames.Where(n => !checkedNames.Contains(n))
                : _folderNames.Where(checkedNames.Contains);
            string root = _scannedRoot;
            EditCommand(c => FolderExclusions.SetExcludedChildren(c, root, _folderNames, excluded.ToList()));
            SyncFolderChecks(CurrentCommand());
        }

        private void SetAllFolderChecks(bool check)
        {
            _syncing = true;
            for (int i = 0; i < lstFolders.Items.Count; i++)
                lstFolders.SetItemChecked(i, check);
            _syncing = false;
            ApplyFolderChecks();
        }

        private void BrowseSource()
        {
            string path = FolderPicker.Show(this, "Select the source folder", txtSource.Text);
            if (path == null)
                return;
            txtSource.Text = path;
            CommitSource(removeOldExclusions: true, force: true);
        }

        private void BrowseDestination()
        {
            string path = FolderPicker.Show(this, "Select the destination folder", txtDestination.Text);
            if (path != null)
                txtDestination.Text = path;
        }

        private void BrowseLogDirectory()
        {
            string path = FolderPicker.Show(this, "Select the folder for execution logs", txtLogDirectory.Text);
            if (path != null)
                txtLogDirectory.Text = path;
        }

        // ------------------------------------------------------------------
        // Import / export
        // ------------------------------------------------------------------

        private JobSettings CurrentSettings()
        {
            string logDirectory = txtLogDirectory.Text.Trim();
            return new JobSettings
            {
                Command = txtCommand.Text,
                FolderMode = _folderMode,
                StartEnabled = chkStart.Checked,
                StartTime = TimeOf(dtpStart),
                StopEnabled = chkStop.Checked,
                StopTime = TimeOf(dtpStop),
                VerificationEnabled = chkVerify.Checked,
                LogDirectory = SamePath(logDirectory, ExecutionLog.DefaultDirectory) || logDirectory.Length == 0 ? null : logDirectory,
            };
        }

        private void ApplySettings(JobSettings settings)
        {
            _syncing = true;
            try
            {
                _folderMode = settings.FolderMode;
                cboFolderMode.SelectedIndex = (int)settings.FolderMode;
                chkStart.Checked = settings.StartEnabled;
                dtpStart.Value = DateTime.Today + settings.StartTime;
                chkStop.Checked = settings.StopEnabled;
                dtpStop.Value = DateTime.Today + settings.StopTime;
                chkVerify.Checked = settings.VerificationEnabled;
                txtLogDirectory.Text = settings.LogDirectory ?? ExecutionLog.DefaultDirectory;
            }
            finally
            {
                _syncing = false;
            }
            dtpStart.Enabled = chkStart.Checked;
            dtpStop.Enabled = chkStop.Checked;

            if (txtCommand.Text != settings.Command)
                txtCommand.Text = settings.Command; // parses and updates the controls
            else
                SyncControlsFromCommand();

            _scannedRoot = null;
            ScanFolders(CurrentCommand().Source);
        }

        private static TimeSpan TimeOf(DateTimePicker picker)
        {
            return new TimeSpan(picker.Value.Hour, picker.Value.Minute, 0);
        }

        private void ImportSettings()
        {
            using (var dialog = new OpenFileDialog
            {
                Title = "Import Settings",
                Filter = "Robocopy GUI settings (*.json)|*.json|All files (*.*)|*.*",
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                JobSettings settings;
                try
                {
                    settings = JobSettings.FromJson(File.ReadAllText(dialog.FileName));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "The settings could not be imported.\r\n\r\n" + ex.Message, "Import Settings", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                ApplySettings(settings);
                lblExitCode.Text = string.Empty;
                lblVerification.Text = string.Empty;
                SetStatusLine("Settings imported from " + Path.GetFileName(dialog.FileName), SystemColors.ControlText);
            }
        }

        private void ExportSettings()
        {
            using (var dialog = new SaveFileDialog
            {
                Title = "Export Settings",
                Filter = "Robocopy GUI settings (*.json)|*.json|All files (*.*)|*.*",
                FileName = "robocopy-job.json",
                OverwritePrompt = true,
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                try
                {
                    File.WriteAllText(dialog.FileName, CurrentSettings().ToJson(), new UTF8Encoding(false));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "The settings could not be exported.\r\n\r\n" + ex.Message, "Export Settings", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // ------------------------------------------------------------------
        // Window, tray, misc
        // ------------------------------------------------------------------

        private void ToggleAdvanced()
        {
            pnlAdvanced.Visible = !pnlAdvanced.Visible;
            btnAdvanced.Text = pnlAdvanced.Visible ? "Advanced ▴" : "Advanced ▾";
            FitConfigPanel();
        }

        /// <summary>Gives the configuration area the height it needs, leaving room for the output.</summary>
        private void FitConfigPanel()
        {
            configTable.PerformLayout();
            int wanted = configTable.Height + 2;
            int max = split.Height - split.SplitterWidth - executionHeader.Height - ScaleForDpi(150);
            if (max > split.Panel1MinSize)
                split.SplitterDistance = Math.Max(split.Panel1MinSize, Math.Min(wanted, max));
        }

        private int ScaleForDpi(int pixelsAt96Dpi)
        {
            using (var graphics = CreateGraphics())
                return (int)Math.Round(pixelsAt96Dpi * graphics.DpiY / 96.0);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized)
            {
                if (Visible)
                {
                    Hide();
                    trayIcon.Visible = true;
                }
            }
            else
            {
                _restoreState = WindowState;
            }
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = _restoreState;
            Activate();
            trayIcon.Visible = false;
        }

        private void LoadIcons()
        {
            using (var stream = typeof(MainForm).Assembly.GetManifestResourceStream("RobocopyGui.app.ico"))
            {
                if (stream == null)
                {
                    Icon = SystemIcons.Application;
                    trayIcon.Icon = SystemIcons.Application;
                    return;
                }

                var bytes = new MemoryStream();
                stream.CopyTo(bytes);
                Icon = new Icon(new MemoryStream(bytes.ToArray()));
                trayIcon.Icon = new Icon(new MemoryStream(bytes.ToArray()), SystemInformation.SmallIconSize);
            }
        }

        private void OpenLogFolder()
        {
            try
            {
                if (_lastLogPath != null && File.Exists(_lastLogPath))
                {
                    Process.Start("explorer.exe", "/select,\"" + _lastLogPath + "\"");
                    return;
                }

                string directory = txtLogDirectory.Text.Trim();
                Directory.CreateDirectory(directory);
                Process.Start("explorer.exe", "\"" + directory + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "The log folder could not be opened.\r\n\r\n" + ex.Message, "Open log folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
