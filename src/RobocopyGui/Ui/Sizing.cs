using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace RobocopyGui.Ui
{
    /// <summary>
    /// Font-relative sizing. Nothing in the UI uses fixed pixel values: spacing is measured in
    /// units of the font's line height and fixed-width boxes are measured from their content,
    /// so the layout follows the system font and DPI.
    /// </summary>
    internal static class Sizing
    {
        /// <summary>One spacing unit: a fifth of the font's line height.</summary>
        public static int Unit(Font font)
        {
            return Math.Max(1, font.Height / 5);
        }

        /// <summary>Margin or padding in spacing units.</summary>
        public static Padding Pad(Font font, int left, int top, int right, int bottom)
        {
            int unit = Unit(font);
            return new Padding(left * unit, top * unit, right * unit, bottom * unit);
        }

        /// <summary>
        /// Gives a box that does not size its own width (combo, spin, time picker, short text box)
        /// exactly the width of its widest sample text. Call once the control has its final font.
        /// </summary>
        public static void FitWidth(Control control, bool hasButton, params string[] samples)
        {
            int width = samples.Max(s => TextRenderer.MeasureText(s, control.Font).Width)
                + 2 * SystemInformation.Border3DSize.Width;
            if (hasButton)
                width += SystemInformation.VerticalScrollBarWidth;
            control.MinimumSize = new Size(width, 0);
            control.Width = width;
        }

        /// <summary>Height of a multi-line text box showing <paramref name="lines"/> lines.</summary>
        public static int LinesHeight(Font font, int lines)
        {
            return font.Height * lines + 2 * SystemInformation.Border3DSize.Height;
        }
    }
}
