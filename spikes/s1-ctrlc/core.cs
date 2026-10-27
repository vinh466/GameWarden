// Prototype Core: launches the Runner with CREATE_NO_WINDOW (own console, no window) per mode.
using System;
using System.Diagnostics;

class Core
{
    static int Main(string[] args)
    {
        string runner = args[0];
        string game = args[1];
        foreach (string mode in new[] { "handler", "handler-reset", "stdin", "ignore-null", "newgroup" })
        {
            Console.WriteLine("=== core: start runner (CREATE_NO_WINDOW), mode=" + mode);
            ProcessStartInfo psi = new ProcessStartInfo(runner, mode + " \"" + game + "\"");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            Process r = Process.Start(psi);
            string output = r.StandardOutput.ReadToEnd();
            r.WaitForExit();
            Console.Write(output);
            Console.WriteLine("core: runner exit code " + r.ExitCode + "; core not affected");
        }
        return 0;
    }
}
