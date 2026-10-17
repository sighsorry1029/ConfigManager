using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Microsoft.CSharp;

namespace ConfigurationManager.Tests
{
    internal static class PathOfValheimanCompatibilityTests
    {
        private const string PovGuid = "treextr.pathofvalheiman";
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static Assembly _assembly;
        private static Assembly _bepinex;
        private static Assembly _provider;
        private static Type _bridge;
        private static Type _state;
        private static string _root;
        private static int _entryId;

        internal static void Run(Assembly assembly, ICollection<string> failures, string temporaryRoot)
        {
            _assembly = assembly;
            _bepinex = Assembly.Load("BepInEx");
            _bridge = assembly.GetType("ConfigurationManager.PathOfValheimanCompatibility", true);
            _root = temporaryRoot;
            _provider = CompileProvider(false);
            _state = _provider.GetType("PassiveTree.TestState", true);
            int passed = 0;
            try
            {
                Case("absent PoV preserves normal config reads and writes", AbsentProvider, failures, ref passed);
                Case("PoV effective values and ordinary config fallback", EffectiveValues, failures, ref passed);
                Case("PoV live role, write locks and UI state", LiveLocks, failures, ref passed);
                Case("PoV bridge leaves other owners unchanged", OtherOwners, failures, ref passed);
                Case("open editor rechecks PoV lock before Apply", EditorRoleChange, failures, ref passed);
                Case("PoV callback failures fail closed only for PoV", CallbackFailures, failures, ref passed);
                Case("PoV exact signature mismatch fails closed", SignatureMismatch, failures, ref passed);
                Case("PoV reset releases callbacks and allows rebinding", Reset, failures, ref passed);
                Case("existing PoV Harmony prefixes are not duplicated", NativePrefixes, failures, ref passed);
                Console.WriteLine("PathOfValheimanCompatibility: " + passed + " cases passed");
            }
            finally
            {
                Bridge("Reset");
            }
        }

        private static void AbsentProvider()
        {
            Bridge("Reset");
            object entry = Entry(PovGuid);
            Check((string)Call(entry, "Get") == "local", "missing optional provider retains config value");
            Call(entry, "Set", "updated");
            Check(Raw(entry) == "updated" && !(bool)Bridge("IsLocked", entry),
                "missing optional provider neither blocks writes nor disables rows");
        }

        private static void EffectiveValues()
        {
            object entry = BoundEntry();
            State("HasValue", true);
            State("EffectiveValue", "world");
            Check((string)Call(entry, "Get") == "world" && Raw(entry) == "local",
                "world display value does not overwrite local config");
            State("EffectiveValue", "changed world");
            Check((string)Call(entry, "Get") == "changed world", "effective values are not cached across reads");
            State("HasValue", false);
            Check((string)Call(entry, "Get") == "local", "provider declining an entry falls back to config");
        }

        private static void LiveLocks()
        {
            object entry = BoundEntry();
            State("Lock", true);
            Check(!(bool)Bridge("IsLocked", entry) && !(bool)State("LastClient"),
                "host role is passed to the provider policy");
            Call(entry, "Set", "host edit");
            Check(Raw(entry) == "host edit", "host is allowed when provider permits it");
            State("Client", true);
            Check((bool)Bridge("IsLocked", entry) && (bool)State("LastClient"),
                "current client role immediately disables locked rows");
            Call(entry, "Set", "client edit");
            Call(entry, "Set", Property(entry, "DefaultValue"));
            Check(Raw(entry) == "host edit", "locked direct writes and Reset preserve the stored value");
            State("Lock", false);
            Call(entry, "Set", "unlocked edit");
            Check(Raw(entry) == "unlocked edit" && !(bool)Bridge("IsLocked", entry),
                "provider unlock immediately allows edits");
        }

        private static void OtherOwners()
        {
            BoundEntry();
            object other = Entry("test.unrelated");
            State("Match", Property(other, "Entry"));
            State("HasValue", true);
            State("Lock", true);
            State("Client", true);
            Check((string)Call(other, "Get") == "local", "owner GUID prevents foreign value interception");
            Call(other, "Set", "other edit");
            Check(Raw(other) == "other edit" && !(bool)Bridge("IsLocked", other),
                "another mod keeps its ordinary write and UI policy");
            Check((int)State("ValueCalls") == 0 && (int)State("LockCalls") == 0,
                "other owners do not invoke PoV callbacks");
        }

        private static void EditorRoleChange()
        {
            object entry = BoundEntry();
            State("Lock", true);
            object window = Editor(entry);
            Call(window, "EditSetting", entry);
            Check((bool)Property(window, "IsOpen"), "host can open an unlocked editor");
            Field(window, "valueToSet", "pending edit");
            State("Client", true);
            Call(window, "ApplySettingValue");
            Check(Raw(entry) == "local", "Apply rechecks the role after the window was opened");
            object lockedWindow = Editor(entry);
            Call(lockedWindow, "EditSetting", entry);
            Check(!(bool)Property(lockedWindow, "IsOpen"), "client cannot newly open a locked editor");
            State("Client", false);
            Call(window, "ApplySettingValue");
            Check(Raw(entry) == "pending edit", "pending edit can apply after permission returns");
        }

        private static void CallbackFailures()
        {
            foreach (string failingCallback in new[] { "ThrowValue", "ThrowLock", "ThrowClient" })
            {
                object entry = BoundEntry();
                State(failingCallback, true);
                if (failingCallback == "ThrowValue")
                    Check((string)Call(entry, "Get") == "local", "read failure preserves local fallback");
                else
                    Check((bool)Bridge("IsLocked", entry), "permission failure disables the row");
                State(failingCallback, false);
                Call(entry, "Set", "unsafe retry");
                Check(Raw(entry) == "local" && (bool)Bridge("IsLocked", entry),
                    "failed adapter remains closed until explicitly rebound: " + failingCallback);
                object other = Entry("test.unrelated");
                Call(other, "Set", "safe other edit");
                Check(Raw(other) == "safe other edit", "failure does not block unrelated owners");
            }
        }

        private static void SignatureMismatch()
        {
            Assembly incompatible = CompileProvider(true);
            Check(!(bool)Bridge("Bind", incompatible), "object parameter cannot replace the exact ConfigEntryBase signature");
            object entry = Entry(PovGuid);
            Call(entry, "Set", "unsafe mismatch edit");
            Check(Raw(entry) == "local" && (bool)Bridge("IsLocked", entry),
                "recognized incompatible PoV is distinguishable from absent PoV");
            Check((string)Call(entry, "Get") == "local", "signature mismatch still displays stored value");
        }

        private static void Reset()
        {
            object entry = BoundEntry();
            State("HasValue", true);
            State("Client", true);
            State("Lock", true);
            Bridge("Reset");
            Call(entry, "Set", "after teardown");
            Check((string)Call(entry, "Get") == "after teardown" && !(bool)Bridge("IsLocked", entry),
                "teardown restores ordinary config behavior");
            Check((int)State("ValueCalls") == 0 && (int)State("LockCalls") == 0,
                "teardown does not retain provider callbacks");
            Check((bool)Bridge("Bind", _provider) && (bool)Bridge("IsLocked", entry),
                "fresh initialization can rebind the same loaded provider");
        }

        private static void NativePrefixes()
        {
            Assembly harmonyAssembly = Assembly.Load("0Harmony");
            Type harmonyType = harmonyAssembly.GetType("HarmonyLib.Harmony", true);
            Type harmonyMethodType = harmonyAssembly.GetType("HarmonyLib.HarmonyMethod", true);
            object harmony = Activator.CreateInstance(harmonyType, PovGuid);
            MethodInfo patch = harmonyType.GetMethods().Single(method => method.Name == "Patch" &&
                method.GetParameters().Length == 5);
            MethodInfo unpatch = harmonyType.GetMethod("UnpatchSelf", Instance);
            Type entryType = _assembly.GetType("ConfigurationManager.ConfigSettingEntry", true);
            try
            {
                foreach (string name in new[] { "Get", "Set" })
                {
                    MethodInfo target = entryType.GetMethod(name == "Get" ? "Get" : "SetValue", Instance);
                    MethodInfo prefix = _provider.GetType("PassiveTree.ConfigManagerWorldSettings+" + name, true)
                        .GetMethod("Prefix", Static);
                    object harmonyMethod = Activator.CreateInstance(harmonyMethodType, prefix);
                    patch.Invoke(harmony, new[] { (object)target, harmonyMethod, null, null, null });
                }
                object entry = BoundEntry();
                Check((string)Call(entry, "Get") == "local" && (int)State("ValueCalls") == 1,
                    "native Get fallback calls the provider once, not twice");
                Call(entry, "Set", "native edit");
                Check(Raw(entry) == "native edit" && (int)State("LockCalls") == 1,
                    "native Set owns the lock check without duplicate bridge calls");
                State("Client", true);
                State("Lock", true);
                Call(entry, "Set", "blocked native edit");
                Check(Raw(entry) == "native edit", "native locking remains active");
                Check((bool)Bridge("IsLocked", entry), "editor guard still checks locks with native prefixes present");
            }
            finally
            {
                unpatch.Invoke(harmony, null);
                Bridge("Reset");
            }
        }

        private static object BoundEntry()
        {
            _state.GetMethod("Reset", Static).Invoke(null, null);
            Check((bool)Bridge("Bind", _provider), "matching public provider signatures bind");
            object entry = Entry(PovGuid);
            State("Match", Property(entry, "Entry"));
            return entry;
        }

        private static object Entry(string guid)
        {
            Type configType = _bepinex.GetType("BepInEx.Configuration.ConfigFile", true);
            object config = Activator.CreateInstance(configType,
                Path.Combine(_root, "pov-" + (++_entryId) + ".cfg"), false, null);
            configType.GetProperty("SaveOnConfigSet").SetValue(config, false);
            Type definitionType = _bepinex.GetType("BepInEx.Configuration.ConfigDefinition", true);
            object definition = Activator.CreateInstance(definitionType, "General", "Value");
            MethodInfo bind = configType.GetMethods().Single(method => method.Name == "Bind" &&
                method.IsGenericMethodDefinition && method.GetParameters().Length == 3 &&
                method.GetParameters()[0].ParameterType == definitionType).MakeGenericMethod(typeof(string));
            object configEntry = bind.Invoke(config, new[] { definition, "local", null });
            object entry = Activator.CreateInstance(_assembly.GetType("ConfigurationManager.ConfigSettingEntry", true), configEntry, null);
            object metadata = Activator.CreateInstance(_bepinex.GetType("BepInEx.BepInPlugin", true), guid, "Test", "1.0.0");
            entry.GetType().GetProperty("PluginInfo", Instance).SetValue(entry, metadata);
            return entry;
        }

        private static object Editor(object entry)
        {
            // Bypass Unity-dependent sizing; exercise the real managed open/apply paths.
            object window = FormatterServices.GetUninitializedObject(_assembly.GetType("ConfigurationManager.SettingEditWindow", true));
            Field(window, "setting", entry);
            Field(window, "separatedString", new List<string>());
            Field(window, "separatedStringDefault", new List<string>());
            Field(window, "vectorParts", new List<string>());
            Field(window, "vectorFloats", new List<float>());
            Field(window, "vectorDefault", new List<float>());
            Field(window, "<SettingDrawHandlers>k__BackingField", new Dictionary<Type, Action>());
            return window;
        }

        private static Assembly CompileProvider(bool mismatch)
        {
            const string source = @"
using System;
using BepInEx.Configuration;
namespace PassiveTree {
    public static class TestState {
        public static object Match, EffectiveValue;
        public static bool HasValue, Lock, Client, LastClient, ThrowValue, ThrowLock, ThrowClient;
        public static int ValueCalls, LockCalls;
        public static void Reset() {
            Match = null; EffectiveValue = ""world"";
            HasValue = Lock = Client = LastClient = ThrowValue = ThrowLock = ThrowClient = false;
            ValueCalls = LockCalls = 0;
        }
    }
    public static class DifficultyAdmin {
        public static bool IsClient { get {
            if (TestState.ThrowClient) throw new InvalidOperationException(""client failure"");
            return TestState.Client;
        } }
    }
    public static class ConfigManagerWorldSettings {
        public static bool TryValue(ConfigEntryBase entry, out object value) {
            TestState.ValueCalls++;
            if (TestState.ThrowValue) throw new InvalidOperationException(""value failure"");
            value = TestState.EffectiveValue;
            return ReferenceEquals(entry, TestState.Match) && TestState.HasValue;
        }
        public static bool Locked(ConfigEntryBase entry, bool client) {
            TestState.LockCalls++; TestState.LastClient = client;
            if (TestState.ThrowLock) throw new InvalidOperationException(""lock failure"");
            return ReferenceEquals(entry, TestState.Match) && TestState.Lock && client;
        }
        public static class Get {
            public static bool Prefix(object __instance, ref object __result) {
                var entry = (ConfigEntryBase)__instance.GetType().GetProperty(""Entry"").GetValue(__instance, null);
                return !TryValue(entry, out __result);
            }
        }
        public static class Set {
            public static bool Prefix(object __instance) {
                var entry = (ConfigEntryBase)__instance.GetType().GetProperty(""Entry"").GetValue(__instance, null);
                return !Locked(entry, DifficultyAdmin.IsClient);
            }
        }
    }
}";
            string text = mismatch
                ? source.Replace("public static bool TryValue(ConfigEntryBase entry, out object value)",
                    "public static bool TryValue(object entry, out object value)")
                : source;
            string output = Path.Combine(_root, "PoVTestProvider-" + Guid.NewGuid().ToString("N") + ".dll");
            using (var compiler = new CSharpCodeProvider())
            {
                var options = new CompilerParameters(new[] { "System.dll", _bepinex.Location }, output)
                { GenerateExecutable = false, GenerateInMemory = false };
                CompilerResults result = compiler.CompileAssemblyFromSource(options, text);
                Check(!result.Errors.HasErrors, "fake provider compiler errors: " +
                    string.Join("; ", result.Errors.Cast<CompilerError>().Select(error => error.ToString())));
                return result.CompiledAssembly;
            }
        }

        private static string Raw(object entry) => (string)Property(Property(entry, "Entry"), "BoxedValue");
        private static object Property(object target, string name) => target.GetType().GetProperty(name, Instance).GetValue(target);
        private static void Field(object target, string name, object value) => target.GetType().GetField(name, Instance).SetValue(target, value);
        private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Instance).Invoke(target, args);
        private static object Bridge(string name, params object[] args) => _bridge.GetMethod(name, Static).Invoke(null, args);
        private static object State(string name) => _state.GetField(name, Static).GetValue(null);
        private static void State(string name, object value) => _state.GetField(name, Static).SetValue(null, value);
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Case(string name, Action action, ICollection<string> failures, ref int passed)
        {
            try { action(); passed++; Console.WriteLine("PASS " + name); }
            catch (Exception ex)
            {
                while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
                failures.Add(name + ": " + ex);
            }
            finally { Bridge("Reset"); }
        }
    }
}
