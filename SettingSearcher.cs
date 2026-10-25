// Based on code made by MarC0 / ManlyMarco
// Copyright 2018 GNU General Public License v3.0

using System;
using BepInEx;
using BepInEx.Configuration;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using UnityEngine;

namespace ConfigurationManager
{
    internal static class SettingSearcher
    {
        /// <summary>
        /// Search for all instances of BaseUnityPlugin loaded by chainloader or other means.
        /// </summary>
        public static BaseUnityPlugin[] FindPlugins()
        {
            // Search for instances of BaseUnityPlugin to also find dynamically loaded plugins.
            // Still look inside Chainloader.PluginInfos in case the BepInEx_Manager GameObject uses HideFlags.HideAndDontSave, which hides it from Object.Find methods.
            return Chainloader.PluginInfos.Values.Select(x => x?.Instance)
                              .Union(UnityEngine.Object.FindObjectsByType(typeof(BaseUnityPlugin), FindObjectsInactive.Exclude, FindObjectsSortMode.None).OfType<BaseUnityPlugin>())
                              .Where(plugin => plugin != null)
                              .ToArray();
        }

        public static void CollectSettings(out IEnumerable<SettingEntryBase> results, out List<string> modsWithoutSettings)
        {
            modsWithoutSettings = new List<string>();
            var collectedSettings = new List<SettingEntryBase>();

            try
            {
                // Construct entries here so lazy enumeration cannot escape the error boundary.
                collectedSettings.AddRange(GetBepInExCoreConfig().ToArray());
            }
            catch (Exception ex)
            {
                ConfigurationManager.LogError("Failed to collect BepInEx core settings.");
                ConfigurationManager.LogError(ex);
            }

            foreach (var plugin in FindPlugins())
            {
                try
                {
                    var type = plugin.GetType();
                    var pluginInfo = plugin.Info?.Metadata;
                    var pluginName = pluginInfo?.Name ?? type.FullName;

                    // Dynamic loaders need not assign PluginInfo.Instance as Chainloader does.
                    // Only require the metadata and config that collecting settings actually uses.
                    if (pluginInfo == null || plugin.Config == null)
                    {
                        ConfigurationManager.LogWarning($"Skipping plugin with unavailable metadata or config: {pluginName}");
                        continue;
                    }

                    if (type.GetCustomAttributes(typeof(BrowsableAttribute), false).Cast<BrowsableAttribute>()
                            .Any(x => !x.Browsable))
                    {
                        modsWithoutSettings.Add(pluginName);
                        continue;
                    }

                    var detected = GetPluginConfig(plugin).ToList();
                    detected.RemoveAll(x => x == null || x.Browsable == false || x.PluginInfo == null);

                    if (detected.Count == 0)
                        modsWithoutSettings.Add(pluginName);
                    else
                        collectedSettings.AddRange(detected);
                }
                catch (Exception ex)
                {
                    // A failed entry or partially initialized plugin must not hide other plugins.
                    ConfigurationManager.LogError($"Failed to collect settings of plugin: {GetPluginNameSafe(plugin)}. The plugin will be skipped.");
                    ConfigurationManager.LogError(ex);
                }
            }

            collectedSettings.AddRange(ExternalSettingsRegistry.Snapshot());
            results = collectedSettings;
        }

        private static string GetPluginNameSafe(BaseUnityPlugin plugin)
        {
            try
            {
                return plugin?.Info?.Metadata?.Name ?? plugin?.GetType().FullName ?? "Unknown plugin";
            }
            catch
            {
                return plugin?.GetType().FullName ?? "Unknown plugin";
            }
        }

        /// <summary>
        /// Get entries for all core BepInEx settings
        /// </summary>
        private static IEnumerable<SettingEntryBase> GetBepInExCoreConfig()
        {
            var coreConfigProp = typeof(ConfigFile).GetProperty("CoreConfig", BindingFlags.Static | BindingFlags.NonPublic);
            if (coreConfigProp == null) throw new ArgumentNullException(nameof(coreConfigProp));

            var coreConfig = (ConfigFile)coreConfigProp.GetValue(null, null);
            if (coreConfig == null) throw new InvalidOperationException("BepInEx core config is not available.");
            var bepinMeta = new BepInPlugin("BepInEx", "BepInEx", typeof(Chainloader).Assembly.GetName().Version.ToString());

            return coreConfig.Select(kvp => (SettingEntryBase)new ConfigSettingEntry(kvp.Value, null) { IsAdvanced = true, PluginInfo = bepinMeta });
        }

        /// <summary>
        /// Get entries for all settings of a plugin
        /// </summary>
        private static IEnumerable<ConfigSettingEntry> GetPluginConfig(BaseUnityPlugin plugin)
        {
            return plugin.Config.Select(kvp => new ConfigSettingEntry(kvp.Value, plugin));
        }
    }
}
