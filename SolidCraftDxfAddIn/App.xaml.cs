using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using ExportDxfAddIn.ViewModels;
using ExportDxfAddIn.Views;

namespace ExportDxfAddIn
{
    public partial class App : System.Windows.Application
    {
        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        private const int ATTACH_PARENT_PROCESS = -1;
        private const int STD_OUTPUT_HANDLE = -11;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (e.Args.Contains("--test"))
            {
                try
                {
                    AttachConsole(ATTACH_PARENT_PROCESS);
                    IntPtr stdOut = GetStdHandle(STD_OUTPUT_HANDLE);
                    if (stdOut != IntPtr.Zero && stdOut != new IntPtr(-1))
                    {
                        var fs = new FileStream(new Microsoft.Win32.SafeHandles.SafeFileHandle(stdOut, ownsHandle: false), FileAccess.Write);
                        var writer = new StreamWriter(fs, new System.Text.UTF8Encoding(false)) { AutoFlush = true };
                        Console.SetOut(writer);
                        Console.SetError(writer);
                    }
                }
                catch
                {
                    // Bỏ qua lỗi attach console nếu môi trường không có console
                }

                int exitCode = await TestRunner.RunAllTestsAsync(e.Args);
                Environment.Exit(exitCode);
                return;
            }

            var mainWindow = new MainWindow();
            var vm = new MainViewModel();
            mainWindow.DataContext = vm;
            MainWindow = mainWindow;
            mainWindow.Show();

            if (e.Args.Length > 0 && File.Exists(e.Args[0]))
            {
                _ = vm.LoadDesignFromFileAsync(e.Args[0]);
            }
        }
    }
}
