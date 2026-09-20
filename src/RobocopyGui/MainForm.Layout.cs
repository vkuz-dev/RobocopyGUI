using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using RobocopyGui.Ui;

namespace RobocopyGui
{
    /// <summary>Control construction and layout. Behaviour lives in MainForm.cs and MainForm.Execution.cs.</summary>
    internal partial class MainForm
    {
        private readonly Container components = new Container();

        private SplitContainer split;
        private TableLayoutPanel configTable;
        private TableLayoutPanel executionHeader;
        private ToolTip toolTip;
        private NotifyIcon trayIcon;
        private Timer scheduleTimer;
        private Timer outputTimer;

        private Control pnlSource;
        private Control pnlDestination;
        private TextBox txtSource;
        private TextBox txtDestination;
        private Button btnBrowseSource;
        private Button btnBrowseDestination;

        private GroupBox grpFolders;
        private ComboBox cboFolderMode;
        private Button btnRefreshFolders;
        private Button btnSelectAll;
        private Button btnSelectNone;
        private CheckedListBox lstFolders;
        private Label lblFolderStatus;

        private GroupBox grpParameters;
        private CheckBox chkMirror;
        private CheckBox chkSecurity;
        private CheckBox chkRestartable;
        private CheckBox chkMultithread;
        private NumericUpDown numThreads;
        private CheckBox chkDryRun;
        private Button btnAdvanced;
        private TableLayoutPanel pnlAdvanced;
        private ComboBox cboSubfolders;
        private NumericUpDown numRetries;
        private NumericUpDown numWait;
        private TextBox txtCopyFlags;
        private TextBox txtDirCopyFlags;
        private CheckBox chkExcludeJunctions;
        private CheckBox chkNoProgress;
        private CheckBox chkNoFileList;
        private CheckBox chkNoDirList;
        private TextBox txtLogDirectory;
        private Button btnBrowseLog;

        private GroupBox grpFilters;
        private TextBox txtInclude;
        private TextBox txtExclude;

        private GroupBox grpSchedule;
        private CheckBox chkStart;
        private DateTimePicker dtpStart;
        private CheckBox chkStop;
        private DateTimePicker dtpStop;
        private CheckBox chkVerify;

        private TextBox txtCommand;
        private Label lblCommandHint;
        private CheckBox chkWrap;

        private Button btnImport;
        private Button btnExport;
        private Button btnRunNow;
        private Button btnStart;

        private Label lblStatus;
        private Label lblExitCode;
        private Label lblVerification;

        private OutputTextBox txtOutput;
        private CheckBox chkAutoScroll;
        private Button btnClearOutput;
        private Button btnOpenLogs;

        private void BuildLayout()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont;
            Text = "Robocopy GUI";
            ClientSize = new Size(1060, 940);
            MinimumSize = new Size(860, 640);
            StartPosition = FormStartPosition.CenterScreen;

            toolTip = new ToolTip(components) { AutoPopDelay = 20000, InitialDelay = 400 };

            configTable = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 4,
                Padding = new Padding(8, 8, 8, 0),
            };
            configTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            configTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            for (int i = 0; i < configTable.RowCount; i++)
                configTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            pnlSource = BuildPathPanel("Source", out txtSource, out btnBrowseSource);
            pnlDestination = BuildPathPanel("Destination", out txtDestination, out btnBrowseDestination);
            configTable.Controls.Add(pnlSource, 0, 0);
            configTable.Controls.Add(pnlDestination, 1, 0);
            configTable.Controls.Add(BuildFoldersGroup(), 0, 1);
            configTable.Controls.Add(BuildParametersGroup(), 1, 1);
            configTable.Controls.Add(BuildFiltersGroup(), 0, 2);
            configTable.Controls.Add(BuildScheduleGroup(), 1, 2);
            AddSpanning(configTable, BuildCommandGroup(), 3);

            // Configuration scrolls if the window is small; the action bar, status and
            // output below the splitter always stay visible.
            var configPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            configPanel.Controls.Add(configTable);

            split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                FixedPanel = FixedPanel.Panel1,
                SplitterWidth = 6,
            };
            split.Panel1.Controls.Add(configPanel);
            split.Panel2.Controls.Add(BuildExecutionPanel());
            Controls.Add(split);

            // Names for screen readers (labels in a TableLayoutPanel are not associated automatically).
            txtSource.AccessibleName = "Source";
            txtDestination.AccessibleName = "Destination";
            lstFolders.AccessibleName = "Source folders";
            txtInclude.AccessibleName = "Include files";
            txtExclude.AccessibleName = "Exclude files";
            txtCommand.AccessibleName = "Robocopy command";
            txtOutput.AccessibleName = "Robocopy output";

            trayIcon = new NotifyIcon(components) { Text = "Robocopy GUI", Visible = false };
            scheduleTimer = new Timer(components) { Interval = 1000 };
            outputTimer = new Timer(components) { Interval = 150 };

            ResumeLayout(false);
            PerformLayout();
        }

        private static void AddSpanning(TableLayoutPanel table, Control control, int row)
        {
            table.Controls.Add(control, 0, row);
            table.SetColumnSpan(control, table.ColumnCount);
        }

        private Control BuildPathPanel(string caption, out TextBox box, out Button browse)
        {
            var panel = NewTable(2, new Padding(3, 0, 3, 6));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var label = new Label { Text = caption, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 3) };
            panel.Controls.Add(label, 0, 0);
            panel.SetColumnSpan(label, 2);

            box = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 0, 6, 0) };
            browse = new Button { Text = "Browse…", AutoSize = true, Margin = new Padding(0) };
            panel.Controls.Add(box, 0, 1);
            panel.Controls.Add(browse, 1, 1);
            toolTip.SetToolTip(box, "Local path or UNC path (\\\\server\\share\\folder).");
            return panel;
        }

        private Control BuildFoldersGroup()
        {
            grpFolders = NewGroup("Source folders");
            grpFolders.Dock = DockStyle.Fill;
            grpFolders.Height = 250;

            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 3 };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            cboFolderMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, Anchor = AnchorStyles.Left };
            cboFolderMode.Items.AddRange(new object[] { "Include selected", "Exclude selected" });
            btnSelectAll = new Button { Text = "All", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(44, 0) };
            btnSelectNone = new Button { Text = "None", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(44, 0) };
            btnRefreshFolders = new Button { Text = "Refresh", AutoSize = true };

            table.Controls.Add(new Label { Text = "Folder mode:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            table.Controls.Add(cboFolderMode, 1, 0);
            table.Controls.Add(btnSelectAll, 3, 0);
            table.Controls.Add(btnSelectNone, 4, 0);
            table.Controls.Add(btnRefreshFolders, 5, 0);

            lstFolders = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
            table.Controls.Add(lstFolders, 0, 1);
            table.SetColumnSpan(lstFolders, 6);

            lblFolderStatus = new Label { AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right, ForeColor = SystemColors.GrayText };
            table.Controls.Add(lblFolderStatus, 0, 2);
            table.SetColumnSpan(lblFolderStatus, 6);

            toolTip.SetToolTip(cboFolderMode,
                "Include selected: only the checked folders are copied (unchecked folders are excluded with /XD).\r\n" +
                "Exclude selected: everything is copied except the checked folders.\r\n" +
                "Only immediate subfolders of the source are listed. Files directly in the source root follow the file filters.");
            toolTip.SetToolTip(btnRefreshFolders, "Rescan the immediate subfolders of the source.");

            grpFolders.Controls.Add(table);
            return grpFolders;
        }

        private Control BuildParametersGroup()
        {
            grpParameters = NewGroup("Robocopy parameters");
            grpParameters.Dock = DockStyle.Fill;
            grpParameters.AutoSize = true;
            grpParameters.AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var table = NewTable(1, Padding.Empty);
            table.Dock = DockStyle.Fill;
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            chkMirror = NewCheck("Mirror (/MIR)", "Mirror the source tree: copies subfolders including empty ones and deletes destination files and folders that no longer exist in the source.");
            chkSecurity = NewCheck("Copy ACL / metadata (/COPY:DATS)", "Copy NTFS security (ACLs) in addition to data, attributes and timestamps. Fine-tune the /COPY flags under Advanced.");
            chkRestartable = NewCheck("Restartable mode (/Z)", "Copy files in restartable mode, so interrupted large files resume where they stopped.");
            chkDryRun = NewCheck("Dry run (/L)", "Robocopy only lists what it would do. Nothing is copied, deleted or time-stamped.");

            chkMultithread = NewCheck("Multithreaded (/MT)", "Copy with multiple threads. Robocopy accepts 1–128; without a number it uses 8.");
            chkMultithread.Margin = new Padding(3, 5, 3, 3);
            numThreads = new NumericUpDown { Minimum = 1, Maximum = 128, Value = 16, Width = 60, Margin = new Padding(3, 3, 3, 3) };
            var threadRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            threadRow.Controls.Add(chkMultithread);
            threadRow.Controls.Add(new Label { Text = "Threads:", AutoSize = true, Margin = new Padding(12, 7, 0, 0) });
            threadRow.Controls.Add(numThreads);

            btnAdvanced = new Button { Text = "Advanced ▾", AutoSize = true, Margin = new Padding(3, 8, 3, 3) };

            table.Controls.Add(chkMirror);
            table.Controls.Add(chkSecurity);
            table.Controls.Add(chkRestartable);
            table.Controls.Add(threadRow);
            table.Controls.Add(chkDryRun);
            table.Controls.Add(btnAdvanced);
            table.Controls.Add(BuildAdvancedPanel());

            grpParameters.Controls.Add(table);
            return grpParameters;
        }

        private Control BuildAdvancedPanel()
        {
            pnlAdvanced = NewTable(4, new Padding(0, 6, 0, 0));
            pnlAdvanced.Dock = DockStyle.Fill;
            pnlAdvanced.Visible = false;
            pnlAdvanced.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pnlAdvanced.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            pnlAdvanced.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pnlAdvanced.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            cboSubfolders = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left | AnchorStyles.Right };
            cboSubfolders.Items.AddRange(new object[]
            {
                "Source folder only (no /S or /E)",
                "Subfolders, skip empty ones (/S)",
                "Subfolders including empty ones (/E)",
            });
            AddRow(pnlAdvanced, 0, "Subfolders:", cboSubfolders, 3);

            numRetries = new NumericUpDown { Minimum = 0, Maximum = 1000000, Width = 80, Anchor = AnchorStyles.Left };
            numWait = new NumericUpDown { Minimum = 0, Maximum = 100000, Width = 80, Anchor = AnchorStyles.Left };
            AddRow(pnlAdvanced, 1, "Retries (/R):", numRetries, 1);
            AddRow(pnlAdvanced, 1, "Wait seconds (/W):", numWait, 1, 2);
            toolTip.SetToolTip(numRetries, "Retries on failed copies. Robocopy's own default is 1,000,000.");
            toolTip.SetToolTip(numWait, "Seconds to wait between retries. Robocopy's own default is 30.");

            // MinimumSize: an auto-sized TextBox would otherwise shrink to its (empty) preferred width.
            txtCopyFlags = new TextBox { CharacterCasing = CharacterCasing.Upper, MinimumSize = new Size(80, 0), Anchor = AnchorStyles.Left };
            txtDirCopyFlags = new TextBox { CharacterCasing = CharacterCasing.Upper, MinimumSize = new Size(80, 0), Anchor = AnchorStyles.Left };
            AddRow(pnlAdvanced, 2, "File copy (/COPY:):", txtCopyFlags, 1);
            AddRow(pnlAdvanced, 2, "Folder copy (/DCOPY:):", txtDirCopyFlags, 1, 2);
            toolTip.SetToolTip(txtCopyFlags, "D=Data A=Attributes T=Timestamps S=Security (ACLs) O=Owner U=Auditing X=skip alternate streams.\r\nRobocopy's default is DAT.");
            toolTip.SetToolTip(txtDirCopyFlags, "D=Data A=Attributes T=Timestamps E=Extended attributes X=skip alternate streams.\r\nRobocopy's default is DA.");

            chkExcludeJunctions = NewCheck("Exclude junctions and symlinks (/XJ)", "Do not follow junction points and symbolic links. Avoids copying loops such as 'Application Data'.");
            chkNoProgress = NewCheck("No percentage progress (/NP)", "Do not print per-file percentage progress. Keeps the output and the log readable.");
            chkNoFileList = NewCheck("Don't list files (/NFL)", "Do not list individual file names in the output.");
            chkNoDirList = NewCheck("Don't list folders (/NDL)", "Do not list individual folder names in the output.");
            AddCheckPair(pnlAdvanced, 3, chkExcludeJunctions, chkNoProgress);
            AddCheckPair(pnlAdvanced, 4, chkNoFileList, chkNoDirList);

            txtLogDirectory = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };
            btnBrowseLog = new Button { Text = "…", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(30, 0), Anchor = AnchorStyles.Left };
            AddRow(pnlAdvanced, 5, "Execution log folder:", txtLogDirectory, 2);
            pnlAdvanced.Controls.Add(btnBrowseLog, 3, 5);
            toolTip.SetToolTip(txtLogDirectory, "Every run writes its own robocopy_<date>_<time>.log file here. Not part of the robocopy command.");
            return pnlAdvanced;
        }

        private Control BuildFiltersGroup()
        {
            grpFilters = NewGroup("File filters");
            grpFilters.Dock = DockStyle.Fill;
            grpFilters.AutoSize = true;
            grpFilters.AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var table = NewTable(2, Padding.Empty);
            table.Dock = DockStyle.Fill;
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            txtInclude = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };
            txtExclude = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };
            AddRow(table, 0, "Include:", txtInclude, 1);
            AddRow(table, 1, "Exclude (/XF):", txtExclude, 1);
            var hint = new Label
            {
                Text = "Separate patterns with ;  e.g. *.docx;*.xlsx. Leave Include empty to copy all files.",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
            };
            table.Controls.Add(hint, 0, 2);
            table.SetColumnSpan(hint, 2);

            toolTip.SetToolTip(txtInclude, "File name patterns to copy. Written directly after the destination in the command.");
            toolTip.SetToolTip(txtExclude, "File name patterns to skip (/XF).");
            grpFilters.Controls.Add(table);
            return grpFilters;
        }

        private Control BuildScheduleGroup()
        {
            grpSchedule = NewGroup("Schedule / Verification");
            grpSchedule.Dock = DockStyle.Fill;
            grpSchedule.AutoSize = true;
            grpSchedule.AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var table = NewTable(5, Padding.Empty);
            table.Dock = DockStyle.Fill;
            for (int i = 0; i < 4; i++)
                table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            chkStart = NewCheck("Start at", "When checked, Start waits until this time (today or tomorrow) before launching robocopy.");
            chkStop = NewCheck("Stop at", "When checked, robocopy is asked to stop gracefully (Ctrl+C) at this time. Run the job again later to continue.");
            dtpStart = NewTimePicker();
            dtpStop = NewTimePicker();
            chkStart.Anchor = chkStop.Anchor = AnchorStyles.Left;
            chkStop.Margin = new Padding(24, 3, 3, 3);
            table.Controls.Add(chkStart, 0, 0);
            table.Controls.Add(dtpStart, 1, 0);
            table.Controls.Add(chkStop, 2, 0);
            table.Controls.Add(dtpStop, 3, 0);

            chkVerify = NewCheck("Verify destination size after a successful run",
                "Before copying, the selected source files are totalled in bytes. After a successful run the destination is totalled the same way and compared.\r\n" +
                "A size check only – not a content comparison. Skipped for dry runs and unsuccessful or stopped runs.");
            table.Controls.Add(chkVerify, 0, 1);
            table.SetColumnSpan(chkVerify, 5);

            var hint = new Label
            {
                Text = "Scheduled runs require this application to stay open (it can sit in the tray).",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
            };
            table.Controls.Add(hint, 0, 2);
            table.SetColumnSpan(hint, 5);

            grpSchedule.Controls.Add(table);
            return grpSchedule;
        }

        private Control BuildCommandGroup()
        {
            var group = NewGroup("Robocopy command");
            group.Dock = DockStyle.Fill;
            group.AutoSize = true;
            group.AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var table = NewTable(2, Padding.Empty);
            table.Dock = DockStyle.Fill;
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            txtCommand = new TextBox
            {
                Multiline = true,
                WordWrap = true,
                AcceptsReturn = false,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 10F),
                Height = 62,
                Dock = DockStyle.Fill,
                MaxLength = 0,
            };
            table.Controls.Add(txtCommand, 0, 0);
            table.SetColumnSpan(txtCommand, 2);

            lblCommandHint = new Label
            {
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
            };
            chkWrap = NewCheck("Wrap", "Wrap the command visually. Uncheck to scroll horizontally. The command stays a single line either way.");
            chkWrap.Checked = true;
            table.Controls.Add(lblCommandHint, 0, 1);
            table.Controls.Add(chkWrap, 1, 1);

            group.Controls.Add(table);
            return group;
        }

        private Control BuildActionBar()
        {
            var table = NewTable(5, new Padding(3, 6, 3, 3));
            table.Dock = DockStyle.Fill;
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            btnImport = new Button { Text = "Import Settings…", AutoSize = true, Anchor = AnchorStyles.Left };
            btnExport = new Button { Text = "Export Settings…", AutoSize = true, Anchor = AnchorStyles.Left };
            btnRunNow = new Button { Text = "Run now", AutoSize = true, MinimumSize = new Size(96, 32), Visible = false, Anchor = AnchorStyles.Right };
            btnStart = new Button { Text = "Start", MinimumSize = new Size(120, 32), AutoSize = true, Font = new Font(Font, FontStyle.Bold), Anchor = AnchorStyles.Right };
            table.Controls.Add(btnImport, 0, 0);
            table.Controls.Add(btnExport, 1, 0);
            table.Controls.Add(btnRunNow, 3, 0);
            table.Controls.Add(btnStart, 4, 0);
            toolTip.SetToolTip(btnRunNow, "Start the scheduled job now instead of waiting for the start time.");
            return table;
        }

        private Control BuildStatusPanel()
        {
            var table = NewTable(1, new Padding(3, 0, 3, 6));
            table.Dock = DockStyle.Fill;
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            lblStatus = new Label { AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right, Font = new Font(Font.FontFamily, Font.Size * 1.1f, FontStyle.Bold), Margin = new Padding(3, 3, 3, 2) };
            lblExitCode = new Label { AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(3, 0, 3, 2) };
            lblVerification = new Label { AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(3, 0, 3, 2) };
            table.Controls.Add(lblStatus, 0, 0);
            table.Controls.Add(lblExitCode, 0, 1);
            table.Controls.Add(lblVerification, 0, 2);
            return table;
        }

        /// <summary>Lower pane: Import/Export/Start, status, and the robocopy output.</summary>
        private Control BuildExecutionPanel()
        {
            executionHeader = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Margin = Padding.Empty,
            };
            executionHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            executionHeader.Controls.Add(BuildActionBar(), 0, 0);
            executionHeader.Controls.Add(BuildStatusPanel(), 0, 1);

            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(8, 0, 8, 8) };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            table.Controls.Add(executionHeader, 0, 0);
            table.Controls.Add(BuildOutputPanel(), 0, 1);
            return table;
        }

        private Control BuildOutputPanel()
        {
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 2, Margin = Padding.Empty };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            table.Controls.Add(new Label { Text = "Robocopy output", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Anchor = AnchorStyles.Left }, 0, 0);
            chkAutoScroll = NewCheck("Auto-scroll", "Keep the newest output in view. Uncheck to read or select earlier output while robocopy runs.");
            chkAutoScroll.Checked = true;
            chkAutoScroll.Anchor = AnchorStyles.Right;
            btnClearOutput = new Button { Text = "Clear", AutoSize = true, Anchor = AnchorStyles.Right };
            btnOpenLogs = new Button { Text = "Open log folder", AutoSize = true, Anchor = AnchorStyles.Right };
            table.Controls.Add(chkAutoScroll, 2, 0);
            table.Controls.Add(btnClearOutput, 3, 0);
            table.Controls.Add(btnOpenLogs, 4, 0);

            txtOutput = new OutputTextBox { Dock = DockStyle.Fill, Font = new Font("Consolas", 9F), BackColor = SystemColors.Window };
            table.Controls.Add(txtOutput, 0, 1);
            table.SetColumnSpan(txtOutput, 5);
            return table;
        }

        // ------------------------------------------------------------------
        // Small factories
        // ------------------------------------------------------------------

        private static GroupBox NewGroup(string text)
        {
            return new GroupBox { Text = text, Padding = new Padding(8, 6, 8, 8), Margin = new Padding(3, 3, 3, 6) };
        }

        private static TableLayoutPanel NewTable(int columns, Padding margin)
        {
            return new TableLayoutPanel
            {
                ColumnCount = columns,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = margin,
                Dock = DockStyle.Fill,
            };
        }

        private CheckBox NewCheck(string text, string tip)
        {
            var box = new CheckBox { Text = text, AutoSize = true };
            if (tip != null)
                toolTip.SetToolTip(box, tip);
            return box;
        }

        private static DateTimePicker NewTimePicker()
        {
            return new DateTimePicker
            {
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "HH:mm",
                ShowUpDown = true,
                Width = 70,
                Anchor = AnchorStyles.Left,
            };
        }

        private static void AddRow(TableLayoutPanel table, int row, string caption, Control control, int span, int column = 0)
        {
            table.Controls.Add(new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(column == 0 ? 3 : 12, 3, 3, 3) }, column, row);
            table.Controls.Add(control, column + 1, row);
            if (span > 1)
                table.SetColumnSpan(control, span);
        }

        private static void AddCheckPair(TableLayoutPanel table, int row, CheckBox left, CheckBox right)
        {
            table.Controls.Add(left, 0, row);
            table.SetColumnSpan(left, 2);
            table.Controls.Add(right, 2, row);
            table.SetColumnSpan(right, 2);
        }
    }
}
