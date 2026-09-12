using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Tripper.Navigation;

internal static class LoaderDiagnostics
{
    internal static void Run(Navigator navigator, string root, string output)
    {
        var evidence = new List<object>();
        void Save() => File.WriteAllText(Path.Combine(output, "loader-diagnostics.json"),
            JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
        try
        {
            string[] modules = Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
                .Where(module => module.ModuleName.Equals("Navigation.dll", StringComparison.OrdinalIgnoreCase))
                .Select(module => module.FileName).ToArray();
            evidence.Add(new { kind="loaded-module", paths=modules, cwd=Environment.CurrentDirectory,
                base_directory=AppContext.BaseDirectory, process=Environment.ProcessPath });
            foreach (string folder in modules.Select(path => Path.GetDirectoryName(path)!).Append(root).Distinct())
                foreach (string name in new[] { "001.mmap", "0013431.mmtile", "0013134.mmtile" })
                {
                    string path = Path.Combine(folder,"mmaps",name);
                    if (!File.Exists(path)) { evidence.Add(new { kind="file", path, exists=false }); continue; }
                    using var stream=File.OpenRead(path);
                    byte[] prefix=new byte[Math.Min(128L,stream.Length)]; int read=stream.Read(prefix);
                    evidence.Add(new { kind="file", path, exists=true, size=stream.Length,
                        header_hex=Convert.ToHexString(prefix.AsSpan(0,read)) });
                }
            Save();
            var point = new Vector3(-1152.76f,71.41f,145.87f);
            void Probe(string stage)
            {
                var watch=Stopwatch.StartNew();
                var result=navigator.FindPath(1,point,point+new Vector3(2,0,0));
                watch.Stop();
                evidence.Add(new { kind="control-query", stage, success=result.Succeeded,
                    partial=result.IsPartialPath, points=result.Points.Length, status=result.Status.Value,
                    fail_step=(int)result.FailStep, loaded_tiles=navigator.GetLoadedTilesCount(1),
                    loaded_adts=navigator.GetLoadedAdtCount(1), wall_ms=watch.Elapsed.TotalMilliseconds });
                Save();
            }
            Probe("unchanged-host-contract");
            navigator.EnsureTilesAroundPosition(1,point,1);
            Probe("explicit-ensure-tiles");
            // This is a separately labeled hypothesis test, not a silent production change.
            typeof(Navigator).Assembly.GetType("Tripper.Navigation.NativeMethods")!
                .GetMethod("LoadMaps",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,null);
            Probe("explicit-native-loadmaps");
            navigator.SetQueryFilterByStored("Default");
            Probe("default-filter-control");
            var unrestricted=Navigator.GetNewDefaultQueryFilter();
            unrestricted.IncludeFlags=AbilityFlags.All; unrestricted.ExcludeFlags=0;
            navigator.StoreQueryFilter("audit-unrestricted",unrestricted);
            navigator.SetQueryFilterByStored("audit-unrestricted");
            Probe("unrestricted-filter-diagnostic-not-safe-route-policy");
        }
        catch(Exception error) { evidence.Add(new { kind="error", error=error.ToString() }); }
        finally { Save(); }
    }
}
