using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.JavaScript;
using Microsoft.Research.Vcc.ProverBridge;

namespace Microsoft.Research.Vcc.Browser
{
  /// <summary>
  /// Z3 compiled to WebAssembly, provided by the page as the JS module "vccz3"
  /// (see port/web/vcc-engine.mjs and port/wasm/vccz3.c).
  /// </summary>
  sealed partial class JsZ3 : ISolverBackend
  {
    [JSImport("setParam", "vccz3")] private static partial void JsSetParam(string name, string value);
    [JSImport("open", "vccz3")] private static partial int JsOpen();
    [JSImport("eval", "vccz3")] private static partial string JsEval(int handle, string commands);
    [JSImport("close", "vccz3")] private static partial void JsClose(int handle);

    public void SetGlobalParam(string name, string value) { JsSetParam(name, value); }
    public int Open() { return JsOpen(); }
    public string Eval(int handle, string commands) { return JsEval(handle, commands); }
    public void Close(int handle) { JsClose(handle); }
  }

  /// <summary>Console output forwarded to the page (JS module "vccio").</summary>
  sealed partial class JsConsole : TextWriter
  {
    [JSImport("write", "vccio")] private static partial void JsWrite(int stream, string text);

    readonly int stream;
    readonly System.Text.StringBuilder line = new System.Text.StringBuilder();

    public JsConsole(int stream) { this.stream = stream; }

    public override System.Text.Encoding Encoding { get { return System.Text.Encoding.UTF8; } }

    public override void Write(char value)
    {
      line.Append(value);
      if (value == '\n') Flush();
    }

    public override void Write(string value)
    {
      if (value == null) return;
      line.Append(value);
      if (value.IndexOf('\n') >= 0) Flush();
    }

    public override void Flush()
    {
      if (line.Length == 0) return;
      JsWrite(stream, line.ToString());
      line.Clear();
    }
  }

  public static class Program
  {
    public const string Home = "/vcc";

    /// <summary>Unpack the support files embedded under "vfs/" into the in-memory file system.</summary>
    static void UnpackSupportFiles()
    {
      var asm = typeof(Program).Assembly;
      foreach (var name in asm.GetManifestResourceNames()) {
        if (!name.StartsWith("vfs/", StringComparison.Ordinal)) continue;
        var path = Path.Combine(Home, name.Substring(4));
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using (var src = asm.GetManifestResourceStream(name))
        using (var dst = File.Create(path))
          src.CopyTo(dst);
      }
      Environment.SetEnvironmentVariable("VCC_HOME", Home);
    }

    /// <summary>
    /// Runs VCC once (the page starts a fresh runtime for every run). The input is expected to be
    /// preprocessed already (the page runs mcpp), so callers normally pass /n.
    /// </summary>
    public static int Main(string[] args)
    {
      var stdout = new JsConsole(1);
      var stderr = new JsConsole(2);
      Console.SetOut(stdout);
      Console.SetError(stderr);
      UnpackSupportFiles();
      Bridge.Backend = new JsZ3();
      var vccArgs = new List<string> {
        // Boogie insists on a prover path; with the in-process bridge it is never started
        "/b:/proverOpt:PROVER_PATH=" + Path.Combine(Home, "z3"),
      };
      vccArgs.AddRange(args);
      try {
        return VccCommandLineHost.Main(vccArgs.ToArray());
      } finally {
        stdout.Flush();
        stderr.Flush();
      }
    }
  }
}
