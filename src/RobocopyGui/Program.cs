using System;
using System.Threading;
using System.Windows.Forms;

namespace RobocopyGui
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += OnThreadException;
            Application.Run(new MainForm());
        }

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            MessageBox.Show("An unexpected error occurred:\r\n\r\n" + e.Exception.Message,
                "Robocopy GUI", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
