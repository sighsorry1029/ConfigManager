using System;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;

namespace ConfigurationManager
{
    // PoV can initialize before this assembly is loaded and permanently skip its own
    // ConfigManager patches. Use its public policy helpers after all plugins have awakened.
    internal static class PathOfValheimanCompatibility
    {
        private const string PluginGuid = "treextr.pathofvalheiman";
        private const string ProviderTypeName = "PassiveTree.ConfigManagerWorldSettings";
        private delegate bool TryValueDelegate(ConfigEntryBase entry, out object value);

        private static TryValueDelegate _tryValue;
        private static Func<ConfigEntryBase, bool, bool> _locked;
        private static Func<bool> _isClient;
        private static bool _nativeGet;
        private static bool _nativeSet;
        private static bool _faulted;

        internal static void Initialize()
        {
            Reset();
            if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var plugin) || plugin.Instance == null)
                return;

            Bind(plugin.Instance.GetType().Assembly);
        }

        // Also allows isolated tests to provide the same public API without loading a game world.
        internal static bool Bind(Assembly assembly)
        {
            Reset();
            try
            {
                var provider = assembly.GetType(ProviderTypeName, true);
                var admin = assembly.GetType("PassiveTree.DifficultyAdmin", true);
                var tryValue = RequireMethod(provider, "TryValue", typeof(ConfigEntryBase), typeof(object).MakeByRefType());
                if (!tryValue.GetParameters()[1].IsOut)
                    throw new MissingMethodException(provider.FullName, "TryValue(ConfigEntryBase, out object)");

                var locked = RequireMethod(provider, "Locked", typeof(ConfigEntryBase), typeof(bool));
                var isClient = RequireMethod(admin, "get_IsClient");
                _tryValue = (TryValueDelegate)Delegate.CreateDelegate(typeof(TryValueDelegate), tryValue);
                _locked = (Func<ConfigEntryBase, bool, bool>)Delegate.CreateDelegate(typeof(Func<ConfigEntryBase, bool, bool>), locked);
                _isClient = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), isClient);

                _nativeGet = HasNativePrefix(assembly, typeof(ConfigSettingEntry).GetMethod(nameof(ConfigSettingEntry.Get)), "Get");
                _nativeSet = HasNativePrefix(assembly, typeof(ConfigSettingEntry).GetMethod("SetValue", BindingFlags.Instance | BindingFlags.NonPublic), "Set");
                ConfigurationManager.LogInfo(_nativeGet && _nativeSet
                    ? "Path of Valheiman configuration compatibility enabled; existing value/write patches retained and editor locks connected."
                    : "Path of Valheiman configuration compatibility enabled; missing value/write patches and editor locks are handled by ConfigManager.", always: true);
                return true;
            }
            catch (Exception exception)
            {
                Fail(exception);
                return false;
            }
        }

        internal static void Reset()
        {
            _tryValue = null;
            _locked = null;
            _isClient = null;
            _nativeGet = false;
            _nativeSet = false;
            _faulted = false;
        }

        internal static bool TryGetValue(ConfigSettingEntry setting, out object value)
        {
            value = null;
            if (!IsPathOfValheiman(setting) || _nativeGet || _tryValue == null)
                return false;

            try
            {
                return _tryValue(setting.Entry, out value);
            }
            catch (Exception exception)
            {
                value = null;
                Fail(exception);
                return false;
            }
        }

        internal static bool IsLocked(ConfigSettingEntry setting)
        {
            if (!IsPathOfValheiman(setting))
                return false;
            if (_faulted)
                return true;
            if (_locked == null)
                return false;

            try
            {
                // Role and policy can change with connections, world settings and admin status.
                return _locked(setting.Entry, _isClient());
            }
            catch (Exception exception)
            {
                Fail(exception);
                return true;
            }
        }

        internal static bool ShouldBlockSet(ConfigSettingEntry setting)
        {
            return (!_nativeSet || _faulted) && IsLocked(setting);
        }

        private static bool IsPathOfValheiman(ConfigSettingEntry setting)
        {
            return string.Equals(setting?.PluginInfo?.GUID, PluginGuid, StringComparison.Ordinal);
        }

        private static MethodInfo RequireMethod(Type type, string name, params Type[] arguments)
        {
            var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
                null, arguments, null);
            if (method == null || method.ReturnType != typeof(bool) || method.ContainsGenericParameters)
                throw new MissingMethodException(type.FullName, name);
            var parameters = method.GetParameters();
            for (var i = 0; i < parameters.Length; i++)
                if (parameters[i].ParameterType != arguments[i])
                    throw new MissingMethodException(type.FullName, name);
            return method;
        }

        private static bool HasNativePrefix(Assembly assembly, MethodBase target, string nestedType)
        {
            var patches = target == null ? null : Harmony.GetPatchInfo(target);
            if (patches == null)
                return false;

            foreach (var prefix in patches.Prefixes)
            {
                var method = prefix.PatchMethod;
                if (prefix.owner == PluginGuid && method?.DeclaringType?.Assembly == assembly && method.Name == "Prefix" &&
                    method.DeclaringType.FullName == ProviderTypeName + "+" + nestedType)
                    return true;
            }
            return false;
        }

        private static void Fail(Exception exception)
        {
            _tryValue = null;
            _locked = null;
            _isClient = null;
            if (_faulted)
                return;

            _faulted = true;
            ConfigurationManager.LogWarning("Path of Valheiman configuration integration failed; its settings are read-only in ConfigManager until restart to preserve its write restrictions. " + exception.GetType().Name + ": " + exception.Message);
        }
    }
}
