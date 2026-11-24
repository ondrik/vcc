using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Microsoft.Research.Vcc.ProverBridge
{
  /// <summary>
  /// Backend calling a native libz3 through its C API, for testing the in-process path natively.
  /// Note that Z3_eval_smtlib2_string can be much slower than the z3 executable on Boogie's
  /// queries (see port/wasm/vccz3.cpp, which therefore mirrors the executable instead); the
  /// native build normally runs z3 as a process through Z3Shim.
  /// </summary>
  public sealed class NativeZ3 : ISolverBackend
  {
    const string Lib = "z3";

    [DllImport(Lib)] static extern IntPtr Z3_mk_config();
    [DllImport(Lib)] static extern void Z3_del_config(IntPtr cfg);
    [DllImport(Lib)] static extern IntPtr Z3_mk_context(IntPtr cfg);
    [DllImport(Lib)] static extern void Z3_del_context(IntPtr ctx);
    [DllImport(Lib)] static extern void Z3_set_error_handler(IntPtr ctx, IntPtr handler);
    [DllImport(Lib)] static extern IntPtr Z3_eval_smtlib2_string(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string str);
    [DllImport(Lib)] static extern int Z3_get_error_code(IntPtr ctx);
    [DllImport(Lib)] static extern IntPtr Z3_get_error_msg(IntPtr ctx, int code);
    [DllImport(Lib)] static extern void Z3_global_param_set([MarshalAs(UnmanagedType.LPUTF8Str)] string id, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    readonly Dictionary<int, IntPtr> contexts = new Dictionary<int, IntPtr>();
    int next = 1;

    public void SetGlobalParam(string name, string value) { Z3_global_param_set(name, value); }

    public int Open()
    {
      var cfg = Z3_mk_config();
      var ctx = Z3_mk_context(cfg);
      Z3_del_config(cfg);
      Z3_set_error_handler(ctx, IntPtr.Zero);   // report errors through the error code, do not abort
      contexts[next] = ctx;
      return next++;
    }

    public string Eval(int handle, string commands)
    {
      var ctx = contexts[handle];
      var res = Marshal.PtrToStringUTF8(Z3_eval_smtlib2_string(ctx, commands)) ?? "";
      int code = Z3_get_error_code(ctx);
      if (code != 0) res += "(error \"" + (Marshal.PtrToStringUTF8(Z3_get_error_msg(ctx, code)) ?? "").Replace("\"", "'") + "\")\n";
      return res;
    }

    public void Close(int handle)
    {
      IntPtr ctx;
      if (contexts.TryGetValue(handle, out ctx)) { Z3_del_context(ctx); contexts.Remove(handle); }
    }
  }
}
