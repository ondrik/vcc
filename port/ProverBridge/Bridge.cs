using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Research.Vcc.Z3Shim;

namespace Microsoft.Research.Vcc.ProverBridge
{
  /// <summary>A Z3 that can be driven in-process through SMT-LIB text.</summary>
  public interface ISolverBackend
  {
    /// <summary>Set a global Z3 parameter; applies to solvers opened afterwards.</summary>
    void SetGlobalParam(string name, string value);
    int Open();
    /// <summary>Run SMT-LIB commands, returning what Z3 would have printed.</summary>
    string Eval(int handle, string commands);
    void Close(int handle);
  }

  /// <summary>
  /// Called from the patched Microsoft.Boogie.SMTLib.SMTLibProcess instead of starting z3.exe:
  /// the TextWriter returned by Open evaluates every command right away and queues the reply
  /// lines, which ReadLine hands to Boogie's response parser.
  /// </summary>
  public static class Bridge
  {
    /// <summary>The solver to use; defaults to the native libz3 when none is set.</summary>
    public static ISolverBackend Backend;

    /// <summary>Optional log of the SMT-LIB traffic (for debugging).</summary>
    public static TextWriter Trace;

    sealed class State
    {
      public int Handle;
      public readonly Queue<string> Lines = new Queue<string>();
    }

    static readonly ConditionalWeakTable<object, State> states = new ConditionalWeakTable<object, State>();

    static ISolverBackend GetBackend()
    {
      if (Backend == null) Backend = new NativeZ3();
      if (Trace == null) {
        var path = Environment.GetEnvironmentVariable("VCC_SMT_TRACE");
        if (!string.IsNullOrEmpty(path)) Trace = new StreamWriter(path, false) { AutoFlush = true };
      }
      return Backend;
    }

    /// <summary>
    /// Replacement for Assembly.Location in the in-process Boogie build: where assemblies have no
    /// file (WebAssembly), report them as living in VCC_HOME, which holds the support files
    /// Boogie looks for next to itself (e.g., UnivBackPred2.smt2).
    /// </summary>
    public static string GetLocation(System.Reflection.Assembly assembly)
    {
      string loc = assembly.Location;
      if (!string.IsNullOrEmpty(loc)) return loc;
      string home = Environment.GetEnvironmentVariable("VCC_HOME") ?? AppContext.BaseDirectory ?? "/";
      return Path.Combine(home, assembly.GetName().Name + ".dll");
    }

    // --- ProcessStartInfo stand-ins -----------------------------------------------------------
    // In the browser every System.Diagnostics.Process API (even new ProcessStartInfo) throws, so
    // the in-process Provers.SMTLib.dll calls these instead; the start info is never created and
    // its file name and arguments are kept here (single-threaded use).

    static string pendingFileName = "", pendingArguments = "";

    public static ProcessStartInfo NewStartInfo(string fileName, string arguments)
    {
      pendingFileName = fileName ?? "";
      pendingArguments = arguments ?? "";
      return null;
    }

    public static string GetArguments(ProcessStartInfo psi) { return psi != null ? psi.Arguments : pendingArguments; }

    public static void SetArguments(ProcessStartInfo psi, string value)
    {
      if (psi != null) psi.Arguments = value; else pendingArguments = value ?? "";
    }

    public static string GetFileName(ProcessStartInfo psi) { return psi != null ? psi.FileName : pendingFileName; }

    /// <summary>Ignores CreateNoWindow, UseShellExecute, Redirect*: there is no process.</summary>
    public static void SetFlag(ProcessStartInfo psi, bool value) { }

    public static TextWriter Open(object owner, ProcessStartInfo psi)
    {
      var backend = GetBackend();
      var args = Z3Compat.TranslateArguments(SplitArguments(GetArguments(psi)));
      foreach (var arg in args) {
        if (arg.StartsWith("-memory:", StringComparison.Ordinal)) {
          backend.SetGlobalParam("memory_max_size", arg.Substring(8));
        } else if (arg.Length > 0 && arg[0] != '-' && arg.IndexOf('=') > 0) {
          int eq = arg.IndexOf('=');
          backend.SetGlobalParam(arg.Substring(0, eq), arg.Substring(eq + 1));
        }
        // -smt2, -in and other flags select the text front end, which is what Eval uses anyway
      }
      var state = new State { Handle = backend.Open() };
      states.AddOrUpdate(owner, state);
      if (Trace != null) Trace.WriteLine("; open " + state.Handle + " " + string.Join(" ", args));
      return new Writer(state);
    }

    public static string ReadLine(object owner)
    {
      State state;
      if (!states.TryGetValue(owner, out state) || state.Lines.Count == 0) return null;
      return state.Lines.Dequeue();
    }

    public static void Close(object owner)
    {
      State state;
      if (!states.TryGetValue(owner, out state)) return;
      states.Remove(owner);
      if (state.Handle != 0) GetBackend().Close(state.Handle);
      state.Handle = 0;
    }

    static void Eval(State state, string commands)
    {
      if (state.Handle == 0 || commands.Trim().Length == 0) return;
      commands = Z3Compat.TranslateInput(commands);
      if (Trace != null) Trace.WriteLine(commands);
      string output = GetBackend().Eval(state.Handle, commands);
      if (Trace != null && output.Length > 0) Trace.WriteLine("; => " + output.Replace("\n", "\n; => "));
      foreach (var line in output.Split('\n')) {
        var l = line.TrimEnd('\r');
        if (l.Length > 0) state.Lines.Enqueue(l);
      }
    }

    static IEnumerable<string> SplitArguments(string s)
    {
      foreach (var a in (s ?? "").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)) yield return a.Trim('"');
    }

    /// <summary>
    /// Collects text until a line is complete; Boogie sends whole commands per WriteLine.
    /// </summary>
    sealed class Writer : TextWriter
    {
      readonly State state;
      readonly StringBuilder pending = new StringBuilder();

      public Writer(State state) { this.state = state; }

      public override Encoding Encoding { get { return Encoding.UTF8; } }

      public override void Write(char value) { pending.Append(value); }
      public override void Write(string value) { pending.Append(value); }

      public override void WriteLine(string value)
      {
        pending.Append(value);
        Flush();
      }

      public override void WriteLine() { Flush(); }

      public override void Flush()
      {
        if (pending.Length == 0) return;
        var text = pending.ToString();
        pending.Clear();
        Eval(state, text);
      }
    }
  }
}
