using System.Drawing;
using System.Windows.Forms;

namespace RobocopyGui.Ui
{
    /// <summary>
    /// A small confirmation box with custom button labels (e.g. [Stop] [Cancel]).
    /// Cancel is the default button, so an accidental Enter does not confirm.
    /// </summary>
    internal static class ConfirmDialog
    {
        public static bool Show(IWin32Window owner, string title, string heading, string message, string confirmText)
        {
            using (var form = new Form())
            {
                form.SuspendLayout();
                form.AutoScaleDimensions = new SizeF(96F, 96F);
                form.Text = title;
                form.Font = SystemFonts.MessageBoxFont;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = owner == null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ShowInTaskbar = false;
                form.AutoScaleMode = AutoScaleMode.Dpi;
                form.AutoSize = true;
                form.AutoSizeMode = AutoSizeMode.GrowAndShrink;

                var layout = new TableLayoutPanel
                {
                    AutoSize = true,
                    ColumnCount = 2,
                    Padding = new Padding(16, 16, 16, 12),
                    Dock = DockStyle.Fill,
                };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

                var icon = new PictureBox
                {
                    Image = SystemIcons.Warning.ToBitmap(),
                    SizeMode = PictureBoxSizeMode.AutoSize,
                    Margin = new Padding(0, 0, 12, 0),
                };
                layout.Controls.Add(icon, 0, 0);
                layout.SetRowSpan(icon, 2);

                layout.Controls.Add(new Label
                {
                    Text = heading,
                    AutoSize = true,
                    Font = new Font(form.Font.FontFamily, form.Font.Size * 1.25f, FontStyle.Bold),
                    Margin = new Padding(0, 0, 0, 8),
                    MaximumSize = new Size(420, 0),
                }, 1, 0);
                layout.Controls.Add(new Label
                {
                    Text = message,
                    AutoSize = true,
                    MaximumSize = new Size(420, 0),
                    Margin = new Padding(0, 0, 0, 16),
                }, 1, 1);

                var confirm = new Button { Text = confirmText, AutoSize = true, MinimumSize = new Size(88, 0), DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Cancel", AutoSize = true, MinimumSize = new Size(88, 0), DialogResult = DialogResult.Cancel };
                var buttons = new FlowLayoutPanel
                {
                    FlowDirection = FlowDirection.RightToLeft,
                    AutoSize = true,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0),
                };
                buttons.Controls.Add(cancel);
                buttons.Controls.Add(confirm);
                layout.Controls.Add(buttons, 0, 2);
                layout.SetColumnSpan(buttons, 2);

                form.Controls.Add(layout);
                form.ResumeLayout(false);
                form.PerformLayout();
                form.AcceptButton = cancel;
                form.CancelButton = cancel;
                form.Shown += (s, e) => cancel.Focus();

                return form.ShowDialog(owner) == DialogResult.OK;
            }
        }
    }
}
