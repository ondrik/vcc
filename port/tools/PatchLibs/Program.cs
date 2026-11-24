// Copies the prebuilt Boogie binaries and rewrites the few .NET Framework-only API uses so that
// they run on modern .NET:
//   System.Diagnostics.Debug::get_Listeners  ->  System.Diagnostics.Trace::get_Listeners
// (on .NET Framework both return the same shared listener collection).
using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length != 2) { Console.Error.WriteLine("usage: PatchLibs <boogie-dir> <out-dir>"); return 1; }
string src = args[0], dst = args[1];
Directory.CreateDirectory(dst);
foreach (var f in Directory.GetFiles(src)) {
  var ext = Path.GetExtension(f).ToLowerInvariant();
  if (ext is ".dll" or ".exe" or ".pdb" or ".smt2") File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
}

int patched = 0;
var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(src);
foreach (var name in new[] { "Core.dll" }) {
  var path = Path.Combine(src, name);
  using var asm = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = resolver, ReadSymbols = false });
  var module = asm.MainModule;
  var system = module.AssemblyReferences.FirstOrDefault(r => r.Name == "System");
  if (system == null) {
    var corlib = module.AssemblyReferences.First(r => r.Name == "mscorlib");
    system = new AssemblyNameReference("System", corlib.Version) { PublicKeyToken = new byte[] { 0xb7, 0x7a, 0x5c, 0x56, 0x19, 0x34, 0xe0, 0x89 } };
    module.AssemblyReferences.Add(system);
  }
  foreach (var type in module.GetTypes())
    foreach (var method in type.Methods.Where(m => m.HasBody))
      foreach (var ins in method.Body.Instructions)
        if (ins.Operand is MethodReference mr && mr.Name == "get_Listeners" && mr.DeclaringType.FullName == "System.Diagnostics.Debug") {
          var trace = new TypeReference("System.Diagnostics", "Trace", module, system);
          ins.Operand = new MethodReference("get_Listeners", mr.ReturnType, trace) { HasThis = false };
          patched++;
        }
  // the strong-name signature is invalidated by the rewrite; modern .NET does not check it
  asm.Name.HasPublicKey = asm.Name.HasPublicKey;
  asm.Write(Path.Combine(dst, name));
}
Console.WriteLine($"PatchLibs: rewrote {patched} call(s)");
return patched > 0 ? 0 : 2;
