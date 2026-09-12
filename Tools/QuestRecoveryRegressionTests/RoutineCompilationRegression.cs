using System.CodeDom.Compiler;
using System.Reflection;
using Styx.Loaders;

internal static class RoutineCompilationRegression
{
    internal static void Run()
    {
        VerifySourceDiscovery();
        string sourceDirectory = ResolveSourceDirectory(AppContext.BaseDirectory);
        Console.WriteLine($"Singular compatibility source: {sourceDirectory}");
        var compiler = new SourceCompiler(sourceDirectory);
        // Match the desktop host's available framework references without starting the game client.
        foreach (string assemblyPath in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
            compiler.AddReference(assemblyPath);
        CompilerResults results = compiler.Compile();
        var errors = results.Errors.Cast<CompilerError>().Where(error => !error.IsWarning).ToArray();
        if (errors.Length != 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors.Select(error => error.ToString())));
        if (compiler.CompiledAssembly == null)
            throw new InvalidOperationException("Singular compilation produced no assembly.");
        VerifyPaladinPostKillPredicate(compiler.CompiledAssembly);
        VerifyPaladinExorcismPolicy(compiler.CompiledAssembly);
        Console.WriteLine($"Selected Singular source compiled successfully ({compiler.SourceFilePaths.Count} source files).");
    }

    // A checkout must test its tracked snapshot, never a stale installed copy in bin/.
    // Installed-layout discovery is retained only for execution outside a checkout.
    private static string ResolveSourceDirectory(string startDirectory)
    {
        var ancestors = new List<DirectoryInfo>();
        for (DirectoryInfo? root = new DirectoryInfo(startDirectory); root != null; root = root.Parent)
            ancestors.Add(root);

        foreach (DirectoryInfo root in ancestors)
        {
            if (!File.Exists(Path.Combine(root.FullName, "CopilotBuddy.csproj")))
                continue;
            string snapshot = Path.Combine(root.FullName, "runtime-snapshot", "Routines", "Singular wotlk");
            if (!Directory.Exists(snapshot))
                throw new DirectoryNotFoundException($"Tracked Singular source was not found: {snapshot}");
            return snapshot;
        }

        foreach (DirectoryInfo root in ancestors)
        {
            string installed = Path.Combine(root.FullName, "Routines", "Singular wotlk");
            if (Directory.Exists(installed))
                return installed;
        }
        throw new DirectoryNotFoundException("Neither a tracked nor installed Singular source directory was found.");
    }

    private static void VerifySourceDiscovery()
    {
        string root = Path.Combine(Path.GetTempPath(), "cb-routine-source-" + Guid.NewGuid().ToString("N"));
        string start = Path.Combine(root, "bin", "Release");
        string snapshot = Path.Combine(root, "runtime-snapshot", "Routines", "Singular wotlk");
        string installed = Path.Combine(start, "Routines", "Singular wotlk");
        string project = Path.Combine(root, "CopilotBuddy.csproj");
        try
        {
            Directory.CreateDirectory(snapshot);
            Directory.CreateDirectory(installed);
            File.WriteAllText(project, "<Project />");
            if (ResolveSourceDirectory(start) != snapshot)
                throw new InvalidOperationException("A stale installed copy must not shadow the tracked snapshot.");

            Directory.Delete(snapshot);
            try
            {
                ResolveSourceDirectory(start);
                throw new InvalidOperationException("A checkout missing its snapshot must fail, not test unrelated installed code.");
            }
            catch (DirectoryNotFoundException) { }

            File.Delete(project);
            if (ResolveSourceDirectory(start) != installed)
                throw new InvalidOperationException("Standalone installed-layout compatibility must remain supported.");
            Directory.Delete(installed);
            try
            {
                ResolveSourceDirectory(start);
                throw new InvalidOperationException("Missing routine source must not silently skip compatibility checks.");
            }
            catch (DirectoryNotFoundException) { }
            Console.WriteLine("Singular source discovery regressions passed.");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static void VerifyPaladinExorcismPolicy(Assembly assembly)
    {
        Type retribution = assembly.GetType("Singular.ClassSpecific.Paladin.Retribution")
            ?? throw new InvalidOperationException("Singular Retribution routine was not found.");
        MethodInfo policy = retribution.GetMethod(
            "ShouldCastExorcism",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Retribution Exorcism policy was not found.");

        bool Invoke(bool knows, bool proc, bool melee, bool autoAttack) =>
            (bool)policy.Invoke(null, new object[] { knows, proc, melee, autoAttack })!;

        if (!Invoke(false, false, false, false))
            throw new InvalidOperationException("Low-level Paladin must retain ranged Exorcism opener.");
        if (!Invoke(false, false, true, true))
            throw new InvalidOperationException("Low-level Paladin must allow Exorcism as a melee filler.");
        if (!Invoke(true, true, true, true))
            throw new InvalidOperationException("Art of War must retain instant Exorcism in melee.");
        if (Invoke(true, false, false, false))
            throw new InvalidOperationException("A trained Art of War Paladin must wait for its proc.");

        var retry = retribution.GetMethod("CreateExorcismRetryBehavior", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Exorcism retry behavior is missing.");
        int attempts = 0, moves = 0;
        var cast = new TreeSharp.Action(_ => { attempts++; return TreeSharp.RunStatus.Success; });
        var guarded = (TreeSharp.Composite)retry.Invoke(null, new object[] { cast })!;
        var root = new TreeSharp.PrioritySelector(guarded,
            new TreeSharp.Action(_ => { moves++; return TreeSharp.RunStatus.Success; }));
        for (int i = 0; i < 5; i++)
        {
            root.Start(null!);
            while (root.Tick(null!) == TreeSharp.RunStatus.Running) { }
            root.Stop(null!);
        }
        if (attempts != 1 || moves != 4)
            throw new InvalidOperationException("Unconfirmed Exorcism attempts must yield to movement during the retry window.");
    }

    private static void VerifyPaladinPostKillPredicate(Assembly assembly)
    {
        Type retribution = assembly.GetType("Singular.ClassSpecific.Paladin.Retribution")
            ?? throw new InvalidOperationException("Singular Retribution routine was not found.");
        MethodInfo predicate = retribution
            .GetNestedTypes(BindingFlags.NonPublic)
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic))
            .SingleOrDefault(method => method.Name ==
                "<CreateRetributionPaladinNormalPullAndCombat>b__2_12")
            ?? throw new InvalidOperationException("Logged Retribution post-kill predicate was not found.");
        var originalPlayer = Styx.WoWInternals.ObjectManager.Me;

        try
        {
            Styx.WoWInternals.ObjectManager.Me = new Styx.WoWInternals.WoWObjects.LocalPlayer(0);
            object? target = predicate.IsStatic
                ? null
                : System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(predicate.DeclaringType!);
            predicate.Invoke(target, new object?[predicate.GetParameters().Length]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is NullReferenceException)
        {
            throw new InvalidOperationException(
                "Retribution must tolerate CurrentTarget disappearing immediately after a kill.",
                ex.InnerException);
        }
        finally
        {
            Styx.WoWInternals.ObjectManager.Me = originalPlayer;
        }
    }
}
