// Translates what the bundled (2014-era) Boogie sends to Z3 into what a modern Z3 (4.8+) accepts.
// Boogie and VCC use the pre-4.0 upper-case parameter names, both on the command line
// (NAME=value, /flag) and in (set-option :NAME value) commands.
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Microsoft.Research.Vcc.Z3Shim
{
  public static class Z3Compat
  {
    // old name -> new name; null means the parameter no longer exists and is dropped
    static readonly Dictionary<string, string> Params = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
      { "AUTO_CONFIG", "auto_config" },
      { "MODEL_HIDE_UNUSED_PARTITIONS", null },
      { "MODEL_V2", "model.v2" },
      { "ASYNC_COMMANDS", null },
      { "PHASE_SELECTION", "smt.phase_selection" },
      { "RESTART_STRATEGY", "smt.restart_strategy" },
      { "RESTART_FACTOR", "smt.restart_factor" },
      { "ARITH_RANDOM_INITIAL_VALUE", "smt.arith.random_initial_value" },
      { "CASE_SPLIT", "smt.case_split" },
      { "DELAY_UNITS", "smt.delay_units" },
      { "DELAY_UNITS_THRESHOLD", "smt.delay_units_threshold" },
      { "NNF_SK_HACK", "nnf.sk_hack" },
      { "MBQI", "smt.mbqi" },
      { "QI_EAGER_THRESHOLD", "smt.qi.eager_threshold" },
      { "QI_COST", "smt.qi.cost" },
      { "TYPE_CHECK", "type_check" },
      { "BV_REFLECT", "smt.bv.reflect" },
      { "SOFT_TIMEOUT", "timeout" },
      { "PROGRESS_SAMPLING_FREQ", null },
      { "OPTIMIZE_FOR_BV", null },
    };

    static string MapName(string name, out bool known)
    {
      string mapped;
      known = Params.TryGetValue(name, out mapped);
      return known ? mapped : name;
    }

    static string MapValue(string value)
    {
      // |"(+ weight generation)"| -> "(+ weight generation)"
      if (value.Length >= 4 && value.StartsWith("|\"") && value.EndsWith("\"|")) return value.Substring(1, value.Length - 2);
      return value;
    }

    /// <summary>Translate the command line Boogie passes to z3.exe.</summary>
    public static List<string> TranslateArguments(IEnumerable<string> args)
    {
      var result = new List<string>();
      foreach (var arg in args) {
        if (arg.Length == 0) continue;
        if (arg[0] == '/') { result.Add("-" + arg.Substring(1)); continue; }   // /memory:300 -> -memory:300
        int eq = arg.IndexOf('=');
        if (eq > 0 && arg[0] != '-') {
          bool known;
          var name = MapName(arg.Substring(0, eq), out known);
          if (name != null) result.Add(name + "=" + MapValue(arg.Substring(eq + 1)));
          continue;
        }
        result.Add(arg);
      }
      return result;
    }

    static readonly Regex SetOption = new Regex(@"\(set-option\s+:([A-Za-z_][A-Za-z0-9_.]*)\s+(\|[^|]*\||""[^""]*""|[^\s()]+)\s*\)", RegexOptions.Compiled);

    /// <summary>Translate one chunk of SMT-LIB input.</summary>
    public static string TranslateInput(string text)
    {
      if (text.IndexOf("set-option", StringComparison.Ordinal) < 0) return text;
      return SetOption.Replace(text, m => {
        bool known;
        var name = MapName(m.Groups[1].Value, out known);
        if (!known) return m.Value;
        if (name == null) return "";
        return "(set-option :" + name + " " + MapValue(m.Groups[2].Value) + ")";
      });
    }
  }
}
