using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;

namespace ConfigurationManager.Tests
{
    internal static class ExternalSettingsTests
    {
        private static Type _externalType;
        private static Type _registryType;
        private static MethodInfo _register;
        private static Assembly _assembly;
        private static string _temporaryRoot;
        private static int _passed;

        internal static void Run(string[] args, ICollection<string> failures)
        {
            string assemblyPath = Path.GetFullPath(args[0]);
            string corePath = Path.GetFullPath(args[1]);
            string managedPath = Path.GetFullPath(args[2]);
            string temporaryRoot = Path.GetFullPath(args[3]);
            _temporaryRoot = temporaryRoot;
            ResolveEventHandler resolver = (_, request) =>
            {
                string file = new AssemblyName(request.Name).Name + ".dll";
                foreach (string directory in new[] { corePath, managedPath, Path.GetDirectoryName(assemblyPath) })
                {
                    string path = Path.Combine(directory, file);
                    if (File.Exists(path)) return Assembly.LoadFrom(path);
                }
                return null;
            };
            AppDomain.CurrentDomain.AssemblyResolve += resolver;
            try
            {
                Assembly bepinex = Assembly.LoadFrom(Path.Combine(corePath, "BepInEx.dll"));
                Type paths = bepinex.GetType("BepInEx.Paths", true);
                paths.GetMethod("SetExecutablePath", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { Path.Combine(temporaryRoot, "ConfigManager.Tests.exe"),
                        Path.Combine(temporaryRoot, "BepInEx"), managedPath, new[] { corePath, managedPath } });
                _assembly = Assembly.LoadFrom(assemblyPath);
                _externalType = _assembly.GetType("ConfigurationManager.ExternalSetting", true);
                _registryType = _assembly.GetType("ConfigurationManager.ExternalSettingsRegistry", true);
                _register = _assembly.GetType("ConfigurationManager.ConfigurationManager", true)
                    .GetMethod("RegisterExternalSettings", BindingFlags.Public | BindingFlags.Static);
                Check(_register != null && _register.ReturnType == typeof(IDisposable), "public registration contract");

                Case("ownerless get/set/reset and recollection", OwnerlessSettings, failures);
                Case("read-only live values, write guards and withdrawal", ReadOnlySettings, failures);
                Case("replacement and stale handle isolation", Replacement, failures);
                Case("callback failures preserve other settings", CallbackFailures, failures);
                Case("invalid registration preserves active owner", InvalidRegistration, failures);
                Case("text validation and editor buffer", TextEditing, failures);
                Case("inline numeric commit, rejection, cancel and reload", InlineNumericEditing, failures);
                Case("inline numeric focus ownership and withdrawal", InlineNumericFocus, failures);
                Case("external enum cache, reload, selection and dynamic fallback", EnumCaching, failures);
                Case("external grouped flags combinations, labels and editor", GroupedFlags, failures);
                Case("typed Reset and text edit intent", TypedTextReset, failures);
                Case("ordinary string Apply and view synchronization", StringEditing, failures);
                Case("worker registration and disposal", WorkerRegistration, failures);
                Program.RunAttributeTests(_assembly, failures);
                FileEditorTests.Run(_assembly, failures, temporaryRoot);
                PathOfValheimanCompatibilityTests.Run(_assembly, failures, temporaryRoot);
                Console.WriteLine("ExternalSettings: " + _passed + " cases passed");
            }
            catch (Exception ex)
            {
                failures.Add("ExternalSettings setup: " + Unwrap(ex));
            }
            finally
            {
                AppDomain.CurrentDomain.AssemblyResolve -= resolver;
            }
        }

        private static void OwnerlessSettings()
        {
            bool value = true;
            int reads = 0;
            using (IDisposable registration = Register("test.ownerless", Setting("Enabled", typeof(bool), false,
                () => { reads++; return value; }, input => value = (bool)input)))
            {
                Check(reads == 0, "registration does not call getter");
                object entry = Entry("test.ownerless");
                Check(Get(entry, "PluginInstance") == null, "BaseUnityPlugin is not required");
                Check((string)Get(entry, "Category") == "General", "category metadata");
                Check((bool)Call(entry, "Get"), "initial getter");
                value = false;
                Check(!(bool)Call(entry, "Get"), "getter reads latest owner value");
                Call(entry, "Set", true);
                Check(value, "setter reached owner");
                Call(entry, "Set", Get(entry, "DefaultValue"));
                Check(!value, "reset uses owner setter");
                Check(ReferenceEquals(entry, Entry("test.ownerless")), "recollection retains registration");
            }
            Check(!Entries().Any(entry => Guid(entry) == "test.ownerless"), "disposed owner removed");
        }

        private static void ReadOnlySettings()
        {
            string status = "Disabled: test conflict";
            LogGroups groups = LogGroups.Errors | LogGroups.Warnings;
            int number = 120;
            int reads = 0;
            object descriptor = Setting("Status", typeof(string), string.Empty,
                () => { reads++; return status; }, null);
            Check((bool)Get(descriptor, "ReadOnly") && _externalType.GetProperty("ReadOnly").SetMethod == null,
                "null setter advertises immutable read-only metadata");
            Check(!(bool)Get(Setting("Writable", typeof(bool), false, () => true, _ => { }), "ReadOnly"),
                "normal constructor remains writable");
            object statusEntry;
            using (Register("test.read-only-status", descriptor))
            using (Register("test.read-only-groups", Setting("Groups", typeof(LogGroups), LogGroups.None, () => groups, null)))
            using (Register("test.read-only-number", Setting("Seconds", typeof(int), 0, () => number, null)))
            {
                Check(reads == 0, "read-only registration does not eagerly evaluate getter");
                statusEntry = Entry("test.read-only-status");
                Check((bool)Get(statusEntry, "ReadOnly") && (bool)Get(statusEntry, "IsActive"),
                    "read-only is distinct from inactive");
                Check((string)Call(statusEntry, "GetPreviewText") == status, "read-only status uses live preview");
                status = "Disabled: another conflict";
                Check((string)Call(statusEntry, "Get") == status, "read-only getter follows owner updates");
                Call(statusEntry, "Set", Get(statusEntry, "DefaultValue"));
                Check(status == "Disabled: another conflict", "base Reset does not write read-only values");
                Check(!(bool)Call(statusEntry, "TrySet", "changed") &&
                    ((string)Get(statusEntry, "ErrorText")).Contains("read-only"), "direct write reports read-only");
                Check(!(bool)Call(statusEntry, "TrySetText", "changed"), "text write refuses read-only values");
                Check((bool)Get(statusEntry, "IsActive"), "failed writes do not withdraw getter");

                object groupEntry = Entry("test.read-only-groups");
                object cache = NumberDrawerCall("GetExternalEnumCache", groupEntry);
                Call(cache, "SetFlag", 3, true);
                Call(groupEntry, "Set", Get(groupEntry, "DefaultValue"));
                Check(groups == (LogGroups.Errors | LogGroups.Warnings), "checkbox and Reset preserve read-only flags");
                groups = LogGroups.Debug;
                Check(Convert.ToInt32(Call(groupEntry, "Get")) == 8, "read-only checkboxes still follow owner updates");

                object numeric = Entry("test.read-only-number");
                Check(!(bool)Call(numeric, "TrySetText", "invalid number") &&
                    ((string)Get(numeric, "ErrorText")).Contains("read-only"), "read-only check precedes text parsing");
                Call(Get(numeric, "NumericEditor"), "Reset");
                Check(number == 120, "inline numeric Reset cannot write a read-only value");

                object window = CreateManagedEditor(statusEntry);
                Call(window, "EditSetting", statusEntry);
                Check(!(bool)Get(window, "IsOpen"), "read-only Edit cannot open a dialog");
                SetField(window, "valueToSet", status);
                Call(window, "ResetExternalValue");
                Check((string)GetField(window, "valueToSet") == status, "dialog Reset cannot stage read-only changes");
                Call(window, "SetExternalText", "changed");
                Call(window, "ApplySettingValue");
                Check(status == "Disabled: another conflict", "dialog Apply cannot change read-only value");
            }
            int readsBeforeWithdrawal = reads;
            Check(!(bool)Get(statusEntry, "IsActive") && (string)Call(statusEntry, "Get") == status,
                "withdrawn read-only entry retains last value");
            Check(reads == readsBeforeWithdrawal, "withdrawal releases the read-only getter");
            Check(!(bool)Call(statusEntry, "TrySetText", "changed") &&
                ((string)Get(statusEntry, "ErrorText")).Contains("no longer registered"),
                "withdrawal is reported separately from active read-only status");
            NumberDrawerCall("ClearComboboxCache");
        }

        private static void Replacement()
        {
            int staleCalls = 0;
            IDisposable old = Register("test.replace", Setting("Value", typeof(int), 1,
                () => { staleCalls++; return 1; }, _ => staleCalls++));
            object oldEntry = Entry("test.replace");
            using (IDisposable replacement = Register("test.replace", Setting("Value", typeof(int), 2, () => 2, _ => { })))
            {
                old.Dispose();
                Check((int)Call(Entry("test.replace"), "Get") == 2, "old handle preserves replacement");
                Call(oldEntry, "Get");
                Call(oldEntry, "Set", 3);
                Check(staleCalls == 0, "stale entries release callbacks");
                Check((string)Get(oldEntry, "ErrorText") != null, "stale set has feedback");
            }
            long revision = (long)GetStatic(_registryType, "Revision");
            old.Dispose();
            Check((long)GetStatic(_registryType, "Revision") == revision, "stale disposal is idempotent");
        }

        private static void CallbackFailures()
        {
            int value = 5;
            bool failRead = false;
            bool failWrite = true;
            using (Register("test.failing", Setting("Value", typeof(int), 0,
                () => failRead ? throw new InvalidOperationException("read failure") : (object)value,
                input => { if (failWrite) throw new IOException("write failure"); value = (int)input; })))
            using (Register("test.healthy", Setting("Value", typeof(int), 9, () => 9, _ => { })))
            {
                object entry = Entry("test.failing");
                Check((int)Call(entry, "Get") == 5, "last good value");
                failRead = true;
                Check((int)Call(entry, "Get") == 5, "getter exception retained value");
                Check(((string)Get(entry, "ErrorText")).Contains("read failure"), "read error feedback");
                failRead = false;
                Call(entry, "Get");
                Check(Get(entry, "ErrorText") == null, "getter recovery clears read error");
                Call(entry, "Set", 7);
                Check(value == 5 && ((string)Get(entry, "ErrorText")).Contains("write failure"), "write failure does not report success");
                Call(entry, "Get");
                Check(Get(entry, "ErrorText") != null, "getter cannot erase failed write feedback");
                failWrite = false;
                Call(entry, "Set", 7);
                Check(value == 7 && Get(entry, "ErrorText") == null, "successful retry clears error");
                Call(entry, "Set", "wrong type");
                Check(value == 7 && Get(entry, "ErrorText") != null, "wrong typed value rejected");
                Check((int)Call(Entry("test.healthy"), "Get") == 9 && Entries().Length == 2, "other owner retained");
            }
        }

        private static void InvalidRegistration()
        {
            object setting = Setting("Value", typeof(int), 3, () => 3, _ => { });
            using (Register("test.invalid", setting))
            {
                long revision = (long)GetStatic(_registryType, "Revision");
                ExpectThrow(() => Register("test.invalid", setting, setting));
                Check((long)GetStatic(_registryType, "Revision") == revision, "invalid replacement does not change registry");
                Check((int)Call(Entry("test.invalid"), "Get") == 3, "previous registration preserved");
                ExpectThrow(() => Setting("Bad", typeof(int), "wrong", () => 0, _ => { }));
                ExpectThrow(() => Setting("Bad", typeof(int), 0, null, _ => { }));
            }
        }

        private static void TextEditing()
        {
            float number = 120f;
            string text = "BepInEx = All";
            using (Register("test.text",
                Setting("Seconds", typeof(float), 120f, () => number,
                    input => { float next = (float)input; if (float.IsNaN(next) || float.IsInfinity(next) || next < 0f) throw new ArgumentException("finite and nonnegative required"); number = next; }),
                Setting("Sources", typeof(string), string.Empty, () => text,
                    input => { if (!((string)input).Contains("=")) throw new ArgumentException("source assignment required"); text = (string)input; })))
            {
                object numeric = Entries().Single(entry => (string)Get(entry, "DispName") == "Seconds");
                Check(!(bool)Call(numeric, "TrySetText", "1e-"), "incomplete number does not commit");
                Check(number == 120f, "invalid number preserves owner");
                Check(!(bool)Call(numeric, "TrySetText", "-2"), "owner validation is respected");
                Check((bool)Call(numeric, "TrySetText", "120.5") && number == 120.5f, "invariant numeric commit");
                object entry = Entries().Single(item => (string)Get(item, "DispName") == "Sources");
                object window = FormatterServices.GetUninitializedObject(_assembly.GetType("ConfigurationManager.SettingEditWindow", true));
                SetField(window, "setting", entry);
                Call(window, "SetExternalText", "unfinished source");
                Call(window, "ApplySettingValue");
                Check((string)GetField(window, "_externalText") == "unfinished source", "failed Apply preserves editor text");
                Check(((string)GetField(window, "errorOnSetting")).Contains("source assignment"), "editor shows setter error");
                Check(text == "BepInEx = All", "failed Apply preserves owner text");
                Check((bool)Call(entry, "TrySetText", "BepInEx = All\nExample = Warning"), "multiline accepted as one string");
                Check(text.Contains("\nExample"), "multiline contents preserved");
                Check((string)Call(entry, "GetPreviewText") == "BepInEx = All ...", "bounded multiline preview");
            }

        }

        private static void InlineNumericEditing()
        {
            float value = 120f;
            int writes = 0;
            bool rejectWrite = false;
            using (Register("test.inline-number", Setting("Seconds", typeof(float), 120f, () => value,
                input =>
                {
                    writes++;
                    float next = (float)input;
                    if (rejectWrite) throw new IOException("save failed");
                    if (float.IsNaN(next) || float.IsInfinity(next) || next < 0f)
                        throw new ArgumentException("finite and nonnegative required");
                    value = next;
                })))
            {
                object entry = Entry("test.inline-number");
                Check((bool)Get(entry, "UsesInlineNumericEditor"), "float uses inline number editor");
                object editor = Get(entry, "NumericEditor");
                Call(editor, "Refresh");
                string originalText = (string)Get(editor, "Text");
                Call(editor, "Refresh");
                Check(originalText == "120" && ReferenceEquals(originalText, Get(editor, "Text")), "unchanged value reuses formatted text");

                NumberInput(entry, "120", true);
                NumberInput(entry, "1e-", true);
                Check(writes == 0 && value == 120f && (string)Get(editor, "Text") == "1e-", "typing incomplete exponent only edits buffer");
                NumberInput(entry, "1e-", true, enter: true);
                Check(writes == 0 && (bool)Get(editor, "HasPendingText") && Get(entry, "ErrorText") != null, "Enter reports parse failure without owner write");
                NumberInput(entry, "1e-", false);
                value = 180f; // Owner cfg reload while rejected text is still pending.
                Call(editor, "Refresh");
                Check((string)Get(editor, "Text") == "1e-" && value == 180f, "rejected input survives blur and reload");
                NumberInput(entry, "1e-", true);
                Check((bool)NumberDrawerCall("CancelExternalNumericEdit"), "manager Escape consumes active edit before closing window");
                Check((string)Get(editor, "Text") == "180" && writes == 0 && Get(entry, "ErrorText") == null, "Escape restores latest owner value without saving");
                Check(!(bool)NumberDrawerCall("CancelExternalNumericEdit"), "second Escape has no numeric edit to consume");

                NumberInput(entry, "180", true);
                NumberInput(entry, "240.5", true);
                Check(writes == 0, "valid typing also waits for commit");
                NumberInput(entry, "240.5", true, enter: true);
                Check(value == 240.5f && writes == 1 && !(bool)Get(editor, "HasPendingText"), "Enter submits once");
                NumberInput(entry, "240.5", false);
                Check(writes == 1, "blur after Enter does not submit twice");

                NumberInput(entry, "240.5", true);
                NumberInput(entry, "-2", true);
                NumberInput(entry, "-2", false);
                Check(value == 240.5f && writes == 2 && (string)Get(editor, "Text") == "-2", "blur rejection retains persisted value and rejected text");
                Check(((string)Get(entry, "ErrorText")).Contains("nonnegative"), "owner validation error is visible");
                NumberInput(entry, "-2", false);
                Check(writes == 2, "unfocused redraw does not retry rejected writes");
                NumberInput(entry, "-2", true);
                NumberInput(entry, "300", true);
                NumberInput(entry, "300", false);
                Check(value == 300f && writes == 3 && Get(entry, "ErrorText") == null, "corrected input commits on focus loss");

                value = 360f;
                Call(editor, "Refresh");
                Check((string)Get(editor, "Text") == "360", "clean unfocused field follows cfg reload");
                NumberInput(entry, "360", true);
                value = 420f;
                NumberInput(entry, "360", false);
                Check(value == 420f && writes == 3 && (string)Get(editor, "Text") == "420", "unmodified focused text cannot overwrite reload on blur");
                NumberInput(entry, "420", true);
                NumberInput(entry, "480", true);
                NumberInput(entry, "480", true, escape: true);
                Check(value == 420f && writes == 3 && (string)Get(editor, "Text") == "420", "IMGUI Escape boundary cancels pending text");

                rejectWrite = true;
                NumberInput(entry, "420", true);
                NumberInput(entry, "600", true, enter: true);
                Check(value == 420f && writes == 4 && (string)Get(editor, "Text") == "600", "persistence exception keeps pending text");
                Call(editor, "Reset");
                Check(value == 420f && writes == 5 && (string)Get(editor, "Text") == "600" &&
                    ((string)Get(entry, "ErrorText")).Contains("save failed"), "failed Reset preserves pending edit and error");
                rejectWrite = false;
                Call(editor, "Reset");
                Check(value == 120f && writes == 6 && (string)Get(editor, "Text") == "120" &&
                    !(bool)Get(editor, "HasPendingText"), "successful Reset replaces pending edit through owner setter");
                NumberInput(entry, "120", false);
                Check(writes == 6, "later focus loss cannot resubmit text from before Reset");
            }
        }

        private static void InlineNumericFocus()
        {
            int first = 1, second = 2, writes = 0;
            IDisposable registration = Register("test.numeric-focus",
                Setting("First", typeof(int), 1, () => first, input => { first = (int)input; writes++; }),
                Setting("Second", typeof(int), 2, () => second, input => { second = (int)input; writes++; }),
                Setting("String", typeof(string), "text", () => "text", _ => { }),
                Setting("Bool", typeof(bool), true, () => true, _ => { }),
                Setting("Enum", typeof(DayOfWeek), DayOfWeek.Monday, () => DayOfWeek.Monday, _ => { }));
            try
            {
                object firstEntry = Entries().Single(entry => (string)Get(entry, "DispName") == "First");
                object secondEntry = Entries().Single(entry => (string)Get(entry, "DispName") == "Second");
                object firstEditor = Get(firstEntry, "NumericEditor");
                object secondEditor = Get(secondEntry, "NumericEditor");
                Call(firstEditor, "Refresh");
                Call(secondEditor, "Refresh");
                Check((string)Get(firstEditor, "ControlName") != (string)Get(secondEditor, "ControlName"), "distinct settings have stable distinct focus names");
                foreach (object other in Entries().Where(entry => (string)Get(entry, "DispName") != "First" && (string)Get(entry, "DispName") != "Second"))
                    Check(!(bool)Get(other, "UsesInlineNumericEditor"), "string, bool and enum keep their existing controls");

                NumberInput(firstEntry, "10", true);
                NumberInput(secondEntry, "2", true);
                Check(first == 10 && writes == 1 && !(bool)Get(firstEditor, "IsFocused"), "moving focus commits old row even if it was not drawn again");
                NumberInput(secondEntry, "20", true);
                NumberDrawerCall("FinishExternalNumericEdit");
                Check(second == 20 && writes == 2 && !(bool)Get(secondEditor, "IsFocused"), "window close or hidden row commits active edit once");
                NumberDrawerCall("FinishExternalNumericEdit");
                Check(writes == 2, "repeated list cleanup does not repeat commit");

                NumberInput(firstEntry, "30", true);
                registration.Dispose();
                NumberDrawerCall("FinishExternalNumericEdit");
                Check(writes == 2 && first == 10, "withdrawing an edited owner cannot call its released setter");
            }
            finally
            {
                registration.Dispose();
                NumberDrawerCall("FinishExternalNumericEdit");
            }
        }

        private static void NumberInput(object entry, string text, bool focused, bool enter = false, bool escape = false)
            => NumberDrawerCall("HandleExternalNumericInput", entry, text, focused, enter, escape);

        private static object NumberDrawerCall(string method, params object[] args)
            => _assembly.GetType("ConfigurationManager.SettingFieldDrawer", true)
                .GetMethod(method, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null, args);

        private enum DisplayMode
        {
            [Description("No output")] Off,
            Problems,
            Normal,
            Detailed
        }

        private sealed class DynamicDescriptionAttribute : DescriptionAttribute
        {
            internal static string Current = "first label";
            public override string Description => Current;
        }

        private enum DynamicMode
        {
            [DynamicDescription] Value
        }

        [Flags]
        private enum LogGroups
        {
            None = 0,
            [Description("Fatal + Error")] Errors = 1,
            [Description("Warning")] Warnings = 2,
            [Description("Message + Info")] Information = 4,
            [Description("Debug")] Debug = 8
        }

        [Flags]
        private enum DynamicFlags
        {
            None = 0,
            [DynamicDescription] Value = 1
        }

        private static void EnumCaching()
        {
            DisplayMode value = DisplayMode.Detailed;
            int writes = 0;
            bool rejectWrite = false;
            using (Register("test.enum-cache", Setting("Mode", typeof(DisplayMode), DisplayMode.Detailed,
                () => value, input =>
                {
                    writes++;
                    if (rejectWrite) throw new IOException("enum save failed");
                    value = (DisplayMode)input;
                })))
            using (Register("test.dynamic-enum", Setting("Dynamic", typeof(DynamicMode), DynamicMode.Value, () => DynamicMode.Value, _ => { })))
            using (Register("test.flags-enum", Setting("Flags", typeof(FileAttributes), FileAttributes.Normal, () => FileAttributes.Normal, _ => { })))
            {
                object entry = Entry("test.enum-cache");
                object cache = NumberDrawerCall("GetExternalEnumCache", entry);
                Check((bool)Get(cache, "IsEligible"), "ordinary external enum uses cached choices");
                Check(ReferenceEquals(cache, NumberDrawerCall("GetExternalEnumCache", entry)), "repeated draws reuse enum metadata");
                IList values = (IList)Get(cache, "Values");
                Array contents = (Array)Get(cache, "Contents");
                Check(values.Count == 4 && (string)Get(contents.GetValue(0), "text") == "No output", "cached labels retain DescriptionAttribute text");
                object detailed = Call(cache, "GetCurrentContent");
                Check(ReferenceEquals(detailed, Call(cache, "GetCurrentContent")) &&
                    ReferenceEquals(detailed, contents.GetValue(3)), "unchanged getter uses cached content without rebuilding labels");
                value = DisplayMode.Problems;
                Check(ReferenceEquals(Call(cache, "GetCurrentContent"), contents.GetValue(1)), "cfg reload updates current dropdown label from existing choices");
                Action<int> select = (Action<int>)Get(cache, "SelectionChanged");
                select(2);
                Check(value == DisplayMode.Normal && writes == 1 &&
                    ReferenceEquals(Call(cache, "GetCurrentContent"), contents.GetValue(2)), "cached dropdown selection reaches typed owner setter");
                rejectWrite = true;
                select(0);
                Check(value == DisplayMode.Normal && writes == 2 &&
                    ReferenceEquals(Call(cache, "GetCurrentContent"), contents.GetValue(2)) &&
                    ((string)Get(entry, "ErrorText")).Contains("enum save failed"), "failed selection retains owner value and feedback");
                select(-1);
                select(values.Count);
                Check(writes == 2, "invalid dropdown indexes do not call owner");

                object dynamicCache = NumberDrawerCall("GetExternalEnumCache", Entry("test.dynamic-enum"));
                Check(!(bool)Get(dynamicCache, "IsEligible"), "derived DescriptionAttribute retains ordinary dynamic rendering");
                DynamicDescriptionAttribute.Current = "first label";
                object firstLabel = NumberDrawerCall("ObjectToGuiContent", DynamicMode.Value);
                DynamicDescriptionAttribute.Current = "updated label";
                object updatedLabel = NumberDrawerCall("ObjectToGuiContent", DynamicMode.Value);
                Check((string)Get(firstLabel, "text") == "first label" &&
                    (string)Get(updatedLabel, "text") == "updated label", "fallback renderer observes dynamic descriptions");
                object flagsCache = NumberDrawerCall("GetExternalEnumCache", Entry("test.flags-enum"));
                Check((bool)Get(flagsCache, "IsEligible") && (bool)Get(flagsCache, "IsFlags"), "ordinary external flags retain flags rendering with cached metadata");

                NumberDrawerCall("ClearComboboxCache");
                object rebuilt = NumberDrawerCall("GetExternalEnumCache", entry);
                Check(!ReferenceEquals(cache, rebuilt) && !ReferenceEquals(values, Get(rebuilt, "Values")), "UI cache cleanup releases entry metadata and rebuilds on demand");
            }
            NumberDrawerCall("ClearComboboxCache");
        }

        private static void GroupedFlags()
        {
            const LogGroups defaultGroups = LogGroups.Errors | LogGroups.Warnings;
            LogGroups value = defaultGroups;
            int writes = 0;
            bool rejectWrite = false;
            using (Register("test.grouped-flags", Setting("Logs", typeof(LogGroups), defaultGroups,
                () => value, input =>
                {
                    Check(input.GetType() == typeof(LogGroups), "flags setter receives its declared enum type");
                    writes++;
                    if (rejectWrite) throw new IOException("flags save failed");
                    value = (LogGroups)input;
                })))
            using (Register("test.dynamic-flags", Setting("Dynamic", typeof(DynamicFlags), DynamicFlags.Value, () => DynamicFlags.Value, _ => { })))
            {
                object entry = Entry("test.grouped-flags");
                object cache = NumberDrawerCall("GetExternalEnumCache", entry);
                long[] masks = (long[])Get(cache, "FlagMasks");
                Array contents = (Array)Get(cache, "FlagContents");
                Check((bool)Get(cache, "IsEligible") && (bool)Get(cache, "IsFlags"), "groups select cached flags rendering");
                Check(masks.SequenceEqual(new long[] { 1, 2, 4, 8 }) && contents.Length == 4, "only four nonzero group toggles, without None or synthetic All");
                Check(contents.Cast<object>().Select(content => (string)Get(content, "text"))
                    .SequenceEqual(new[] { "Fatal + Error", "Warning", "Message + Info", "Debug" }), "static group descriptions are the visible labels");
                Check(Convert.ToInt32(Get(entry, "DefaultValue")) == 3 &&
                    masks.Select(mask => (3 & mask) == mask).SequenceEqual(new[] { true, true, false, false }), "default checks only Errors and Warnings");

                for (int original = 0; original < 16; original++)
                    for (int index = 0; index < masks.Length; index++)
                        foreach (bool enabled in new[] { false, true })
                        {
                            value = (LogGroups)original;
                            int expected = enabled ? original | (1 << index) : original & ~(1 << index);
                            Call(cache, "SetFlag", index, enabled);
                            Check((int)value == expected, "each toggle preserves unrelated bits for all sixteen combinations");
                            Check(ReferenceEquals(masks, Get(cache, "FlagMasks")) && ReferenceEquals(contents, Get(cache, "FlagContents")), "flag edits reuse numeric metadata and labels");
                        }
                Check(writes == 128, "one typed setter call per toggle");
                Call(cache, "SetFlag", -1, true);
                Call(cache, "SetFlag", 4, true);
                Check(writes == 128, "invalid toggle indexes do not call owner");

                value = LogGroups.Debug; // Owner cfg reload after the cache was created.
                Call(cache, "SetFlag", 0, true);
                Check(value == (LogGroups.Debug | LogGroups.Errors), "toggle merges with latest getter after reload");
                rejectWrite = true;
                Call(cache, "SetFlag", 1, true);
                Check(value == (LogGroups.Debug | LogGroups.Errors) &&
                    ((string)Get(entry, "ErrorText")).Contains("flags save failed"), "rejected checkbox change does not optimistically change persisted bits");
                rejectWrite = false;
                Call(entry, "Set", Get(entry, "DefaultValue"));
                Check(value == defaultGroups && Get(entry, "ErrorText") == null, "Reset submits grouped default through owner setter");

                object window = CreateManagedEditor(entry);
                Call(window, "InitializeWindow");
                Check(ReferenceEquals(cache, Call(window, "GetExternalFlagsCache")) &&
                    ReferenceEquals(GetField(window, "_enumContent"), Get(cache, "Contents")), "row and Edit window share the same static descriptions");
                int beforeEdit = writes;
                Call(window, "SetExternalFlag", 3, true);
                Check(writes == beforeEdit && Convert.ToInt32(GetField(window, "valueToSet")) == 11 && value == defaultGroups, "Edit toggle only stages its typed value");
                rejectWrite = true;
                Call(window, "ApplySettingValue");
                Check(value == defaultGroups && Convert.ToInt32(GetField(window, "valueToSet")) == 11 &&
                    ((string)GetField(window, "errorOnSetting")).Contains("flags save failed"), "failed Edit Apply retains pending groups and error");
                rejectWrite = false;
                Call(window, "ApplySettingValue");
                Check((int)value == 11 && Convert.ToInt32(GetField(window, "valueToSet")) == 11, "successful Edit Apply reaches owner and refreshes editor");
                Call(window, "Dispose");
                Check(GetField(window, "_enumContent") == null && GetField(window, "listEnum") == null, "editor disposal releases cached label and value references");

                Check(!(bool)Get(NumberDrawerCall("GetExternalEnumCache", Entry("test.dynamic-flags")), "IsEligible"), "derived flag descriptions keep the previous renderer");
                NumberDrawerCall("ClearComboboxCache");
                object rebuilt = NumberDrawerCall("GetExternalEnumCache", entry);
                Check(!ReferenceEquals(cache, rebuilt) && !ReferenceEquals(contents, Get(rebuilt, "FlagContents")), "manager cache cleanup releases grouped metadata");
            }
            NumberDrawerCall("ClearComboboxCache");
        }

        private static void TypedTextReset()
        {
            string nullableText = string.Empty;
            bool rejectNull = false;
            int writes = 0;
            using (Register("test.nullable", Setting("Text", typeof(string), null,
                () => nullableText, input =>
                {
                    writes++;
                    if (rejectNull && input == null) throw new ArgumentException("null rejected");
                    nullableText = (string)input;
                })))
            {
                object entry = Entry("test.nullable");
                object window = CreateManagedEditor(entry);
                Call(window, "InitializeWindow");
                Check(!(bool)Call(window, "IsPendingValueEqualTo", new object[] { null }), "empty string differs from null default for Reset");
                Call(window, "ResetExternalValue");
                Check(writes == 0, "Reset only changes the editor buffer");
                Check(!(bool)Call(window, "IsPendingValueEqualTo", nullableText), "null reset enables Apply even when both texts are empty");
                Call(window, "SetExternalText", string.Empty);
                Call(window, "ApplySettingValue");
                Check(nullableText == null && writes == 1, "unchanged GUI text preserves typed null Reset");
                Check((bool)Call(window, "IsPendingValueEqualTo", new object[] { null }), "successful Apply clears pending edit");

                Call(window, "SetExternalText", "updated");
                Call(window, "SetExternalText", string.Empty);
                Check(!(bool)Call(window, "IsPendingValueEqualTo", new object[] { null }), "typing then deleting means empty string, not null");
                Call(window, "ApplySettingValue");
                Check(nullableText == string.Empty && writes == 2, "explicit empty string is applied once");

                rejectNull = true;
                Call(window, "ResetExternalValue");
                Call(window, "ApplySettingValue");
                Check(nullableText == string.Empty && writes == 3, "owner can reject typed Reset");
                Check(((string)GetField(window, "errorOnSetting")).Contains("null rejected"), "typed Reset rejection is shown");
                Check(!(bool)Call(window, "IsPendingValueEqualTo", nullableText), "rejected Reset preserves pending typed intent");
                rejectNull = false;
                Call(window, "ApplySettingValue");
                Check(nullableText == null && writes == 4, "typed Reset can be retried without losing null");

                Call(window, "ResetExternalValue");
                Call(window, "SetExternalText", "updated");
                Call(window, "ApplySettingValue");
                Check(nullableText == "updated" && writes == 5, "editing after Reset overrides typed default");
                Check((string)GetField(window, "_externalText") == "updated", "successful edit reloads owner value");
            }
        }

        private static void StringEditing()
        {
            Assembly bepinex = Assembly.Load("BepInEx");
            Type configFileType = bepinex.GetType("BepInEx.Configuration.ConfigFile", true);
            object config = Activator.CreateInstance(configFileType,
                Path.Combine(_temporaryRoot, "editor.cfg"), false, null);
            configFileType.GetProperty("SaveOnConfigSet").SetValue(config, false);
            Type definitionType = bepinex.GetType("BepInEx.Configuration.ConfigDefinition", true);
            object definition = Activator.CreateInstance(definitionType, "General", "Text");
            MethodInfo bind = configFileType.GetMethods().Single(method => method.Name == "Bind" &&
                method.IsGenericMethodDefinition && method.GetParameters().Length == 3 &&
                method.GetParameters()[0].ParameterType == definitionType).MakeGenericMethod(typeof(string));
            object configEntry = bind.Invoke(config, new[] { definition, "abc", null });
            object entry = Activator.CreateInstance(_assembly.GetType("ConfigurationManager.ConfigSettingEntry", true), configEntry, null);
            object window = CreateManagedEditor(entry);
            Call(window, "InitializeWindow");

            SetField(window, "valueToSet", "ABC");
            Check(!(bool)Call(window, "IsPendingValueEqualTo", Call(entry, "Get")), "case-only string edit enables Apply");
            Call(window, "ApplySettingValue");
            Check((string)Call(entry, "Get") == "ABC", "case-only string edit reaches the real ConfigEntry");
            Check((bool)Call(window, "IsPendingValueEqualTo", "ABC"), "successful Apply clears pending comparison");

            SetField(window, "valueToSet", "first, second,,last");
            Call(window, "ChangeStringView", 1);
            var rows = (List<string>)GetField(window, "separatedString");
            Check(rows.SequenceEqual(new[] { "first", " second", "", "last" }), "entering list uses latest text and preserves whitespace/empty items");
            rows[0] = "changed";
            rows.RemoveAt(2);
            Call(window, "UpdateValueFromStringList");
            Call(window, "ChangeStringView", 0);
            Check((string)GetField(window, "valueToSet") == "changed, second,last", "list changes survive return to text");
            SetField(window, "valueToSet", "replacement");
            Call(window, "ChangeStringView", 1);
            Check(rows.SequenceEqual(new[] { "replacement" }), "returning to list does not restore stale rows");
            Call(window, "UpdateValueFromStringList");
            Call(window, "ApplySettingValue");
            Check((string)Call(entry, "Get") == "replacement", "view changes retain the intended committed value");
        }

        private static object CreateManagedEditor(object entry)
        {
            // Bypass only the constructor's Unity window/config sizing. Exercise the
            // real InitializeWindow/ApplySettingValue methods with their managed lists.
            object window = FormatterServices.GetUninitializedObject(_assembly.GetType("ConfigurationManager.SettingEditWindow", true));
            SetField(window, "setting", entry);
            SetField(window, "separatedString", new List<string>());
            SetField(window, "separatedStringDefault", new List<string>());
            SetField(window, "vectorParts", new List<string>());
            SetField(window, "vectorFloats", new List<float>());
            SetField(window, "vectorDefault", new List<float>());
            SetField(window, "<SettingDrawHandlers>k__BackingField", new Dictionary<Type, Action>());
            return window;
        }

        private static void WorkerRegistration()
        {
            IDisposable registration = null;
            Exception failure = null;
            var worker = new Thread(() =>
            {
                try { registration = Register("test.worker", Setting("Value", typeof(bool), false, () => true, _ => { })); }
                catch (Exception ex) { failure = ex; }
            });
            worker.Start(); worker.Join();
            if (failure != null) throw failure;
            Check((bool)Call(Entry("test.worker"), "Get"), "worker registration visible");
            worker = new Thread(() => registration.Dispose());
            worker.Start(); worker.Join();
            Check(Entries().Length == 0, "worker disposal removes entry");
        }

        private static object Setting(string name, Type type, object defaultValue, Func<object> get, Action<object> set)
            => Activator.CreateInstance(_externalType, "General", name, "Test setting", type, defaultValue, get, set);
        private static IDisposable Register(string guid, params object[] settings)
        {
            Array array = Array.CreateInstance(_externalType, settings.Length);
            for (int i = 0; i < settings.Length; i++) array.SetValue(settings[i], i);
            return (IDisposable)_register.Invoke(null, new object[] { guid, "External tests", "1.0.0", array });
        }
        private static object[] Entries() => ((IEnumerable)_registryType.GetMethod("Snapshot", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null)).Cast<object>().ToArray();
        private static object Entry(string guid) => Entries().Single(entry => Guid(entry) == guid);
        private static string Guid(object entry) => (string)Get(Get(entry, "PluginInfo"), "GUID");
        private static object Get(object target, string name) => target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(target);
        private static object GetStatic(Type type, string name) => type.GetProperty(name, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(target, args);
        private static object GetField(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        private static void SetField(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private static Exception Unwrap(Exception ex) => ex is TargetInvocationException call && call.InnerException != null ? Unwrap(call.InnerException) : ex;
        private static void ExpectThrow(Action action) { try { action(); } catch { return; } throw new Exception("Expected validation failure."); }
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Case(string name, Action action, ICollection<string> failures)
        {
            try { action(); _passed++; Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failures.Add(name + ": " + Unwrap(ex)); }
        }
    }
}
