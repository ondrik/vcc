// z3.exe stand-in for Boogie: starts the real Z3 (VCC_Z3, or z3 on PATH) and translates the
// legacy parameter names in its command line and input; see Z3Compat.
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Microsoft.Research.Vcc.Z3Shim
{
  static class Program
  {
    static int Main(string[] args)
    {
      var z3 = Environment.GetEnvironmentVariable("VCC_Z3");
      if (string.IsNullOrEmpty(z3)) z3 = "z3";
      var psi = new ProcessStartInfo(z3) {
        UseShellExecute = false, RedirectStandardInput = true,
        RedirectStandardOutput = false, RedirectStandardError = false,
      };
      // the version query used by `vcc /version`
      if (args.Length == 1 && (args[0] == "/version" || args[0] == "-version")) args = new[] { "-version" };
      foreach (var a in Z3Compat.TranslateArguments(args)) psi.ArgumentList.Add(a);

      var tracePath = Environment.GetEnvironmentVariable("VCC_SMT_TRACE");
      var trace = string.IsNullOrEmpty(tracePath) ? null : new System.IO.StreamWriter(tracePath, false) { AutoFlush = true };
      trace?.WriteLine("; args " + string.Join(" ", psi.ArgumentList));
      using var proc = Process.Start(psi);
      var stdin = Console.In;
      var pin = proc.StandardInput;
      pin.AutoFlush = true;
      var pump = new Thread(() => {
        try {
          string line;
          while ((line = stdin.ReadLine()) != null) {
            var translated = Z3Compat.TranslateInput(line);
            trace?.WriteLine(translated);
            pin.WriteLine(translated);
          }
          pin.Close();
        } catch (Exception) { /* Z3 exited */ }
      }) { IsBackground = true };
      pump.Start();
      proc.WaitForExit();
      return proc.ExitCode;
    }
  }
}
