// Copies the prebuilt Boogie binaries and rewrites them so they run on modern .NET.
//
// Always:
//   System.Diagnostics.Debug::get_Listeners -> System.Diagnostics.Trace::get_Listeners
//   (on .NET Framework both return the same shared listener collection)
//
// With --inproc (for WebAssembly, where there are no processes and no blocking on threads):
//   - SMTLibProcess talks to Vcc.ProverBridge.Bridge instead of starting z3.exe
//   - Checker.BeginCheck runs WaitForOutput synchronously instead of on the thread pool
using Mono.Cecil;
using Mono.Cecil.Cil;

var positional = args.Where(a => !a.StartsWith("--")).ToArray();
bool inproc = args.Contains("--inproc");
if (positional.Length != 2) { Console.Error.WriteLine("usage: PatchLibs <boogie-dir> <out-dir> [--inproc]"); return 1; }
string src = positional[0], dst = positional[1];
Directory.CreateDirectory(dst);
// dst is generated: start from a clean set of binaries (file names may change below)
foreach (var f in Directory.GetFiles(dst))
  if (Path.GetExtension(f).ToLowerInvariant() is ".dll" or ".exe" or ".pdb" or ".smt2") File.Delete(f);
// Name each copied assembly after its identity: e.g. BaseTypes.dll contains "Basetypes", which
// Windows and CoreCLR resolve case-insensitively, but Mono (WebAssembly) does not.
foreach (var f in Directory.GetFiles(src)) {
  var ext = Path.GetExtension(f).ToLowerInvariant();
  if (!(ext is ".dll" or ".exe" or ".pdb" or ".smt2")) continue;
  var name = Path.GetFileName(f);
  var stem = Path.GetFileNameWithoutExtension(f);
  var dll = Path.Combine(src, stem + ".dll");
  if ((ext is ".dll" or ".pdb") && File.Exists(dll)) {
    using var a = AssemblyDefinition.ReadAssembly(dll);
    if (string.Equals(a.Name.Name, stem, StringComparison.OrdinalIgnoreCase)) name = a.Name.Name + ext;
  }
  File.Copy(f, Path.Combine(dst, name), true);
}


var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(src);

AssemblyDefinition Read(string name) =>
  AssemblyDefinition.ReadAssembly(Path.Combine(src, name), new ReaderParameters { AssemblyResolver = resolver, ReadSymbols = false });

void Fail(string msg) => throw new InvalidOperationException("PatchLibs: " + msg);

// --- Core.dll: Debug.Listeners -------------------------------------------------------------
using (var asm = Read("Core.dll")) {
  var module = asm.MainModule;
  var system = module.AssemblyReferences.FirstOrDefault(r => r.Name == "System");
  if (system == null) {
    var corlib = module.AssemblyReferences.First(r => r.Name == "mscorlib");
    system = new AssemblyNameReference("System", corlib.Version) { PublicKeyToken = new byte[] { 0xb7, 0x7a, 0x5c, 0x56, 0x19, 0x34, 0xe0, 0x89 } };
    module.AssemblyReferences.Add(system);
  }
  int patched = 0;
  foreach (var type in module.GetTypes())
    foreach (var method in type.Methods.Where(m => m.HasBody))
      foreach (var ins in method.Body.Instructions)
        if (ins.Operand is MethodReference mr && mr.Name == "get_Listeners" && mr.DeclaringType.FullName == "System.Diagnostics.Debug") {
          var trace = new TypeReference("System.Diagnostics", "Trace", module, system);
          ins.Operand = new MethodReference("get_Listeners", mr.ReturnType, trace) { HasThis = false };
          patched++;
        }
  if (patched == 0) Fail("Debug.Listeners not found in Core.dll");
  asm.Write(Path.Combine(dst, "Core.dll"));
  Console.WriteLine($"PatchLibs: Core.dll: rewrote {patched} Debug.Listeners call(s)");
}

if (!inproc) return 0;

// --- VCGeneration.dll: synchronous Checker.BeginCheck -------------------------------------
using (var asm = Read("VCGeneration.dll")) {
  var checker = asm.MainModule.GetType("Microsoft.Boogie.Checker") ?? throw new Exception("no Checker");
  var waitForOutput = checker.Methods.Single(m => m.Name == "WaitForOutput");
  var body = checker.Methods.Single(m => m.Name == "BeginCheck").Body;
  var queue = body.Instructions.Single(i => i.Operand is MethodReference m && m.Name == "QueueUserWorkItem");
  var ldftn = queue.Previous.Previous;   // ldarg.0; ldftn WaitForOutput; newobj WaitCallback; call QueueUserWorkItem; pop
  var newobj = queue.Previous;
  if (ldftn.OpCode != OpCodes.Ldftn || newobj.OpCode != OpCodes.Newobj || queue.Next.OpCode != OpCodes.Pop) Fail("unexpected BeginCheck IL");
  ldftn.OpCode = OpCodes.Ldnull; ldftn.Operand = null;
  newobj.OpCode = OpCodes.Call; newobj.Operand = waitForOutput;
  queue.OpCode = OpCodes.Nop; queue.Operand = null;
  queue.Next.OpCode = OpCodes.Nop;
  asm.Write(Path.Combine(dst, "VCGeneration.dll"));
  Console.WriteLine("PatchLibs: VCGeneration.dll: Checker.BeginCheck is synchronous");
}

// --- Provers.SMTLib.dll: SMTLibProcess -> Vcc.ProverBridge.Bridge ---------------------------
using (var asm = Read("Provers.SMTLib.dll")) {
  var module = asm.MainModule;
  var proc = module.GetType("Microsoft.Boogie.SMTLib.SMTLibProcess") ?? throw new Exception("no SMTLibProcess");
  var bridgeAsm = new AssemblyNameReference("Vcc.ProverBridge", new Version(0, 0, 0, 0));
  module.AssemblyReferences.Add(bridgeAsm);
  var bridge = new TypeReference("Microsoft.Research.Vcc.ProverBridge", "Bridge", module, bridgeAsm);
  var toProver = proc.Fields.Single(f => f.Name == "toProver");
  var psiType = proc.Methods.Single(m => m.IsConstructor && !m.IsStatic).Parameters[0].ParameterType;

  MethodReference BridgeMethod(string name, TypeReference ret, params TypeReference[] ps)
  {
    var m = new MethodReference(name, ret, bridge) { HasThis = false };
    foreach (var p in ps) m.Parameters.Add(new ParameterDefinition(p));
    return m;
  }
  var open = BridgeMethod("Open", toProver.FieldType, module.TypeSystem.Object, psiType);
  var readLine = BridgeMethod("ReadLine", module.TypeSystem.String, module.TypeSystem.Object);
  var close = BridgeMethod("Close", module.TypeSystem.Void, module.TypeSystem.Object);

  // constructor: replace "prover = Process.Start(psi); ...; toProver = prover.StandardInput"
  // by "toProver = Bridge.Open(this, psi)"
  var ctor = proc.Methods.Single(m => m.IsConstructor && !m.IsStatic).Body;
  var start = ctor.Instructions.Single(i => i.Operand is MethodReference m && m.Name == "Start" && m.DeclaringType.Name == "Process");
  var end = ctor.Instructions.Single(i => i.OpCode == OpCodes.Stfld && i.Operand == toProver);
  var il = ctor.GetILProcessor();
  // the stack holds [this, psi] at the call (for "prover = Process.Start(psi)"): drop both,
  // then toProver = Bridge.Open(this, psi)
  foreach (var ins in new[] {
      il.Create(OpCodes.Pop), il.Create(OpCodes.Pop), il.Create(OpCodes.Ldarg_0), il.Create(OpCodes.Ldarg_0), il.Create(OpCodes.Ldarg_1),
      il.Create(OpCodes.Call, open), il.Create(OpCodes.Stfld, toProver) })
    il.InsertBefore(start, ins);
  for (var i = start; ; i = i.Next) { var last = i == end; i.OpCode = OpCodes.Nop; i.Operand = null; if (last) break; }

  void ReplaceBody(string name, params Instruction[] code)
  {
    var body = proc.Methods.Single(m => m.Name == name).Body;
    body.Instructions.Clear(); body.ExceptionHandlers.Clear(); body.Variables.Clear();
    foreach (var c in code) body.Instructions.Add(c);
  }
  ReplaceBody("ReadProver", Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Call, readLine), Instruction.Create(OpCodes.Ret));
  ReplaceBody("Close", Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Call, close), Instruction.Create(OpCodes.Ret));

  // ProcessStartInfo cannot even be constructed in the browser: route every use through
  // static stand-ins in Bridge (which take the start info as an explicit, possibly null, argument)
  var psiDef = psiType;
  MethodReference Stub(string name, TypeReference ret, params TypeReference[] ps) => BridgeMethod(name, ret, ps);
  int psiSites = 0;
  foreach (var m in module.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody))
    foreach (var i in m.Body.Instructions) {
      if (!(i.Operand is MethodReference mr) || mr.DeclaringType.FullName != "System.Diagnostics.ProcessStartInfo") continue;
      MethodReference stub = mr.Name switch {
        ".ctor" when mr.Parameters.Count == 2 => Stub("NewStartInfo", psiDef, module.TypeSystem.String, module.TypeSystem.String),
        "get_Arguments" => Stub("GetArguments", module.TypeSystem.String, psiDef),
        "set_Arguments" => Stub("SetArguments", module.TypeSystem.Void, psiDef, module.TypeSystem.String),
        "get_FileName" => Stub("GetFileName", module.TypeSystem.String, psiDef),
        "set_CreateNoWindow" or "set_UseShellExecute" or "set_RedirectStandardInput" or "set_RedirectStandardOutput"
          or "set_RedirectStandardError" => Stub("SetFlag", module.TypeSystem.Void, psiDef, module.TypeSystem.Boolean),
        _ => null,
      };
      if (stub == null) {
        // only reachable from code we never run in-process (e.g. the Z3 Inspector); leave it
        Console.WriteLine($"PatchLibs: note: {m.DeclaringType.Name}.{m.Name} uses ProcessStartInfo.{mr.Name}; left as is");
        continue;
      }
      i.OpCode = OpCodes.Call; i.Operand = stub; psiSites++;
    }
  Console.WriteLine($"PatchLibs: Provers.SMTLib.dll: {psiSites} ProcessStartInfo use(s) go through Bridge");

  // no Ctrl-C handling in the browser (Console.CancelKeyPress throws there): drop the handler
  int cancelSites = 0;
  foreach (var i in proc.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions))
    if (i.Operand is MethodReference mr && mr.Name == "add_CancelKeyPress" && mr.DeclaringType.FullName == "System.Console") {
      i.OpCode = OpCodes.Pop; i.Operand = null; cancelSites++;
    }
  if (cancelSites == 0) Fail("Console.CancelKeyPress registration not found");

  asm.Write(Path.Combine(dst, "Provers.SMTLib.dll"));
  Console.WriteLine("PatchLibs: Provers.SMTLib.dll: SMTLibProcess uses Vcc.ProverBridge");
}

// --- all: Assembly.Location -> Bridge.GetLocation(assembly) (empty in WebAssembly) ----------
foreach (var path in Directory.GetFiles(dst, "*.dll")) {
  var name = Path.GetFileName(path);
  // read the (possibly already patched) copy from dst, write it back
  var bytes = File.ReadAllBytes(path);
  using var asm = AssemblyDefinition.ReadAssembly(new MemoryStream(bytes), new ReaderParameters { AssemblyResolver = resolver });
  var module = asm.MainModule;
  var sites = module.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody).SelectMany(m => m.Body.Instructions)
    .Where(i => i.Operand is MethodReference mr && mr.Name == "get_Location" && mr.DeclaringType.FullName == "System.Reflection.Assembly").ToList();
  if (sites.Count == 0) continue;
  var bridgeAsm = module.AssemblyReferences.FirstOrDefault(r => r.Name == "Vcc.ProverBridge");
  if (bridgeAsm == null) { bridgeAsm = new AssemblyNameReference("Vcc.ProverBridge", new Version(0, 0, 0, 0)); module.AssemblyReferences.Add(bridgeAsm); }
  var bridge = new TypeReference("Microsoft.Research.Vcc.ProverBridge", "Bridge", module, bridgeAsm);
  var getLocation = new MethodReference("GetLocation", module.TypeSystem.String, bridge) { HasThis = false };
  getLocation.Parameters.Add(new ParameterDefinition(((MethodReference)sites[0].Operand).DeclaringType));
  foreach (var i in sites) { i.OpCode = OpCodes.Call; i.Operand = getLocation; }
  asm.Write(path);
  Console.WriteLine($"PatchLibs: {name}: {sites.Count} Assembly.Location use(s) go through Bridge.GetLocation");
}
return 0;
