using System;
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
                form.Text = title;
                form.Font = SystemFonts.MessageBoxFont;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = owner == null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ShowInTaskbar = false;
                form.AutoSize = true;
                form.AutoSizeMode = AutoSizeMode.GrowAndShrink;

                Font font = form.Font;
                // Text wraps at roughly 60 characters of the dialog font.
                var wrap = new Size(TextRenderer.MeasureText(new string('x', 60), font).Width, 0);

                var layout = new TableLayoutPanel
                {
                    AutoSize = true,
                    ColumnCount = 2,
                    Padding = Sizing.Pad(font, 5, 5, 5, 4),
                    Dock = DockStyle.Fill,
                };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

                var icon = new PictureBox
                {
                    Image = SystemIcons.Warning.ToBitmap(),
                    SizeMode = PictureBoxSizeMode.AutoSize,
                    Margin = Sizing.Pad(font, 0, 0, 4, 0),
                };
                layout.Controls.Add(icon, 0, 0);
                layout.SetRowSpan(icon, 2);

                layout.Controls.Add(new Label
                {
                    Text = heading,
                    AutoSize = true,
                    Font = new Font(font.FontFamily, font.Size * 1.25f, FontStyle.Bold),
                    Margin = Sizing.Pad(font, 0, 0, 0, 3),
                    MaximumSize = wrap,
                }, 1, 0);
                layout.Controls.Add(new Label
                {
                    Text = message,
                    AutoSize = true,
                    MaximumSize = wrap,
                    Margin = Sizing.Pad(font, 0, 0, 0, 5),
                }, 1, 1);

                // Equal-width buttons, wide enough for either caption.
                var confirm = new Button { Text = confirmText, AutoSize = true, Padding = Sizing.Pad(font, 2, 0, 2, 0), DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Cancel", AutoSize = true, Padding = Sizing.Pad(font, 2, 0, 2, 0), DialogResult = DialogResult.Cancel };
                form.Controls.Add(confirm);
                form.Controls.Add(cancel);
                var buttonSize = new Size(Math.Max(confirm.PreferredSize.Width, cancel.PreferredSize.Width), 0);
                confirm.MinimumSize = cancel.MinimumSize = buttonSize;
                var buttons = new FlowLayoutPanel
                {
                    FlowDirection = FlowDirection.RightToLeft,
                    AutoSize = true,
                    Dock = DockStyle.Fill,
                    Margin = Padding.Empty,
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
