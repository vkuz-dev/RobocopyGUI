using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RobocopyGui.Ui
{
    /// <summary>
    /// The standard Windows folder picker (the Explorer-style dialog, which accepts
    /// typed or pasted UNC paths). Falls back to FolderBrowserDialog if unavailable.
    /// </summary>
    internal static class FolderPicker
    {
        public static string Show(IWin32Window owner, string title, string initialPath)
        {
            try
            {
                return ShowFileDialog(owner, title, initialPath);
            }
            catch (Exception ex) when (ex is COMException || ex is InvalidCastException)
            {
                using (var dialog = new FolderBrowserDialog { Description = title, SelectedPath = initialPath ?? string.Empty })
                    return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.SelectedPath : null;
            }
        }

        private static string ShowFileDialog(IWin32Window owner, string title, string initialPath)
        {
            var dialog = (IFileDialog)new FileOpenDialog();
            try
            {
                uint options;
                dialog.GetOptions(out options);
                dialog.SetOptions(options | FosPickFolders | FosForceFileSystem | FosNoChangeDir | FosPathMustExist);
                dialog.SetTitle(title);

                IShellItem folder;
                var shellItemId = typeof(IShellItem).GUID;
                if (!string.IsNullOrWhiteSpace(initialPath)
                    && SHCreateItemFromParsingName(initialPath.Trim(), IntPtr.Zero, ref shellItemId, out folder) == 0)
                {
                    dialog.SetFolder(folder);
                }

                int hr = dialog.Show(owner == null ? IntPtr.Zero : owner.Handle);
                if (hr == ErrorCancelled)
                    return null;
                Marshal.ThrowExceptionForHR(hr);

                IShellItem result;
                dialog.GetResult(out result);
                string path;
                result.GetDisplayName(SigdnFileSysPath, out path);
                return path;
            }
            finally
            {
                Marshal.ReleaseComObject(dialog);
            }
        }

        private const uint FosNoChangeDir = 0x8;
        private const uint FosPickFolders = 0x20;
        private const uint FosForceFileSystem = 0x40;
        private const uint FosPathMustExist = 0x800;
        private const uint SigdnFileSysPath = 0x80058000;
        private const int ErrorCancelled = unchecked((int)0x800704C7);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid riid, out IShellItem item);

        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        private class FileOpenDialog
        {
        }

        [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint count, IntPtr filterSpec);
            void SetFileTypeIndex(uint index);
            void GetFileTypeIndex(out uint index);
            void Advise(IntPtr events, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint options);
            void GetOptions(out uint options);
            void SetDefaultFolder(IShellItem item);
            void SetFolder(IShellItem item);
            void GetFolder(out IShellItem item);
            void GetCurrentSelection(out IShellItem item);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem item);
            void AddPlace(IShellItem item, int placement);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr filter);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid riid, out IntPtr result);
            void GetParent(out IShellItem parent);
            void GetDisplayName(uint type, [MarshalAs(UnmanagedType.LPWStr)] out string name);
            void GetAttributes(uint mask, out uint attributes);
            void Compare(IShellItem other, uint hint, out int order);
        }
    }
}
