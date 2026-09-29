# Plugin refresh and reload behavior

Refresh discovers plugin source, prepares replacements and enables the requested
plugins. It is not an application restart or an assembly-unload operation.

## Compiled types and state

- Unchanged source and compilation inputs reuse the compiled plugin types, but
  construct new plugin instances. Instance fields start from their constructors;
  static fields retain their values.
- A source or selected reference-content change requires compilation again. A
  successfully compiled new plugin type has its own static state. Old assemblies
  remain loaded in the process; .NET's default load context does not unload them.
- Cache identity includes source/resx content, resolved reference paths and file
  contents, and the effective parse/output/optimization/platform options used by
  `SourceCompiler`. It does not use dependency timestamps as a substitute for
  content. Compiler output filenames and unused legacy CodeDom settings do not
  identify compilation inputs.
- Resolved reference candidates that fail image validation still contribute
  their bytes to the identity. Cache checks validate one image at a time with a
  disposable reader; they do not retain another set of Roslyn metadata blocks.
- Reference selection follows the same resolver as compilation, including source
  `AddRef` directives, loaded assemblies and runtime/application references.
  Loading another host assembly can therefore change the next compilation's
  reference set even if plugin source is unchanged.

Public Refresh currently constructs the default compiler; it exposes no separate
mutable compiler-options configuration. Supported source directives affect the
effective options. Changing a CodeDom property that the Roslyn compiler does not
consume does not establish a changed compilation setting.

Before/after input checks prevent caching a result when an observed input changes
during compilation. They are not an immutable filesystem snapshot; keep plugin
files stable while a refresh is running.

**Restart the application after updating dependencies that may already be loaded.**
Recompiling a plugin against changed DLL bytes does not replace an already loaded
dependency assembly. A new compile-time constant is evidence of recompilation,
not evidence that runtime dependency code has been replaced. Refresh also does
not reset arbitrary static state in shared host or dependency assemblies.
This follows .NET's [load-context versioning rules](https://learn.microsoft.com/en-us/dotnet/core/dependency-loading/understanding-assemblyloadcontext#versioning-rules);
unloading requires a [collectible load context](https://learn.microsoft.com/en-us/dotnet/standard/assembly/unloadability),
which this compiler does not create.

## Failure and cleanup behavior

With an existing plugin set, required compilation/construction failures preserve
the previous set. Replacement metadata is read before the old set is retired;
rejected candidates are disposed. A failed changed-input compilation leaves the
last valid cache entry available if those original inputs are restored.

After successful preparation, Refresh retires old enabled plugins and publishes
the replacements. Failed replacement activation leaves that replacement disabled
and attempts cleanup; it does **not** restore the previously retired set. An
`OnDisable` exception cannot skip the subsequent disposal attempt. Notifications
describe completed lifecycle transitions, including a failed enable attempt.

These rules do not make arbitrary plugin callbacks transactional. Plugins remain
responsible for releasing their resources, tolerating partial initialization and
managing their shared state. Concurrent/reentrant refresh and original-client
integration are separate audit gates. See the W94–W96 audit checkpoints for the
exact regression coverage and evidence limits.
