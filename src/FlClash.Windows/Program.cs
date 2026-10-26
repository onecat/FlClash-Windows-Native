using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using FlClash.Windows.UI;

namespace FlClash.Windows
{
    internal static class Program
    {
        private const string WindowTitle = "FlClash Windows";
        private const int SwRestore = 9;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string className, string windowName);
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr window);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WindowsVersion
        {
            public uint Size, Major, Minor, Build, Platform;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string ServicePack;
        }

        [DllImport("ntdll.dll", CharSet = CharSet.Unicode)]
        private static extern int RtlGetVersion(ref WindowsVersion version);

        private static bool IsSupportedWindows()
        {
            var os = new WindowsVersion { Size = (uint)Marshal.SizeOf(typeof(WindowsVersion)) };
            return RtlGetVersion(ref os) == 0 && os.Major >= 10 && Environment.Is64BitOperatingSystem;
        }

        [STAThread]
        private static void Main(string[] args)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT || !IsSupportedWindows())
            {
                MessageBox.Show("仅支持 Windows 10/11 x64。", WindowTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            bool created;
            using (var mutex = new Mutex(true, @"Local\FlClashWindows.Native.SingleInstance", out created))
            {
                if (!created)
                {
                    var window = FindWindow(null, WindowTitle);
                    if (window != IntPtr.Zero)
                    {
                        ShowWindow(window, SwRestore);
                        SetForegroundWindow(window);
                    }
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (sender, e) =>
                    MessageBox.Show(e.Exception.Message, "FlClash Windows 错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Run(new MainForm(args.Any(a => a == "--background")));
                GC.KeepAlive(mutex);
            }
        }
    }
}
