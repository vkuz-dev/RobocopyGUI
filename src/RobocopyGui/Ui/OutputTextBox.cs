using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RobocopyGui.Ui
{
    /// <summary>
    /// Read-only, selectable output view. Appends in batches, optionally keeps the
    /// user's scroll position and selection, and drops the oldest text once it grows
    /// very large (the execution log always has the complete output).
    /// </summary>
    internal sealed class OutputTextBox : TextBox
    {
        private const int MaxChars = 1000000;
        private const int TrimToChars = 700000;
        private const string TrimNotice = "[... earlier output removed from this view; the log file contains everything ...]\r\n";

        public OutputTextBox()
        {
            Multiline = true;
            ReadOnly = true;
            WordWrap = false;
            ScrollBars = ScrollBars.Both;
            HideSelection = false;
            MaxLength = 0; // multiline edit control maximum
        }

        public bool AutoScroll { get; set; } = true;

        /// <summary>True if the view is empty or its text ends with a line break.</summary>
        public bool EndsWithNewLine { get; private set; } = true;

        protected override void OnTextChanged(EventArgs e)
        {
            if (TextLength == 0)
                EndsWithNewLine = true;
            base.OnTextChanged(e);
        }

        public void AppendOutput(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            EndsWithNewLine = text[text.Length - 1] == '\n';
            if (TextLength + text.Length > MaxChars)
            {
                Trim(text);
                return;
            }

            if (AutoScroll)
            {
                AppendText(text);
                return;
            }

            // Keep the user's view: remember selection and first visible line.
            int selectionStart = SelectionStart;
            int selectionLength = SelectionLength;
            int firstVisible = (int)SendMessage(Handle, EmGetFirstVisibleLine, IntPtr.Zero, IntPtr.Zero);

            SendMessage(Handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
            try
            {
                AppendText(text);
                Select(selectionStart, selectionLength);
                int nowVisible = (int)SendMessage(Handle, EmGetFirstVisibleLine, IntPtr.Zero, IntPtr.Zero);
                SendMessage(Handle, EmLineScroll, IntPtr.Zero, (IntPtr)(firstVisible - nowVisible));
            }
            finally
            {
                SendMessage(Handle, WmSetRedraw, (IntPtr)1, IntPtr.Zero);
                Invalidate();
            }
        }

        private void Trim(string appended)
        {
            string all = Text + appended;
            int cut = all.Length - TrimToChars;
            int lineStart = all.IndexOf('\n', Math.Max(0, cut));
            cut = lineStart < 0 ? cut : lineStart + 1;
            Text = TrimNotice + all.Substring(cut);
            if (AutoScroll)
            {
                SelectionStart = TextLength;
                ScrollToCaret();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.A)
            {
                SelectAll();
                e.SuppressKeyPress = true;
            }
            base.OnKeyDown(e);
        }

        private const int WmSetRedraw = 0x000B;
        private const int EmGetFirstVisibleLine = 0x00CE;
        private const int EmLineScroll = 0x00B6;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}
