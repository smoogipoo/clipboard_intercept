using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ClipboardHistoryViewer
{
    class Program
    {
        private static NotifyIcon trayIcon;
        private static NativeClipboardForm form;

        [STAThread]
        static void Main(string[] args)
        {
            Form f = new Form();

            trayIcon = new NotifyIcon();
            trayIcon.ContextMenu = new ContextMenu();
            trayIcon.Icon = f.Icon;
            trayIcon.Visible = true;

            form = new NativeClipboardForm();
            form.OnNewData += Form_OnNewData;

            Application.Run(new ApplicationContext());
        }

        private static void Form_OnNewData(IDataObject obj)
        {
            MenuItem item = null;

            if (obj.GetDataPresent(DataFormats.StringFormat))
                item = new TextClipboardMenuItem(obj.GetData(DataFormats.StringFormat));
            else if (obj.GetDataPresent(DataFormats.FileDrop))
                item = new FileListClipboardMenuItem(obj.GetData(DataFormats.FileDrop));

            if (item != null)
                trayIcon.ContextMenu.MenuItems.Add(item);
        }

        private abstract class ClipboardMenuItem : MenuItem
        {
            protected object Data;

            public ClipboardMenuItem(object data)
            {
                Data = data;
                Text = Content.Substring(0, Math.Min(200, Content.Length)).Trim();
            }

            protected abstract string ContentType { get; }
            protected abstract string Content { get; }

            protected override void OnClick(EventArgs e)
            {
                base.OnClick(e);

                form.HandleNewItem = false;
                Set();
                form.HandleNewItem = true;
            }

            protected abstract void Set();
        }

        private class TextClipboardMenuItem : ClipboardMenuItem
        {
            public TextClipboardMenuItem(object data)
                : base(data)
            {
            }

            protected override string ContentType => "text";
            protected override string Content => (string)Data;

            protected override void Set()
            {
                Clipboard.SetText((string)Data);
            }
        }

        private class FileListClipboardMenuItem : ClipboardMenuItem
        {
            public FileListClipboardMenuItem(object data)
                : base(data)
            {
            }

            protected override string ContentType => "files";
            protected override string Content => ((string[])Data).Select(d => Path.GetFileName(d)).Aggregate((w, n) => $"{w}, {n}").ToString();

            protected override void Set()
            {
                StringCollection sc = new StringCollection();
                sc.AddRange((string[])Data);

                Clipboard.SetFileDropList(sc);
            }
        }
    }

    class NativeClipboardForm : NativeWindow, IDisposable
    {
        public event Action<IDataObject> OnNewData;

        private const int WM_DRAWCLIPBOARD = 0x308;
        private const int WM_CHANGECBCHAIN = 0x030D;

        [DllImport("User32.dll")]
        private static extern IntPtr SetClipboardViewer(int hWndNewViewer);
        [DllImport("User32.dll", CharSet = CharSet.Auto)]
        private static extern bool ChangeClipboardChain(IntPtr hWndRemove, IntPtr hWndNewNext);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int SendMessage(IntPtr hwnd, int wMsg, IntPtr wParam, IntPtr lParam);

        private IntPtr nextViewer;
        public bool HandleNewItem = true;

        public NativeClipboardForm()
        {
            CreateHandle(new CreateParams());
            nextViewer = SetClipboardViewer(Handle.ToInt32());
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_DRAWCLIPBOARD:
                    if (HandleNewItem)
                        OnNewData?.Invoke(Clipboard.GetDataObject());
                    SendMessage(nextViewer, m.Msg, m.WParam, m.LParam);
                    break;
                case WM_CHANGECBCHAIN:
                    if (m.WParam == nextViewer)
                        nextViewer = m.LParam;
                    else
                        SendMessage(nextViewer, m.Msg, m.WParam, m.LParam);
                    break;
            }

            base.WndProc(ref m);
        }

        #region Disposal
        ~NativeClipboardForm()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            Dispose(true);
        }

        private bool disposed = false;
        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
                return;
            disposed = true;

            ChangeClipboardChain(Handle, nextViewer);
        }
        #endregion
    }
}
