# Mounted/ground integration regression

This standalone harness compiles the real mounted-combat bridge and mounted
escape policy together with the real ground-transition context, runtime, state
machine, landing search, collision/mesh query adapter, and TreeSharp action
guard. `Boundary.cs` is a provenance-marked copy of the
`IndoorApproachRegressionTests` controlled client/native boundary with only the
combat observation leaves needed by `MountedCombatTransition` added.

From a clean checkout at the repository root, using the repository-pinned x86
SDK:

```powershell
& 'D:/World of Warcraft 3.3.5a/CB/.dotnet-sdk/dotnet.exe' build Tools/MountedGroundIntegrationRegressionTests/MountedGroundIntegrationRegressionTests.csproj -c Release -p:PlatformTarget=x86 --nologo
& 'D:/World of Warcraft 3.3.5a/CB/.dotnet-sdk/dotnet.exe' Tools/MountedGroundIntegrationRegressionTests/bin/Release/net10.0/MountedGroundIntegrationRegressionTests.dll
```

The fixture deliberately does not attach to WoW, execute the native game loop,
or claim live navmesh traversal. Collision, mesh, movement, process/executor,
mount removal, and combat observations terminate at controlled leaves. Commands
do not mutate those observations: each later landing/unmount state is supplied
explicitly by the test after the corresponding production command has been
observed. The proof therefore covers ownership, admission, ordering,
acknowledgement, cancellation, replacement, and reentrant cleanup across the
linked production owners; it does not prove original-client physics, real map
geometry, server combat state, or successful in-game landing.
