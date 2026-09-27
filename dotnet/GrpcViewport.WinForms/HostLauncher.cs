using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace GrpcViewport.WinForms
{
    /// <summary>
    /// Starts GrpcViewport.Host.exe as a child process and blocks until it prints READY.
    /// </summary>
    internal static class HostLauncher
    {
        public static Process Start(TimeSpan timeout)
        {
            string exe = FindHostExe();

            var process = new Process
            {
                StartInfo = new ProcessStartInfo(exe, "--parent-pid " + Process.GetCurrentProcess().Id)
                {
                    UseShellExecute = false,          // required for redirection
                    RedirectStandardOutput = true,    // we read READY from here
                    RedirectStandardError = true,     // error text (e.g. port busy)
                    CreateNoWindow = true,            // no console window
                    WorkingDirectory = Path.GetDirectoryName(exe),
                },
                EnableRaisingEvents = true,           // so Exited fires
            };

            var ready = new TaskCompletionSource<bool>();
            var errors = new StringBuilder();

            // Async reading keeps both pipes drained for the host's whole lifetime.
            process.OutputDataReceived += (s, e) =>
            {
                if (e.Data == "READY") ready.TrySetResult(true);
            };
            process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null) lock (errors) errors.AppendLine(e.Data);
            };
            process.Exited += (s, e) =>
            {
                process.WaitForExit(); // let the stderr reader finish
                string text;
                lock (errors) text = errors.ToString().Trim();
                ready.TrySetException(new InvalidOperationException(
                    "GrpcViewport.Host exited during startup (exit code " + process.ExitCode + ").\n" + text));
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                if (!ready.Task.Wait(timeout))
                {
                    try { process.Kill(); } catch { /* already gone */ }
                    throw new TimeoutException("GrpcViewport.Host did not report READY within " + timeout.TotalSeconds + " s.");
                }
            }
            catch (AggregateException ex)
            {
                throw ex.InnerException ?? ex; // surface the real error, not the wrapper
            }

            return process;
        }

        private static string FindHostExe()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            string[] candidates =
            [
                // Shipped layout (step 9): <app folder>\host\GrpcViewport.Host.exe
                Path.Combine(baseDir, "host", "GrpcViewport.Host.exe"),

                // Development, old-style csproj: WinForms\bin\Debug\  -> ..\..\..\GrpcViewport.Host\...
                Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\GrpcViewport.Host\bin\Debug\net8.0\GrpcViewport.Host.exe")),

                // Development, SDK-style csproj: WinForms\bin\Debug\net48\ -> ..\..\..\..\GrpcViewport.Host\...
                Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\GrpcViewport.Host\bin\Debug\net8.0\GrpcViewport.Host.exe")),
            ];

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate)) return candidate;
            }

            throw new FileNotFoundException(
                "GrpcViewport.Host.exe not found. Build GrpcViewport.Host first. Looked in:\n" +
                string.Join("\n", candidates));
        }
    }
}