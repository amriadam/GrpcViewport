using System;
using System.Diagnostics;
using System.Net;
using System.Windows.Forms;

namespace GrpcViewport.WinForms
{
    internal static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            ServicePointManager.DefaultConnectionLimit = 16;

            Process host;
            try
            {
                host = HostLauncher.Start(TimeSpan.FromSeconds(15));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "GrpcViewport", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                Application.Run(new Form1());
            }
            finally
            {
                // Normal exit: stop the host. (A crash is covered by --parent-pid.)
                try
                {
                    if (!host.HasExited) host.Kill();
                }
                catch { /* already gone */ }
                host.Dispose();
            }
        }


    }
}
