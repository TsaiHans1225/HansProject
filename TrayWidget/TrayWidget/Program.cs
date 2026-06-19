using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TrayWidget
{
    static class Program
    {
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TrayWidget", "crash.log");

        [STAThread]
        static void Main()
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(LogPath)); } catch { }

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => LogException("ThreadException", e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => LogException("UnhandledException", e.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                LogException("UnobservedTaskException", e.Exception);
                e.SetObserved();
            };

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayWidgetForm());
        }

        private static void LogException(string source, Exception ex)
        {
            try
            {
                var msg = string.Format("[{0}] {1}\r\n{2}\r\n\r\n",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    source,
                    ex == null ? "(null exception)" : ex.ToString());
                File.AppendAllText(LogPath, msg);
            }
            catch { }
        }
    }
}
