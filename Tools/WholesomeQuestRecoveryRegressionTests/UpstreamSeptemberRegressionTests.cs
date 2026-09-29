using System;
using System.Collections.Generic;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Xml.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Styx;
using Styx.Logic;
using Styx.Logic.Pathing;
using Styx.Logic.Profiles;
using Styx.Logic.Profiles.Quest;

// Actual profile/XML owners and complete tracked observation classes. Native,
// spellbook and terrain leaves in the compiled probes are controlled explicitly.
internal static class UpstreamSeptemberRegressionTests
{
    private sealed class Failure(string reason) : Exception(reason) { }
    internal static string Root()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "CopilotBuddy.csproj"))) return d.FullName;
        throw new InvalidOperationException("Tracked checkout required");
    }
    internal static void Probe(string label, string source, params string[] files)
    {
        string temp = Path.Combine(Path.GetTempPath(), "cb-upstream-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            File.WriteAllText(Path.Combine(temp, "Probe.cs"), source);
            foreach (string path in files) File.Copy(Path.Combine(Root(), path), Path.Combine(temp, Path.GetFileName(path)));
            var type = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            var compiler = Activator.CreateInstance(type, new object[] { temp })!;
            foreach (string path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
                type.GetMethod("AddReference")!.Invoke(compiler, new object[] { path });
            var result = (CompilerResults)type.GetMethod("Compile")!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException(label + " probe compilation: " + string.Join("; ", errors.Select(e => e.ToString())));
            var assembly = (Assembly)type.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            try { assembly.GetType("Cases", true)!.GetMethod("Run")!.Invoke(null, null); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        finally { Directory.Delete(temp, true); }
    }
    internal static string Member(string path, string name, bool isClass = false)
    {
        var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(Root(), path))).GetRoot();
        return isClass ? tree.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(m => m.Identifier.ValueText == name).ToString()
            : tree.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.ValueText == name).ToString();
    }
    private static void Check(bool condition, string reason) { if (!condition) throw new Failure(reason); }
    private static void Rejected(string xml)
    {
        try { OrderNode.FromXml(XElement.Parse(xml)); }
        catch (ProfileException) { return; }
        throw new Failure("Invalid objective was admitted: " + xml);
    }
    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new List<(string Name, Action Test)>();
        foreach (var (alias, expected, idName) in new[] {
            ("Collect", ObjectiveType.CollectItem, "ItemId"), ("gatheritem", ObjectiveType.CollectItem, "itementry"),
            ("Slay", ObjectiveType.KillMob, "MobId"), ("grind", ObjectiveType.KillMob, "mobentry"),
            ("Use", ObjectiveType.UseObject, "ObjectId"), ("useobject", ObjectiveType.UseObject, "gameobjectentry") })
        {
            string type = alias, key = idName; var kind = expected;
            cases.Add(("objective alias " + type, () => {
                ObjectiveNode node;
                try { node = (ObjectiveNode)OrderNode.FromXml(XElement.Parse($"<Objective qUeStId='123' tYpE='{type}' {key}='456' cOuNt='2' iNdEx='1'/>")); }
                catch (ProfileException e) { throw new Failure("Valid alias rejected: " + e.Message); }
                Check(node.QuestId == 123 && node.ObjectiveType == kind && node.ObjectiveId == 456 && node.ObjectiveCount == 2 && node.ObjectiveIndex == 1,
                    "Alias silently became a different objective/identity");
            }));
        }
        foreach (string xml in new[] {
            "<Objective QuestId='123' Type='typo' Id='456'/>", "<Objective QuestId='123' Type='999' Id='456'/>",
            "<Objective QuestId='123' Type='CollectItem'/>", "<Objective Type='KillMob' Id='456'/>",
            "<Objective QuestId='bad' Type='KillMob' Id='456'/>", "<Objective QuestId='123' Type='KillMob' Id='0'/>",
            "<Objective QuestId='123' Type='KillMob' Id='456' Count='bad'/>",
            "<Objective QuestId='123' Type='KillMob' Id='456' Index='bad'/>",
            "<Objective QuestId='123' questid='124' Type='KillMob' Id='456'/>",
            "<Objective QuestId='123' Type='KillMob' Id='456' MobId='457'/>" })
        { string input = xml; cases.Add(("reject " + xml, () => Rejected(input))); }
        foreach(int value in new[]{0,1})
        {
            int index=value;cases.Add(("retained index-only objective "+index,()=>{
                ObjectiveNode node;
                try{node=ObjectiveNode.FromXml(XElement.Parse($"<Objective QuestId='868' Type='CollectItem' Index='{index}'/>"));}
                catch(ProfileException error){throw new Failure(error.Message);}
                Check(node.ObjectiveId==0&&node.ObjectiveIndex==index&&node.ObjectiveType==ObjectiveType.CollectItem,
                    "index-only objective lost its explicit identity");
            }));
        }
        foreach(string index in new[]{"-1","bad","-2"})
        {string value=index;cases.Add(("missing ID rejects unusable index "+value,()=>Rejected($"<Objective QuestId='868' Type='CollectItem' Index='{value}'/>")));}
        cases.Add(("mixed-case move preserves explicit mode and coordinates", () => {
            MoveToNode node;
            try { node = MoveToNode.FromXml(XElement.Parse("<RunTo x='1.5' y='2.5' z='3.5' nAv='Run' pReCiSiOn='2' qUeStId='12'/>")); }
            catch (ProfileException e) { throw new Failure(e.Message); }
            Check(node.Location.X == 1.5f && node.Location.Y == 2.5f && node.Location.Z == 3.5f && node.QuestId == 12 && node.NavType == NavType.Run && node.Precision == 2, "move aliases lost data");
        }));
        foreach (string tag in new[] { "PickUp", "TurnIn" })
        {
            string name=tag; cases.Add(("mixed-case " + tag, () => {
                string actor=name=="PickUp"?"giver":"turnin";
                try { OrderNode.FromXml(XElement.Parse($"<{name} qUeStId='123' {actor}Id='456' x='1' y='2' z='3'/>")); }
                catch (ProfileException e) { throw new Failure(e.Message); }
            }));
        }
        cases.Add(("flight connections preserve full names and XML characters", () => {
            var node=new XmlFlightNode(1,80,"Start",0,new WoWPoint(1,2,3));
            foreach(string name in new[]{"Stormwind, Elwynn","Aerie Peak, The Hinterlands","Gate & <Valley>","  exact spacing  "})node.Connect(name);
            var copy=new XmlFlightNode(XElement.Parse(node.ToXml().ToString()));
            Check(copy.Connections.SetEquals(node.Connections), "commas split destination identities");
        }));
        cases.Add(("structured connections ignore malformed empty children", () => {
            var node=new XmlFlightNode(XElement.Parse("<Node><Connections><Connection name='A, B'/><Connection/><Connection name=''/></Connections></Node>"));
            Check(node.Connections.SetEquals(new[]{"A, B"}),"structured connection parse failed");
        }));
        cases.Add(("ambiguous legacy comma list grants no route",()=>Check(new XmlFlightNode(XElement.Parse("<Node Connections='A, B, C, D'/>" )).Connections.Count==0,"ambiguous legacy list became invented connections")));
        cases.Add(("unambiguous legacy singleton remains usable",()=>Check(new XmlFlightNode(XElement.Parse("<Node Connections='Single'/>" )).Connections.SetEquals(new[]{"Single"}),"legacy singleton lost")));
        foreach(string path in new[]{"Protected Items.xml","runtime-snapshot/Protected Items.xml"})
        {string file=path;cases.Add(("mining pick protected in "+file,()=>Check(XElement.Load(Path.Combine(Root(),file)).Descendants("Item").Any(e=>(string?)e.Attribute("entry")=="2901"),"mining pick can be mailed/sold")));}
        int passed=0,assertions=0,unexpected=0;
        foreach(var item in cases){try{item.Test();passed++;Console.WriteLine("PASS upstream September: "+item.Name);}catch(Failure e){assertions++;Console.Error.WriteLine("FAIL upstream September: "+item.Name+": "+e.Message);}catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR upstream September: "+item.Name+": "+e);}}
        Console.WriteLine($"Upstream September scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual profile/XML/data; no game attached.");
        if(assertions+unexpected!=0)throw new InvalidOperationException("Upstream profile/persistence regression failures");
    }
}
