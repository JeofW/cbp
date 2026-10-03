using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class NearbyHostileQueryBudgetRegressionTests
{
    [ModuleInitializer] internal static void Run()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root!=null&&!File.Exists(Path.Combine(root.FullName,"CopilotBuddy.csproj")))root=root.Parent;
        var syntax=CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root!.FullName,"runtime-snapshot/Routines/Singular wotlk/Helpers/Unit.cs"))).GetRoot();
        string[] names={"NearbyUnfriendlyUnits","NearbyUnitsInCombatWithMe"};
        string properties=string.Join("\n",syntax.DescendantNodes().OfType<PropertyDeclarationSyntax>().Where(p=>names.Contains(p.Identifier.ValueText)));
        string source="using System;using System.Linq;using System.Collections.Generic;public static class Probe {"+properties+"public static bool ValidUnit(WoWUnit p){Calls++;if(p.Throw)throw new InvalidOperationException(\"reaction unavailable\");return p.Valid;}public static int Calls;}"+"""
public sealed class WoWUnit {public double DistanceSqr;public bool Throw,Valid=true,Combat=true,TaggedByMe=true;}
public static class ObjectManager {public static List<WoWUnit> Units=new();public static IEnumerable<T> GetObjectsOfType<T>(bool a,bool b)=>Units.Cast<T>();}
public static class Cases {public static void Run(){int count=0;foreach(string property in new[]{"NearbyUnfriendlyUnits","NearbyUnitsInCombatWithMe"}){
 var p=typeof(Probe).GetProperty(property);ObjectManager.Units.Clear();for(int i=0;i<100;i++)ObjectManager.Units.Add(new WoWUnit{DistanceSqr=1601+i,Throw=true});
 var near=new WoWUnit{DistanceSqr=1600};ObjectManager.Units.Add(near);Probe.Calls=0;
 var found=((IEnumerable<WoWUnit>)p.GetValue(null)).ToArray();if(found.Length!=1||found[0]!=near||Probe.Calls!=1)throw new Exception("far objects consumed reaction queries: "+property);count++;
 near.Throw=true;bool denied=false;try{p.GetValue(null);}catch(System.Reflection.TargetInvocationException e){denied=e.InnerException is InvalidOperationException;}
 if(!denied)throw new Exception("nearby unavailable hostility was hidden: "+property);count++;
}Console.WriteLine("Nearby hostile query budget: "+count+"/4; actual enumeration properties, controlled expensive hostility boundary; no native reaction proof.");}}
""";
        string temp=Path.Combine(Path.GetTempPath(),"cb-nearby-budget-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        try
        {
            File.WriteAllText(Path.Combine(temp,"Probe.cs"),source);
            Type compilerType=typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler",true)!;
            var compiler=Activator.CreateInstance(compilerType,new object[]{temp})!;
            var result=(CompilerResults)compilerType.GetMethod("Compile")!.Invoke(compiler,null)!;
            var errors=result.Errors.Cast<CompilerError>().Where(e=>!e.IsWarning).ToArray();
            if(errors.Length!=0)throw new InvalidOperationException(string.Join(";",errors.Select(e=>e.ToString())));
            ((Assembly)compilerType.GetProperty("CompiledAssembly")!.GetValue(compiler)!).GetType("Cases")!.GetMethod("Run")!.Invoke(null,null);
        }
        finally {Directory.Delete(temp,true);}
    }
}
