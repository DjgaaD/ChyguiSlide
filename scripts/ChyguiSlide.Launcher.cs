// Tiny .NET Framework launcher — no extra runtime to ship (built into Windows).
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        try
        {
            var root = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                       ?? AppDomain.CurrentDomain.BaseDirectory;
            var appDir = Path.Combine(root, "app");
            var appExe = Path.Combine(appDir, "ChyguiSlide.exe");

            if (!File.Exists(appExe))
            {
                MessageBox.Show(
                    "Не найден файл:\n" + appExe + "\n\nРаспакуйте архив целиком.",
                    "Чугуй Слайды",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = appExe,
                WorkingDirectory = appDir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Чугуй Слайды", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
