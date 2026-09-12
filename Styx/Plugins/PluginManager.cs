#nullable disable
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Styx.Helpers;
using Styx.Plugins.PluginClass;

namespace Styx.Plugins
{
    /// <summary>
    /// Manages plugin loading, compilation, and execution.
    /// </summary>
    public static class PluginManager
    {
        /// <summary>
        /// Gets whether the plugin system is initialized.
        /// </summary>
        public static bool IsInitialized { get; private set; }

        /// <summary>
        /// Gets whether plugins are currently being built/compiled.
        /// </summary>
        public static bool IsBuildingPlugins { get; private set; }

        /// <summary>Set during teardown so cleanup-disables don't rewrite the saved EnabledPlugins list.</summary>
        public static bool IsTearingDown { get; set; }

        /// <summary>
        /// Gets all loaded plugins.
        /// </summary>
        public static List<PluginContainer> Plugins { get; private set; }
        private static readonly Dictionary<string, DateTime> SlowPluginLogTimes =
            new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private static readonly HashSet<string> UnavailableEnabledPlugins =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Gets the path to the Plugins directory.
        /// </summary>
        public static string PluginsDirectory => Path.Combine(Logging.ApplicationPath, "Plugins");

        static PluginManager()
        {
            Plugins = new List<PluginContainer>();
        }

        /// <summary>
        /// Pulses all enabled plugins.
        /// </summary>
        internal static void Pulse()
        {
            for (int i = 0; i < Plugins.Count; i++)
            {
                if (Plugins[i].Enabled)
                {
                    PluginContainer container = Plugins[i];
                    var timer = Stopwatch.StartNew();
                    try
                    {
                        container.Plugin.Pulse();
                    }
                    catch (Exception ex)
                    {
                        Logging.WriteException(ex);
                    }
                    finally
                    {
                        timer.Stop();
                        string pluginName = container.Plugin.Name ?? container.Plugin.GetType().Name;
                        SlowPluginLogTimes.TryGetValue(pluginName, out DateTime previousLogUtc);
                        DateTime nowUtc = DateTime.UtcNow;
                        if (ShouldLogSlowPluginPulse(timer.ElapsedMilliseconds, nowUtc, previousLogUtc))
                        {
                            SlowPluginLogTimes[pluginName] = nowUtc;
                            Logging.WriteDiagnostic(
                                "[Pulse] Slow plugin: name={0} elapsed={1}ms",
                                pluginName,
                                timer.ElapsedMilliseconds);
                        }
                    }
                }
            }
        }

        internal static bool ShouldLogSlowPluginPulse(
            long elapsedMilliseconds,
            DateTime nowUtc,
            DateTime previousLogUtc)
        {
            return elapsedMilliseconds >= 20
                && nowUtc - previousLogUtc >= TimeSpan.FromSeconds(10);
        }

        /// <summary>
        /// Initializes the plugin system.
        /// </summary>
        /// <param name="defaultEnabled">Names of plugins to enable by default.</param>
        public static void Initialize(params string[] defaultEnabled)
        {
            if (!IsInitialized)
            {
                RefreshPlugins(defaultEnabled);
                // Note: Plugin.Initialize() is already called by PluginContainer.Enabled setter
                // No need to call it again here
                IsInitialized = true;
            }
        }

        /// <summary>
        /// Updates the EnabledPlugins property in CharacterSettings.
        /// Note: Does NOT save immediately - save is done at app close or bot start/stop (HB 4.3.4 pattern).
        /// </summary>
        public static void UpdateEnabledPlugins()
        {
            // don't rewrite the saved list during build or teardown — both would persist an empty set
            if (IsBuildingPlugins || IsTearingDown)
                return;

            try
            {
                var enabledPluginNames = Plugins
                    .Where(p => p.Enabled)
                    .Select(p => p.Name)
                    .Concat(UnavailableEnabledPlugins)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                
                Helpers.CharacterSettings.Instance.EnabledPlugins = enabledPluginNames;
                // Note: No Save() here - HB 4.3.4 saves at window close and bot start/stop
            }
            catch (Exception ex)
            {
                Helpers.Logging.WriteException(ex);
            }
        }

        /// <summary>
        /// Saves the list of enabled plugins to CharacterSettings (legacy compatibility).
        /// </summary>
        [Obsolete("Use UpdateEnabledPlugins() instead. Saving is handled globally.")]
        public static void SaveEnabledPlugins()
        {
            UpdateEnabledPlugins();
        }

        /// <summary>
        /// Refreshes the plugin list by reloading from the Plugins directory.
        /// </summary>
        /// <param name="defaultEnabled">Names of plugins to enable by default.</param>
        public static void RefreshPlugins(params string[] defaultEnabled)
        {
            if (IsBuildingPlugins)
                return;

            try
            {
                IsBuildingPlugins = true;
                List<PluginContainer> previousPlugins = Plugins.ToList();
                var replacementPlugins = new List<PluginContainer>();
                var requestedEnabled = new HashSet<string>(
                    defaultEnabled ?? Array.Empty<string>(),
                    StringComparer.OrdinalIgnoreCase);
                bool hadLoadErrors = false;

                // Force garbage collection to release old plugin assemblies
                GC.Collect();
                GC.WaitForPendingFinalizers();

                // Scan the main assembly for built-in HBPlugin subclasses (e.g. LeaderPlugin)
                try
                {
                    foreach (Type type in Assembly.GetExecutingAssembly().GetTypes())
                    {
                        if (!type.IsAbstract && typeof(HBPlugin).IsAssignableFrom(type))
                        {
                            HBPlugin plugin = (HBPlugin)Activator.CreateInstance(type);
                            replacementPlugins.Add(new PluginContainer(plugin, false));
                        }
                    }
                }
                    catch (Exception ex)
                    {
                        hadLoadErrors = true;
                        Logging.WriteException(ex);
                }

                string pluginsPath = Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                    "Plugins");

                if (!Directory.Exists(pluginsPath))
                {
                    Directory.CreateDirectory(pluginsPath);
                    Logging.Write("No plugins found. Place plugins in the Plugins directory.");
                    return;
                }

                var files = new List<string>();
                files.AddRange(Directory.GetFiles(pluginsPath, "*.cs", SearchOption.TopDirectoryOnly));
                files.AddRange(Directory.GetDirectories(pluginsPath, "*", SearchOption.TopDirectoryOnly));

                if (files.Count == 0)
                {
                    Logging.Write("No plugins found. Place plugins in the Plugins directory.");
                    return;
                }

                for (int i = 0; i < files.Count; i++)
                {
                    try
                    {
                        List<HBPlugin> loadedPlugins = CompileAndLoadFrom(files[i]);
                        foreach (HBPlugin plugin in loadedPlugins)
                        {
                            replacementPlugins.Add(new PluginContainer(plugin, false));
                        }
                    }
                    catch (CompilerErrorsException ex)
                    {
                        hadLoadErrors = true;
                        Logging.Write("Plugin from {0} could not be compiled. Compiler errors:", files[i]);
                        Logging.Write(ex.ToString());
                    }
                    catch (Exception ex)
                    {
                        hadLoadErrors = true;
                        Logging.Write("Error loading plugin: {0}", files[i]);
                        Logging.WriteException(ex);
                    }
                }

                if (hadLoadErrors && previousPlugins.Count > 0)
                {
                    foreach (PluginContainer replacement in replacementPlugins)
                    {
                        try { replacement.Plugin.Dispose(); } catch { }
                    }
                    Logging.Write("Plugin refresh failed; keeping the previous {0} loaded plugins.", previousPlugins.Count);
                    throw new InvalidOperationException("One or more plugins failed to compile or load; the previous plugin set was preserved.");
                }

                foreach (PluginContainer previous in previousPlugins)
                {
                    if (previous.Enabled)
                        previous.Enabled = false;
                }

                Plugins = replacementPlugins;
                foreach (PluginContainer container in Plugins)
                {
                    if (requestedEnabled.Contains(container.Name))
                        container.Enabled = true;
                }

                UnavailableEnabledPlugins.Clear();
                if (hadLoadErrors)
                {
                    foreach (string requestedName in requestedEnabled)
                    {
                        if (!Plugins.Any(p => string.Equals(p.Name, requestedName, StringComparison.OrdinalIgnoreCase)))
                            UnavailableEnabledPlugins.Add(requestedName);
                    }
                }

                Logging.Write("Plugin loading complete. {0} plugins loaded.", Plugins.Count);
                
                if (Plugins.Count == 0)
                {
                    Logging.Write("No plugins found. Place plugins in the Plugins directory.");
                }
                else
                {
                    Logging.Write(hadLoadErrors
                        ? "Plugins loaded with errors; unavailable enabled plugins were preserved in settings."
                        : "Plugins refreshed successfully.");
                }
            }
            finally
            {
                IsBuildingPlugins = false;
            }
        }

        /// <summary>
        /// Compiles and loads plugins from a path.
        /// </summary>
        /// <param name="path">The path to compile from (file or directory).</param>
        /// <returns>List of loaded plugins.</returns>
        public static List<HBPlugin> CompileAndLoadFrom(string path)
        {
            var classCollection = new ClassCollection<HBPlugin>();
            CompilerResults compilerResults;
            classCollection.CompileAndLoadFrom(path, out compilerResults);

            if (compilerResults != null && compilerResults.Errors.HasErrors)
            {
                throw new CompilerErrorsException(Utilities.FormatCompilerErrors(compilerResults));
            }

            return classCollection;
        }
    }
}
