using System.Runtime.CompilerServices;
using Styx.Logic.Pathing;
using Styx.Logic.Questing;
internal static class QuestRelationSearchTests
{
    [ModuleInitializer] internal static void Run()
    {
        int passed=0,total=0;
        void Check(bool b,string why){if(!b)throw new InvalidOperationException(why);}
        void Case(string name,Action test){total++;try{test();passed++;Console.WriteLine("PASS relation search: "+name);}catch(Exception e){Console.Error.WriteLine("FAIL relation search: "+name+": "+e.Message);}}
        var origin=new WoWPoint(-689.583f,4167.8f,58.5228f);
        Case("all patrol hints match the pinned primary SQL receipt",()=>{
            using var source=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"aledis-patrol-source.json")));
            var expected=source.RootElement.GetProperty("points").EnumerateArray().ToArray();
            var actual=(WoWPoint[])typeof(QuestRelationSearch).GetMethod("AledisPatrol",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.Invoke(null,null)!;
            Check(actual.Length==expected.Length&&actual.Length==44,"source/code patrol population differs");
            for(int i=0;i<actual.Length;i++)Check(actual[i].Equals(new WoWPoint(expected[i][0].GetSingle(),expected[i][1].GetSingle(),expected[i][2].GetSingle())),"patrol coordinate differs from primary source");
        });
        Case("unknown relation defers after bounded observation instead of wandering",()=>{
            var s=new QuestRelationSearch(530,10161,19367,origin);
            Check(s.SearchPointCount==0&&s.Next(100,origin)==null&&!s.Exhausted,"unproven patrol generated search positions");
            Check(s.Next(129,origin)==null&&!s.Exhausted,"generic absence expired early");
            Check(s.Next(130,origin)==null&&s.Exhausted,"missing relation parked forever");
        });
        Case("source-bound patrol is ordered and finite",()=>{
            var s=new QuestRelationSearch(530,10286,20159,origin);
            Check(s.SearchPointCount==44&&s.Next(100,origin)==null,"patrol identity or initial observation grace wrong");
            var p=s.Next(102,origin);Check(p.HasValue&&p.Value.Distance(new WoWPoint(-693.036f,4187.63f,57.0026f))<.1,"search did not follow the source patrol from its origin");
            for(int i=0;i<50&&!s.Exhausted;i++){p=s.Next(103+i,p??origin);}
            Check(s.Exhausted,"patrol search cycled indefinitely");
        });
        foreach(int changed in new[]{0,1,2,3})Case("patrol hints cannot cross an identity boundary/"+changed,()=>{
            var s=new QuestRelationSearch(changed==0?0u:530u,changed==1?1u:10286u,changed==2?1u:20159u,changed==3?origin.Add(100,0,0):origin);
            Check(s.SearchPointCount==0,"different map/quest/entry/origin acquired patrol hints");
        });
        Case("missing native progress has a short search deadline",()=>{
            var s=new QuestRelationSearch(530,10286,20159,origin);s.Next(0,origin);s.Next(2,origin);s.Next(31,origin);
            Check(s.Exhausted,"no-motion search claimed progress from movement requests");
        });
        Case("a long search cannot renew its overall budget",()=>{
            var s=new QuestRelationSearch(530,10286,20159,origin);s.Next(0,origin);
            for(int i=1;i<30;i++)s.Next(i*10,origin.Add(i,0,0));
            s.Next(300,origin.Add(30,0,0));Check(s.Exhausted,"motion hid the overall missing-NPC deadline");
        });
        Case("clock reversal stays UNKNOWN",()=>{
            var s=new QuestRelationSearch(530,10286,20159,origin);s.Next(100,origin);bool caught=false;
            try{s.Next(99,origin);}catch(Styx.Helpers.ObservationUnavailableException){caught=true;}
            Check(caught&&!s.Exhausted,"invalid clock became endpoint failure");
        });
        Console.WriteLine($"Relation search: {passed}/{total}; actual bounded search policy; source patrol hints not spawn/reachability/completion proof.");
        if(passed!=total)Environment.ExitCode=1;
    }
}
