using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ConfigurationManager.Tests
{
    internal static class UiEditingTests
    {
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static MethodInfo _labelShortcut;
        private static MethodInfo _saveWindowConfig;
        private static Type _shortcutType;
        private static Type _keyCodeType;
        private static Type _configType;
        private static Type _definitionType;
        private static MethodInfo _bindString;
        private static string _suiteRoot;
        private static Assembly _assembly;
        private static Type _drawerType;
        private static Type _rangeEditorType;
        private static MethodInfo _parseRange;
        private static Type _captureType;

        internal static void Run(Assembly assembly, ICollection<string> failures, string temporaryRoot)
        {
            _assembly = assembly;
            Type manager = assembly.GetType("ConfigurationManager.ConfigurationManager", true);
            _labelShortcut = manager.GetMethod("FormatKeyboardShortcutLabel", Static);
            _saveWindowConfig = manager.GetMethod("SaveWindowConfigChanges", Static);
            Check(_labelShortcut != null && _saveWindowConfig != null, "UI editing helper contracts are available");
            _shortcutType = _labelShortcut.GetParameters()[0].ParameterType;
            _keyCodeType = _shortcutType.GetProperty("MainKey").PropertyType;
            _configType = _saveWindowConfig.GetParameters()[0].ParameterType;
            _definitionType = _configType.Assembly.GetType("BepInEx.Configuration.ConfigDefinition", true);
            _bindString = _configType.GetMethods().Single(method => method.Name == "Bind" &&
                method.IsGenericMethodDefinition && method.GetParameters().Length == 3 &&
                method.GetParameters()[0].ParameterType == _definitionType).MakeGenericMethod(typeof(string));
            _suiteRoot = Path.Combine(Path.GetFullPath(temporaryRoot), "UiEditing-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_suiteRoot);
            _drawerType = assembly.GetType("ConfigurationManager.SettingFieldDrawer", true);
            _rangeEditorType = _drawerType.GetNestedType("RangeTextEditor", BindingFlags.NonPublic);
            _parseRange = _drawerType.GetMethod("TryParseRangeText", Static);
            _captureType = _drawerType.GetNestedType("ShortcutCaptureState", BindingFlags.NonPublic);

            int passed = 0;
            Case("shortcut labels preserve trigger, modifiers and serialization without tooltips", ShortcutLabels, failures, ref passed);
            Case("empty and single-key labels preserve their original values", EmptyAndSingleShortcuts, failures, ref passed);
            Case("last pressed trigger survives either or simultaneous release order", CaptureReleaseOrders, failures, ref passed);
            Case("press order preserves reverse and modifier-as-trigger shortcuts", CapturePressOrder, failures, ref passed);
            Case("three-key capture retains its press-time snapshot and ignores unrelated releases", CaptureMultipleKeys, failures, ref passed);
            Case("capture ignores arming-frame input and requires a fresh key press", CaptureArmingAndPreheldKeys, failures, ref passed);
            Case("capture samples a frame once and accepts a single-key fast tap", CaptureFrameBoundaries, failures, ref passed);
            Case("capture cancellation and rearming discard the prior candidate", CaptureCancellation, failures, ref passed);
            Case("capture excludes activation mouse clicks and None", CaptureExcludedKeys, failures, ref passed);
            Case("simultaneous modifier and ordinary groups resolve on a later sole press", CaptureSimultaneousGroups, failures, ref passed);
            Case("unresolved releases retry without guessing a trigger", CaptureUnresolvedRelease, failures, ref passed);
            Case("simultaneous groups invalidate an earlier trigger and allow further groups", CapturePendingReplacesCandidate, failures, ref passed);
            Case("pending capture cancellation and rearming discard the group and preview", CapturePendingCancellation, failures, ref passed);
            Case("a sole fast tap resolves a pending group", CapturePendingFastTap, failures, ref passed);
            Case("mixed press and other-key release retries without saving stale input", CaptureMixedTransitions, failures, ref passed);
            Case("separate taps do not accumulate into a chord or commit twice", CaptureSeparateTaps, failures, ref passed);
            Case("main shortcut capture writes once through the real setting despite callback reentry", CaptureMainSubmission, failures, ref passed);
            Case("shortcut capture rechecks read-only and PoV locks before writing", CaptureWriteGuards, failures, ref passed);
            Case("dialog shortcut capture stages changes until explicit Apply", CaptureDialogSubmission, failures, ref passed);
            Case("cancel, cache clear and dialog close discard active shortcut captures", CaptureControllerCancellation, failures, ref passed);
            Case("starting a built-in key recorder replaces the other window's key recorder", CaptureBuiltInExclusivity, failures, ref passed);
            Case("capture owner changes and built-in cancellation preserve custom recording state", CaptureCustomOwnership, failures, ref passed);
            foreach (bool automaticSave in new[] { true, false })
            {
                Case("window config batch persists completed values and restores SaveOnConfigSet=" + automaticSave,
                    () => SaveCompletedBatch(automaticSave), failures, ref passed);
                Case("failed window config batch preserves disk and restores SaveOnConfigSet=" + automaticSave,
                    () => SaveFailedBatch(automaticSave), failures, ref passed);
            }
            foreach (string culture in new[] { "en-US", "de-DE", "ko-KR" })
                Case("range parsing and partial input under " + culture,
                    () => RangeParsing(culture), failures, ref passed);
            Case("range buffer preserves unmodified values and submits changes once", RangeBufferSubmission, failures, ref passed);
            Case("range dialog stages values until Apply and discards hidden input", RangeDialogSubmission, failures, ref passed);
            Case("range submission uses real ConfigEntry guards and avoids duplicate callbacks", RangeWriteGuards, failures, ref passed);
            Case("range close detaches pending values before reentrant setting callbacks", RangeReentrantSubmission, failures, ref passed);
            Console.WriteLine("UiEditing: " + passed + " cases passed");
        }

        private static void RangeParsing(string culture)
        {
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                AssertRange("20", 10, 60, typeof(int), 20);
                AssertRange("2", 10, 60, typeof(int), 10);
                AssertRange("80", 10, 60, typeof(int), 60);
                AssertRange("-.5", -10, 10, typeof(float), -0.5f);
                AssertRange("1,25", -10, 10, typeof(float), 1.25f);
                AssertRange("1.25", -10, 10, typeof(float), 1.25f);
                AssertRange("1e2", 0, 200, typeof(int), 100);
                AssertRange("1.23456", 0, 10, typeof(float), 1.235f);
                foreach (string invalid in new[] { null, "", " ", "-", ".", "-.", "abc", "1x2", "NaN", "Infinity", "-Infinity", "1e100" })
                    AssertRangeRejected(invalid, -100, 100, typeof(float));
                AssertRangeRejected("300", 0, 1000, typeof(byte));
                AssertRangeRejected("3000000000", 0, 4000000000f, typeof(int));
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        private sealed class Capture
        {
            private readonly object state = Activator.CreateInstance(_captureType, true);
            internal Capture(int frame = 0) => Begin(frame);
            internal bool IsActive => (bool)_captureType.GetProperty("IsActive", Instance).GetValue(state);
            internal bool NeedsRetry => (bool)_captureType.GetProperty("NeedsRetry", Instance).GetValue(state);
            internal string DisplayText => (string)_captureType.GetProperty("DisplayText", Instance).GetValue(state);
            internal void Begin(int frame) => _captureType.GetMethod("Begin", Instance).Invoke(state, new object[] { frame });
            internal void Cancel() => _captureType.GetMethod("Cancel", Instance).Invoke(state, null);
            internal bool Frame(int frame, string pressed, string held, string released, out object shortcut)
            {
                object[] arguments = { frame, Keys(pressed), Keys(held), Keys(released), null };
                bool committed = (bool)_captureType.GetMethod("ProcessFrame", Instance).Invoke(state, arguments);
                shortcut = arguments[4];
                return committed;
            }
        }

        private static Array Keys(string keys)
        {
            string[] names = keys.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            Array result = Array.CreateInstance(_keyCodeType, names.Length);
            for (int index = 0; index < names.Length; index++)
                result.SetValue(Enum.Parse(_keyCodeType, names[index]), index);
            return result;
        }

        private static void AssertCaptured(object shortcut, string main, params string[] modifiers)
        {
            Check(shortcut != null && _shortcutType.GetProperty("MainKey").GetValue(shortcut).ToString() == main,
                "the final press, rather than the first release, selects the trigger");
            string[] actual = Modifiers(shortcut);
            Check(actual.Length == modifiers.Length && actual.Distinct().Count() == actual.Length &&
                new HashSet<string>(actual).SetEquals(modifiers), "modifiers preserve the press-time chord exactly");
            string serialized = shortcut.ToString();
            string expectedSaved = main + (actual.Length == 0 ? "" : " + " + string.Join(" + ", actual));
            string label = (string)_labelShortcut.Invoke(null, new[] { shortcut });
            Check(serialized == expectedSaved && (actual.Length == 0 ? label == main : label.EndsWith(" + " + main, StringComparison.Ordinal)),
                "new captures keep BepInEx main-first serialization and display the actual trigger last");
        }

        private static void CaptureReleaseOrders()
        {
            foreach (string released in new[] { "A", "B", "A B" })
            {
                var capture = new Capture();
                Check(!capture.Frame(1, "A", "A", "", out _), "the first press only stages a candidate");
                Check(!capture.Frame(2, "B", "A B", "", out _), "the later press replaces the trigger candidate");
                string held = released == "A" ? "B" : released == "B" ? "A" : "";
                Check(capture.Frame(3, "", held, released, out object shortcut), "the first chord-key release commits");
                AssertCaptured(shortcut, "B", "A");
                Check(!capture.IsActive && !capture.Frame(4, "", "", "A B", out _),
                    "remaining releases cannot commit the same shortcut again");
            }
        }

        private static void CapturePressOrder()
        {
            foreach (var pair in new[] { new[] { "B", "A" }, new[] { "LeftAlt", "X" },
                new[] { "X", "LeftAlt" }, new[] { "Q", "E" },
                new[] { "LeftShift", "LeftAlt" }, new[] { "LeftAlt", "LeftShift" } })
            {
                var capture = new Capture();
                capture.Frame(1, pair[0], pair[0], "", out _);
                capture.Frame(2, pair[1], pair[0] + " " + pair[1], "", out _);
                Check(capture.Frame(3, "", pair[1], pair[0], out object shortcut), "releasing the held key completes the remembered chord");
                AssertCaptured(shortcut, pair[1], pair[0]);
            }
        }

        private static void CaptureMultipleKeys()
        {
            var capture = new Capture();
            capture.Frame(1, "A", "A", "", out _);
            capture.Frame(2, "B", "A B", "", out _);
            capture.Frame(3, "C", "A B C", "", out _);
            Check(!capture.Frame(4, "", "A B C", "D", out _), "an unrelated release does not finish the chord");
            Check(capture.Frame(5, "", "A C", "B", out object shortcut), "a modifier release finishes the stored three-key chord");
            AssertCaptured(shortcut, "C", "A", "B");
        }

        private static void CaptureArmingAndPreheldKeys()
        {
            var capture = new Capture(10);
            Check(!capture.Frame(10, "A", "A", "", out _), "the GUI activation frame is not captured");
            Check(!capture.Frame(11, "", "", "A", out _), "a release without a newly observed press cannot create a binding");
            Check(!capture.Frame(12, "", "LeftAlt", "", out _), "an already held key alone is not a new trigger");
            capture.Frame(13, "X", "LeftAlt X", "", out _);
            Check(capture.Frame(14, "", "X", "LeftAlt", out object shortcut), "a fresh press can use an already held modifier");
            AssertCaptured(shortcut, "X", "LeftAlt");
        }

        private static void CaptureFrameBoundaries()
        {
            var capture = new Capture();
            capture.Frame(1, "A", "A", "", out _);
            Check(!capture.Frame(1, "B", "A B", "", out _), "another IMGUI pass cannot reorder the same input frame");
            Check(capture.Frame(2, "", "", "A", out object shortcut), "the first frame sample retains its candidate");
            AssertCaptured(shortcut, "A");
            capture.Begin(3);
            Check(capture.Frame(4, "X", "", "X", out shortcut), "a single key pressed and released in one frame is unambiguous");
            AssertCaptured(shortcut, "X");
        }

        private static void CaptureCancellation()
        {
            var capture = new Capture();
            capture.Frame(1, "A", "A", "", out _);
            capture.Frame(2, "B", "A B", "", out _);
            capture.Cancel();
            Check(!capture.IsActive && !capture.Frame(3, "", "", "A B", out _), "cancelled input never commits later");
            capture.Begin(4);
            Check(!capture.Frame(5, "", "", "A B", out _), "rearming does not resurrect the previous candidate");
            capture.Frame(6, "C", "C", "", out _);
            Check(capture.Frame(7, "", "", "C", out object shortcut), "a rearmed capture accepts only its new chord");
            AssertCaptured(shortcut, "C");
        }

        private static void CaptureExcludedKeys()
        {
            var capture = new Capture();
            Check(!capture.Frame(1, "Mouse0 None", "Mouse0 None", "", out _) && !capture.NeedsRetry,
                "activation mouse clicks and None neither become candidates nor create ambiguity");
            capture.Frame(2, "A", "A Mouse0 None", "", out _);
            Check(!capture.Frame(3, "", "A", "Mouse0 None", out _), "excluded releases do not commit a candidate");
            Check(capture.Frame(4, "", "", "A", out object shortcut), "the supported trigger still commits normally");
            AssertCaptured(shortcut, "A");
            capture.Begin(5);
            Check(!capture.Frame(6, "A A Mouse0 None", "A Mouse0 None", "", out _) && !capture.NeedsRetry && capture.DisplayText == "A",
                "duplicate reports of one supported key do not create an unresolved group");
            Check(capture.Frame(7, "", "", "A", out shortcut), "a duplicated report still captures a single key");
            AssertCaptured(shortcut, "A");
        }

        private static void CaptureSimultaneousGroups()
        {
            // Ctrl+RightAlt models an AltGr report, without claiming a real keyboard-layout test.
            foreach (var example in new[]
            {
                new[] { "LeftControl LeftShift", "LeftControl + LeftShift", "X" },
                new[] { "LeftShift LeftControl", "LeftControl + LeftShift", "X" },
                new[] { "LeftAlt LeftControl", "LeftControl + LeftAlt", "X" },
                new[] { "RightAlt LeftControl", "LeftControl + RightAlt", "X" },
                new[] { "Y X", "X + Y", "C" },
                new[] { "X Y", "X + Y", "C" }
            })
            {
                string group = example[0];
                string main = example[2];
                var capture = new Capture();
                Check(!capture.Frame(1, group + " None Mouse0", group + " None Mouse0", "", out object pending) &&
                    !capture.NeedsRetry && capture.IsActive,
                    "simultaneous supported keys remain pending instead of forcing a retry: " + group);
                Check(_shortcutType.GetProperty("MainKey").GetValue(pending).ToString() == "None",
                    "pending input never emits a shortcut with an invented main key");
                string preview = capture.DisplayText;
                Check(preview == example[1] + " + ...", "pending preview shows detected keys and an unresolved trigger");
                Check(!capture.Frame(2, "", group, "", out _) && ReferenceEquals(preview, capture.DisplayText),
                    "idle frames reuse the pending preview without rebuilding it");
                Check(!capture.Frame(3, "", group, "Z", out _) && !capture.NeedsRetry &&
                    ReferenceEquals(preview, capture.DisplayText),
                    "unrelated release keeps the pending group and cached preview");
                Check(!capture.Frame(4, main, group + " " + main, "", out _) && !capture.NeedsRetry &&
                    capture.DisplayText == example[1] + " + " + main,
                    "a later sole press resolves the trigger and immediately replaces the pending preview");
                Check(capture.Frame(5, "", group, main, out object shortcut),
                    "a resolved chord commits when a member is released");
                AssertCaptured(shortcut, main, group.Split(' '));
                Check(!capture.IsActive && !capture.Frame(6, "", "", group, out _),
                    "the remaining releases cannot commit a resolved group twice");
            }
        }

        private static void CaptureUnresolvedRelease()
        {
            foreach (string group in new[] { "LeftControl LeftShift", "Y X", "LeftControl RightAlt", "LeftControl LeftShift X" })
            {
                foreach (bool releaseAll in new[] { false, true })
                {
                    string[] keys = group.Split(' ');
                    string remaining = releaseAll ? "" : string.Join(" ", keys.Skip(1));
                    var capture = new Capture();
                    capture.Frame(1, group, group, "", out _);
                    Check(!capture.Frame(2, "", remaining, releaseAll ? group : keys[0], out _) &&
                        capture.NeedsRetry && capture.IsActive,
                        "releasing an unresolved chord cannot choose its main key by type or enumeration order");
                    Check(capture.DisplayText == "Release all keys, then press them in sequence",
                        "unresolved release replaces the pending preview with the retry instruction");
                    if (!releaseAll)
                    {
                        Check(!capture.Frame(3, "Z", remaining + " Z", "", out _),
                            "after an unresolved release, new presses wait for all old keys to be released");
                        Check(!capture.Frame(4, "", "", remaining + " Z", out _), "draining a failed chord does not save it");
                    }
                    capture.Frame(5, "D", "D", "", out _);
                    Check(!capture.NeedsRetry && capture.DisplayText == "D", "a fresh attempt clears the retry state");
                    Check(capture.Frame(6, "", "", "D", out object shortcut), "retry works without reopening the recorder");
                    AssertCaptured(shortcut, "D");
                }
            }
        }

        private static void CapturePendingReplacesCandidate()
        {
            var capture = new Capture();
            capture.Frame(1, "A", "A", "", out _);
            Check(!capture.Frame(2, "B C", "A B C", "", out _) && !capture.NeedsRetry,
                "multiple later presses replace the old trigger with an unresolved group");
            Check(!capture.Frame(3, "", "B C", "A", out _) && capture.NeedsRetry,
                "releasing the earlier trigger cannot save its stale candidate");

            capture.Begin(4);
            capture.Frame(5, "A", "A", "", out _);
            capture.Frame(6, "B C", "A B C", "", out _);
            Check(!capture.Frame(7, "D E", "A B C D E", "", out _) && !capture.NeedsRetry &&
                capture.DisplayText == "A + B + C + D + E + ...",
                "further simultaneous presses extend the pending chord without inventing a trigger");
            capture.Frame(8, "X", "A B C D E X", "", out _);
            Check(capture.Frame(9, "", "A B C E X", "D", out object shortcut),
                "any member release commits after a later sole press resolves the chord");
            AssertCaptured(shortcut, "X", "A", "B", "C", "D", "E");
        }

        private static void CapturePendingCancellation()
        {
            foreach (bool cancelFirst in new[] { false, true })
            {
                var capture = new Capture();
                string initial = capture.DisplayText;
                capture.Frame(1, "LeftControl LeftShift", "LeftControl LeftShift", "", out _);
                if (cancelFirst)
                {
                    capture.Cancel();
                    Check(!capture.IsActive && capture.DisplayText == initial, "cancel clears the pending preview");
                }
                capture.Begin(2);
                Check(capture.DisplayText == initial && !capture.NeedsRetry,
                    "rearming clears pending state even without a separate cancel call");
                Check(!capture.Frame(3, "", "", "LeftControl LeftShift", out _) && !capture.NeedsRetry,
                    "old group releases cannot save or invalidate a rearmed capture");
                capture.Frame(4, "C", "C", "", out _);
                Check(capture.Frame(5, "", "", "C", out object shortcut), "a new capture is independent of the pending group");
                AssertCaptured(shortcut, "C");
                Check(capture.DisplayText == initial, "completion clears the resolved preview");
            }
        }

        private static void CapturePendingFastTap()
        {
            var capture = new Capture();
            capture.Frame(1, "LeftShift LeftControl", "LeftShift LeftControl", "", out _);
            Check(capture.Frame(2, "X", "LeftShift LeftControl", "X", out object shortcut),
                "a sole press and release within one frame resolves and commits the pending chord");
            AssertCaptured(shortcut, "X", "LeftShift", "LeftControl");
        }

        private static void CaptureMixedTransitions()
        {
            var capture = new Capture();
            capture.Frame(1, "A", "A", "", out _);
            Check(!capture.Frame(2, "B", "B", "A", out _) && capture.NeedsRetry,
                "a new press and another key's release in one frame have unknown relative order");
            Check(!capture.Frame(3, "", "", "B", out _), "mixed-transition ambiguity cannot save the previous or new candidate");
            capture.Frame(4, "C", "C", "", out _);
            Check(capture.Frame(5, "", "", "C", out object shortcut), "the next clean attempt succeeds");
            AssertCaptured(shortcut, "C");

            capture.Begin(6);
            capture.Frame(7, "A B", "A B", "", out _);
            Check(!capture.Frame(8, "C", "B C", "A", out _) && capture.NeedsRetry,
                "a pending group cannot resolve when another key is released in the same frame");
            foreach (string pressed in new[] { "A B", "B A" })
            {
                capture.Begin(9);
                Check(!capture.Frame(10, pressed, "A", "B", out _) && capture.NeedsRetry,
                    "multiple fresh keys with a release in the same frame retry regardless of enumeration order");
            }
        }

        private static void CaptureSeparateTaps()
        {
            var capture = new Capture();
            capture.Frame(1, "A", "A", "", out _);
            Check(capture.Frame(2, "", "", "A", out object shortcut), "a separate tap records a single key");
            AssertCaptured(shortcut, "A");
            Check(!capture.Frame(3, "B", "B", "", out _) && !capture.Frame(4, "", "", "B", out _),
                "input after a completed capture cannot extend the recorded chord");
            capture.Begin(5);
            capture.Frame(6, "B", "B", "", out _);
            Check(capture.Frame(7, "", "", "B", out shortcut), "the next capture starts independently");
            AssertCaptured(shortcut, "B");
        }

        private static object NewShortcut(string main, params string[] modifiers)
            => Activator.CreateInstance(_shortcutType, Enum.Parse(_keyCodeType, main), Keys(string.Join(" ", modifiers)));

        private static object NewShortcutSetting(string key, out object configEntry)
        {
            object config = NewConfig(Path.Combine(_suiteRoot, key + ".cfg"));
            MethodInfo bind = _bindString.GetGenericMethodDefinition().MakeGenericMethod(_shortcutType);
            configEntry = bind.Invoke(config, new[] { Activator.CreateInstance(_definitionType, "Shortcut", key), NewShortcut("F2"), null });
            return Activator.CreateInstance(_assembly.GetType("ConfigurationManager.ConfigSettingEntry", true), configEntry, null);
        }

        private static object ShortcutValue(object configEntry) => configEntry.GetType().GetProperty("Value").GetValue(configEntry);
        private static object DrawerCall(string method, params object[] arguments) => _drawerType.GetMethod(method, Static).Invoke(null, arguments);
        private static bool MainCaptureActive => (bool)_drawerType.GetProperty("IsRecordingKeyboardShortcut", Static).GetValue(null);

        private static void CaptureMainSubmission()
        {
            object setting = NewShortcutSetting("main-capture", out object entry);
            object result = NewShortcut("X", "LeftAlt");
            int writes = 0;
            EventHandler changed = (_, __) => { writes++; DrawerCall("ClearCache"); };
            entry.GetType().GetEvent("SettingChanged").AddEventHandler(entry, changed);
            try
            {
                DrawerCall("BeginKeyboardShortcutCapture", setting, 1);
                Check(MainCaptureActive, "the main list arms the default shortcut recorder");
                DrawerCall("CompleteKeyboardShortcutCapture", result);
                Check(!MainCaptureActive && writes == 1 && ShortcutValue(entry).ToString() == "X + LeftAlt",
                    "the main list commits once and detaches before a setting callback clears UI caches");
                DrawerCall("CompleteKeyboardShortcutCapture", NewShortcut("B", "A"));
                Check(writes == 1 && ShortcutValue(entry).ToString() == "X + LeftAlt",
                    "a duplicate completion cannot write after the capture owner has been released");
            }
            finally
            {
                entry.GetType().GetEvent("SettingChanged").RemoveEventHandler(entry, changed);
                DrawerCall("CancelKeyboardShortcutCapture");
            }
        }

        private static void CaptureWriteGuards()
        {
            object setting = NewShortcutSetting("guarded-capture", out object entry);
            object result = NewShortcut("X", "LeftAlt");
            PropertyInfo readOnly = setting.GetType().GetProperty("ReadOnly", Instance);
            DrawerCall("BeginKeyboardShortcutCapture", setting, 1);
            readOnly.SetValue(setting, true);
            DrawerCall("CompleteKeyboardShortcutCapture", result);
            Check(!MainCaptureActive && ShortcutValue(entry).ToString() == "F2",
                "read-only becoming active mid-capture prevents the write and cancels the capture");
            DrawerCall("BeginKeyboardShortcutCapture", setting, 2);
            Check(!MainCaptureActive, "a read-only setting cannot arm a new recorder");
            readOnly.SetValue(setting, null);

            Type bridge = _assembly.GetType("ConfigurationManager.PathOfValheimanCompatibility", true);
            FieldInfo faulted = bridge.GetField("_faulted", Static);
            object priorFaulted = faulted.GetValue(null);
            object metadata = Activator.CreateInstance(_configType.Assembly.GetType("BepInEx.BepInPlugin", true),
                "treextr.pathofvalheiman", "Capture test", "1.0.0");
            setting.GetType().GetProperty("PluginInfo", Instance).SetValue(setting, metadata);
            try
            {
                faulted.SetValue(null, false);
                DrawerCall("BeginKeyboardShortcutCapture", setting, 3);
                Check(MainCaptureActive, "the unlocked setting can begin a recorder");
                faulted.SetValue(null, true);
                DrawerCall("CompleteKeyboardShortcutCapture", result);
                Check(!MainCaptureActive && ShortcutValue(entry).ToString() == "F2",
                    "PoV locking after the initial press prevents a pending shortcut from being stored");
            }
            finally
            {
                faulted.SetValue(null, priorFaulted);
                DrawerCall("CancelKeyboardShortcutCapture");
            }
        }

        private static object NewShortcutDialog(object setting)
        {
            object window = ExternalSettingsTests.CreateManagedEditor(setting);
            window.GetType().GetMethod("InitializeWindow", Instance).Invoke(window, null);
            window.GetType().GetProperty("IsOpen", Instance).SetValue(window, true);
            return window;
        }

        private static object DialogCall(object window, string method, params object[] arguments)
            => window.GetType().GetMethod(method, Instance).Invoke(window, arguments);
        private static object DialogDraft(object window) => window.GetType().GetField("valueToSet", Instance).GetValue(window);
        private static bool DialogCaptureActive(object window)
            => (bool)window.GetType().GetProperty("IsRecordingKeyboardShortcut", Instance).GetValue(window);

        private static void CaptureDialogSubmission()
        {
            object setting = NewShortcutSetting("dialog-capture", out object entry);
            object window = NewShortcutDialog(setting);
            int writes = 0;
            EventHandler changed = (_, __) => writes++;
            entry.GetType().GetEvent("SettingChanged").AddEventHandler(entry, changed);
            try
            {
                DialogCall(window, "BeginKeyboardShortcutCapture", 1);
                Check(DialogCaptureActive(window), "the open dialog arms its own recorder");
                DialogCall(window, "CompleteKeyboardShortcutCapture", NewShortcut("X", "LeftAlt"));
                Check(!DialogCaptureActive(window) && writes == 0 && ShortcutValue(entry).ToString() == "F2" &&
                    DialogDraft(window).ToString() == "X + LeftAlt", "finishing a dialog capture only stages its result");
                DialogCall(window, "CompleteKeyboardShortcutCapture", NewShortcut("B", "A"));
                Check(DialogDraft(window).ToString() == "X + LeftAlt", "a duplicate completion cannot overwrite the draft");
                DialogCall(window, "ApplySettingValue");
                Check(writes == 1 && ShortcutValue(entry).ToString() == "X + LeftAlt" &&
                    string.IsNullOrEmpty((string)window.GetType().GetField("errorOnSetting", Instance).GetValue(window)),
                    "explicit Apply is the only dialog action that writes the captured shortcut");
            }
            finally
            {
                entry.GetType().GetEvent("SettingChanged").RemoveEventHandler(entry, changed);
                DialogCall(window, "Dispose");
            }
        }

        private static void CaptureControllerCancellation()
        {
            object setting = NewShortcutSetting("cancelled-capture", out object entry);
            object result = NewShortcut("X", "LeftAlt");
            foreach (string cancel in new[] { "CancelKeyboardShortcutCapture", "ClearCache" })
            {
                DrawerCall("BeginKeyboardShortcutCapture", setting, 1);
                DrawerCall(cancel);
                DrawerCall("CompleteKeyboardShortcutCapture", result);
                Check(!MainCaptureActive && ShortcutValue(entry).ToString() == "F2",
                    cancel + " discards the main recorder without changing the saved binding");
            }
            object window = NewShortcutDialog(setting);
            try
            {
                DialogCall(window, "BeginKeyboardShortcutCapture", 1);
                DialogCall(window, "ClearCache");
                DialogCall(window, "CompleteKeyboardShortcutCapture", result);
                Check(!DialogCaptureActive(window) && DialogDraft(window).ToString() == "F2",
                    "hiding or clearing the dialog discards its pending recorder");
                DialogCall(window, "BeginKeyboardShortcutCapture", 2);
                window.GetType().GetProperty("IsOpen", Instance).SetValue(window, false);
                DialogCall(window, "CompleteKeyboardShortcutCapture", result);
                Check(!DialogCaptureActive(window) && ShortcutValue(entry).ToString() == "F2" && DialogDraft(window).ToString() == "F2",
                    "closing the dialog prevents any later completion from staging or writing a shortcut");
            }
            finally { DialogCall(window, "Dispose"); }
        }

        private static void WithCaptureCoordinator(object window, Action<object> action)
        {
            Type managerType = _assembly.GetType("ConfigurationManager.ConfigurationManager", true);
            FieldInfo managerInstance = managerType.GetField("instance", Static);
            FieldInfo drawerInstance = _drawerType.GetField("_instance", Static);
            FieldInfo mainOwner = _drawerType.GetField("_currentKeyboardShortcutToSet", Static);
            FieldInfo dialogOwner = window.GetType().GetField("_currentKeyboardShortcutToSet", Static);
            object previousManager = managerInstance.GetValue(null);
            object previousDrawer = drawerInstance.GetValue(null);
            object previousMainOwner = mainOwner.GetValue(null);
            object previousDialogOwner = dialogOwner.GetValue(null);
            object manager = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(managerType);
            managerType.GetField("_configSettingWindow", Instance).SetValue(manager, window);
            try
            {
                managerInstance.SetValue(null, manager);
                drawerInstance.SetValue(null, manager);
                action(manager);
            }
            finally
            {
                DrawerCall("CancelBuiltInKeyRecording");
                DialogCall(window, "Dispose");
                managerInstance.SetValue(null, previousManager);
                drawerInstance.SetValue(null, previousDrawer);
                mainOwner.SetValue(null, previousMainOwner);
                dialogOwner.SetValue(null, previousDialogOwner);
            }
        }

        private static void CaptureBuiltInExclusivity()
        {
            object shortcutSetting = NewShortcutSetting("exclusive-shortcut", out object shortcutEntry);
            object config = NewConfig(Path.Combine(_suiteRoot, "exclusive-keycode.cfg"));
            object keyEntry = _bindString.GetGenericMethodDefinition().MakeGenericMethod(_keyCodeType).Invoke(config,
                new[] { Activator.CreateInstance(_definitionType, "Shortcut", "KeyCode"), Enum.Parse(_keyCodeType, "F2"), null });
            object keySetting = Activator.CreateInstance(_assembly.GetType("ConfigurationManager.ConfigSettingEntry", true), keyEntry, null);
            object window = NewShortcutDialog(shortcutSetting);
            FieldInfo mainOwner = _drawerType.GetField("_currentKeyboardShortcutToSet", Static);
            FieldInfo dialogOwner = window.GetType().GetField("_currentKeyboardShortcutToSet", Static);
            FieldInfo dialogSetting = window.GetType().GetField("setting", Instance);
            WithCaptureCoordinator(window, _ =>
            {
                DrawerCall("BeginKeyCodeCapture", keySetting);
                Check(ReferenceEquals(mainOwner.GetValue(null), keySetting), "the main legacy KeyCode recorder starts");
                DialogCall(window, "BeginKeyboardShortcutCapture", 1);
                Check(mainOwner.GetValue(null) == null && DialogCaptureActive(window),
                    "starting a dialog shortcut cancels the main KeyCode recorder");
                DrawerCall("BeginKeyCodeCapture", keySetting);
                Check(!DialogCaptureActive(window) && dialogOwner.GetValue(null) == null &&
                    ReferenceEquals(mainOwner.GetValue(null), keySetting), "starting a main KeyCode cancels the dialog shortcut recorder");
                DialogCall(window, "CompleteKeyboardShortcutCapture", NewShortcut("X"));
                Check(DialogDraft(window).ToString() == "F2", "a replaced dialog recorder cannot alter its draft later");

                dialogSetting.SetValue(window, keySetting);
                DrawerCall("BeginKeyboardShortcutCapture", shortcutSetting, 2);
                DialogCall(window, "BeginKeyCodeCapture");
                Check(!MainCaptureActive && mainOwner.GetValue(null) == null && ReferenceEquals(dialogOwner.GetValue(null), keySetting),
                    "starting a dialog KeyCode cancels the main shortcut recorder");
                DrawerCall("CompleteKeyboardShortcutCapture", NewShortcut("X"));
                DrawerCall("BeginKeyboardShortcutCapture", shortcutSetting, 3);
                Check(MainCaptureActive && dialogOwner.GetValue(null) == null,
                    "starting a main shortcut cancels the dialog KeyCode recorder");
                Check(ShortcutValue(shortcutEntry).ToString() == "F2" && ShortcutValue(keyEntry).ToString() == "F2",
                    "changing recorder ownership never writes either saved binding");
            });
        }

        private static void IgnoreCustomCapture<T>(T entry, ref bool accepting) { }

        private static void CaptureCustomOwnership()
        {
            object setting = NewShortcutSetting("owner-shortcut", out object entry);
            object custom = NewShortcutSetting("owner-custom", out _);
            PropertyInfo customDrawer = _assembly.GetType("ConfigurationManager.SettingEntryBase", true).GetProperty("CustomHotkeyDrawer", Instance);
            Type entryBase = _configType.Assembly.GetType("BepInEx.Configuration.ConfigEntryBase", true);
            MethodInfo callback = typeof(UiEditingTests).GetMethod("IgnoreCustomCapture", Static).MakeGenericMethod(entryBase);
            customDrawer.GetSetMethod(true).Invoke(custom, new object[] { Delegate.CreateDelegate(customDrawer.PropertyType, callback) });
            object window = NewShortcutDialog(setting);
            FieldInfo mainOwner = _drawerType.GetField("_currentKeyboardShortcutToSet", Static);
            FieldInfo dialogOwner = window.GetType().GetField("_currentKeyboardShortcutToSet", Static);
            WithCaptureCoordinator(window, manager =>
            {
                DrawerCall("BeginKeyboardShortcutCapture", setting, 1);
                mainOwner.SetValue(null, custom);
                DrawerCall("UpdateKeyboardShortcutCapture");
                Check(!MainCaptureActive && ReferenceEquals(mainOwner.GetValue(null), custom),
                    "a changed main owner cancels only the old built-in capture before polling native input");
                DialogCall(window, "BeginKeyboardShortcutCapture", 2);
                Check(ReferenceEquals(mainOwner.GetValue(null), custom), "starting a dialog recorder preserves the main custom drawer owner");
                dialogOwner.SetValue(null, custom);
                DialogCall(window, "UpdateKeyboardShortcutCapture");
                Check(!DialogCaptureActive(window) && ReferenceEquals(dialogOwner.GetValue(null), custom),
                    "a changed dialog owner cancels only the old built-in capture");
                manager.GetType().GetMethod("CancelBuiltInKeyRecordings", Instance).Invoke(manager, null);
                Check(ReferenceEquals(mainOwner.GetValue(null), custom) && ReferenceEquals(dialogOwner.GetValue(null), custom),
                    "coordinated built-in cancellation leaves live custom drawer recording pointers intact");
                DrawerCall("CompleteKeyboardShortcutCapture", NewShortcut("X"));
                DialogCall(window, "CompleteKeyboardShortcutCapture", NewShortcut("X"));
                Check(ShortcutValue(entry).ToString() == "F2" && DialogDraft(window).ToString() == "F2",
                    "stale completions after an owner change neither save nor stage input");
            });
        }

        private static void AssertRange(string text, float min, float max, Type type, object expected)
        {
            object[] arguments = { text, min, max, type, null };
            Check((bool)_parseRange.Invoke(null, arguments), "completed range value parses: " + text);
            Check(arguments[4]?.GetType() == type && Equals(arguments[4], expected),
                "range parsing preserves target type and clamp/round policy: " + text);
        }

        private static void AssertRangeRejected(string text, float min, float max, Type type)
        {
            object[] arguments = { text, min, max, type, null };
            Check(!(bool)_parseRange.Invoke(null, arguments) && arguments[4] == null,
                "incomplete, malformed or unrepresentable text does not produce a value: " + (text ?? "<null>"));
        }

        private static void RangeBufferSubmission()
        {
            object editor = Activator.CreateInstance(_rangeEditorType, Instance, null, new object[] { "RangeTest" }, null);
            Check(!FinishRange(editor, out _), "an unused editor has no pending value");
            StageRange(editor, "1.234567", "1.234567");
            Check(!FinishRange(editor, out string unchanged) && unchanged == null,
                "focus without text changes does not request rounding or a setter call");
            StageRange(editor, "10", "2");
            Check(FinishRange(editor, out string submitted) && submitted == "2",
                "the typed intermediate number is retained until submission, before range clamping");
            Check(!FinishRange(editor, out string duplicate) && duplicate == null,
                "closing or flushing the same edit twice cannot resubmit it");
            StageRange(editor, "10", "-");
            Check(FinishRange(editor, out string partial) && partial == "-",
                "the editor retains an incomplete sign for the parser to reject on submission");
            StageRange(editor, "10", "25");
            _rangeEditorType.GetMethod("Reset", Instance).Invoke(editor, new object[] { false });
            Check(!FinishRange(editor, out _), "Reset or Cancel discards the older pending text");
        }

        private static void RangeDialogSubmission()
        {
            object setting = NewRangeSetting("dialog", out object configEntry);
            object window = ExternalSettingsTests.CreateManagedEditor(setting);
            Type windowType = window.GetType();
            windowType.GetMethod("InitializeWindow", Instance).Invoke(window, null);
            windowType.GetMethod("ApplyRangeText", Instance).Invoke(window, new object[] { "25" });
            Check((float)configEntry.GetType().GetProperty("Value").GetValue(configEntry) == 10f &&
                (float)windowType.GetField("valueToSet", Instance).GetValue(window) == 25f,
                "leaving a dialog range field changes only the dialog draft");
            object editor = windowType.GetField("rangeTextEditor", Instance).GetValue(window);
            StageRange(editor, "25", "35");
            windowType.GetMethod("ClearCache", Instance).Invoke(window, null);
            Check(!FinishRange(editor, out _) &&
                (float)configEntry.GetType().GetProperty("Value").GetValue(configEntry) == 10f,
                "hiding the parent window discards unfinished text without saving the draft");
            windowType.GetMethod("ApplySettingValue", Instance).Invoke(window, null);
            Check((float)configEntry.GetType().GetProperty("Value").GetValue(configEntry) == 25f &&
                string.IsNullOrEmpty((string)windowType.GetField("errorOnSetting", Instance).GetValue(window)),
                "explicit Apply saves the staged value and cannot resurrect discarded text");
        }

        private static void RangeWriteGuards()
        {
            object setting = NewRangeSetting("guard", out object configEntry);
            int writes = 0;
            EventHandler changed = (_, __) => writes++;
            configEntry.GetType().GetEvent("SettingChanged").AddEventHandler(configEntry, changed);
            MethodInfo apply = _drawerType.GetMethod("ApplyPendingRangeText", Static);
            apply.Invoke(null, new[] { setting, "25" });
            Check((float)configEntry.GetType().GetProperty("Value").GetValue(configEntry) == 25f && writes == 1,
                "completed text reaches the real ConfigEntry through the existing setting setter");
            apply.Invoke(null, new[] { setting, "25" });
            apply.Invoke(null, new[] { setting, "abc" });
            Check(writes == 1, "equal and malformed values do not fire setting-changed callbacks");

            PropertyInfo readOnly = setting.GetType().GetProperty("ReadOnly", Instance);
            readOnly.SetValue(setting, true);
            apply.Invoke(null, new[] { setting, "40" });
            Check(writes == 1 && (float)configEntry.GetType().GetProperty("Value").GetValue(configEntry) == 25f,
                "a setting becoming read-only before submission keeps the committed value");
            readOnly.SetValue(setting, null);

            // Simulate the bridge's fail-closed state, without loading an external plugin or Unity objects.
            Type bridge = _assembly.GetType("ConfigurationManager.PathOfValheimanCompatibility", true);
            FieldInfo faulted = bridge.GetField("_faulted", Static);
            object priorFaulted = faulted.GetValue(null);
            object metadata = Activator.CreateInstance(_configType.Assembly.GetType("BepInEx.BepInPlugin", true),
                "treextr.pathofvalheiman", "Range test", "1.0.0");
            setting.GetType().GetProperty("PluginInfo", Instance).SetValue(setting, metadata);
            try
            {
                faulted.SetValue(null, true);
                apply.Invoke(null, new[] { setting, "40" });
                Check(writes == 1 && (float)configEntry.GetType().GetProperty("Value").GetValue(configEntry) == 25f,
                    "range submission cannot bypass the PoV write guard");
            }
            finally
            {
                faulted.SetValue(null, priorFaulted);
                configEntry.GetType().GetEvent("SettingChanged").RemoveEventHandler(configEntry, changed);
            }
        }

        private static void RangeReentrantSubmission()
        {
            MethodInfo clear = _drawerType.GetMethod("ClearCache", Static);
            MethodInfo finish = _drawerType.GetMethod("FinishPendingRangeTextEditors", Static);
            clear.Invoke(null, null);
            object first = NewRangeSetting("reentrant-first", out object firstConfig);
            object second = NewRangeSetting("reentrant-second", out object secondConfig);
            int firstWrites = 0;
            int secondWrites = 0;
            EventHandler firstChanged = (_, __) => { firstWrites++; clear.Invoke(null, null); };
            EventHandler secondChanged = (_, __) => { secondWrites++; clear.Invoke(null, null); };
            firstConfig.GetType().GetEvent("SettingChanged").AddEventHandler(firstConfig, firstChanged);
            secondConfig.GetType().GetEvent("SettingChanged").AddEventHandler(secondConfig, secondChanged);
            try
            {
                TrackPendingRange(first, "10", "25");
                TrackPendingRange(second, "10", "35");
                finish.Invoke(null, null);
                finish.Invoke(null, null);
                Check(firstWrites == 1 && secondWrites == 1 &&
                    (float)firstConfig.GetType().GetProperty("Value").GetValue(firstConfig) == 25f &&
                    (float)secondConfig.GetType().GetProperty("Value").GetValue(secondConfig) == 35f,
                    "a setter rebuilding UI caches cannot lose, duplicate or recursively apply the pending batch");
            }
            finally
            {
                firstConfig.GetType().GetEvent("SettingChanged").RemoveEventHandler(firstConfig, firstChanged);
                secondConfig.GetType().GetEvent("SettingChanged").RemoveEventHandler(secondConfig, secondChanged);
                clear.Invoke(null, null);
            }
        }

        private static object NewRangeSetting(string key, out object configEntry)
        {
            Assembly bepinex = _configType.Assembly;
            object config = NewConfig(Path.Combine(_suiteRoot, key + ".cfg"));
            Type rangeType = bepinex.GetType("BepInEx.Configuration.AcceptableValueRange`1", true).MakeGenericType(typeof(float));
            object range = Activator.CreateInstance(rangeType, 10f, 60f);
            object description = Activator.CreateInstance(bepinex.GetType("BepInEx.Configuration.ConfigDescription", true),
                "Range test", range, new object[0]);
            MethodInfo bind = _bindString.GetGenericMethodDefinition().MakeGenericMethod(typeof(float));
            configEntry = bind.Invoke(config, new[] { Activator.CreateInstance(_definitionType, "Range", key), 10f, description });
            return Activator.CreateInstance(_assembly.GetType("ConfigurationManager.ConfigSettingEntry", true), configEntry, null);
        }

        private static void TrackPendingRange(object setting, string original, string typed)
        {
            object editor = _drawerType.GetMethod("GetRangeTextEditor", Static).Invoke(null, new[] { setting });
            StageRange(editor, original, typed);
            object active = _drawerType.GetField("ActiveRangeTextEditors", Static).GetValue(null);
            active.GetType().GetMethod("Add").Invoke(active, new[] { setting });
        }

        private static void StageRange(object editor, string original, string typed)
        {
            _rangeEditorType.GetField("editing", Instance).SetValue(editor, true);
            _rangeEditorType.GetField("originalText", Instance).SetValue(editor, original);
            _rangeEditorType.GetField("text", Instance).SetValue(editor, typed);
        }

        private static bool FinishRange(object editor, out string text)
        {
            object[] arguments = { null };
            bool changed = (bool)_rangeEditorType.GetMethod("FinishOnWindowClose", Instance).Invoke(editor, arguments);
            text = (string)arguments[0];
            return changed;
        }

        private static void ShortcutLabels()
        {
            AssertShortcutLabel("X", new[] { "LeftAlt" }, "X + LeftAlt", "LeftAlt + X");
            AssertShortcutLabel("LeftAlt", new[] { "X" }, "LeftAlt + X", "X + LeftAlt");
            AssertShortcutLabel("X", new[] { "LeftShift", "LeftAlt" }, "X + LeftShift + LeftAlt", "LeftAlt + LeftShift + X");
            AssertShortcutLabel("D", new[] { "LeftAlt", "S" }, "D + S + LeftAlt", "LeftAlt + S + D");
            AssertShortcutLabel("LeftAlt", new[] { "LeftShift" }, "LeftAlt + LeftShift", "LeftShift + LeftAlt");
            AssertShortcutLabel("LeftShift", new[] { "LeftAlt" }, "LeftShift + LeftAlt", "LeftAlt + LeftShift");
            AssertShortcutLabel("X", new[] { "RightShift", "LeftAlt", "RightControl", "LeftShift", "RightAlt", "LeftControl", "S", "A" }, null,
                "LeftControl + RightControl + LeftAlt + RightAlt + LeftShift + RightShift + A + S + X");
        }

        private static void EmptyAndSingleShortcuts()
        {
            AssertShortcutLabel("None", new string[0], null, "Not set");
            AssertShortcutLabel("X", new string[0], "X", "X");
            AssertShortcutLabel("LeftAlt", new string[0], "LeftAlt", "LeftAlt");
        }

        private static void AssertShortcutLabel(string mainKey, string[] modifiers, string expectedSerialization, string expectedLabel)
        {
            Array held = Array.CreateInstance(_keyCodeType, modifiers.Length);
            for (int index = 0; index < modifiers.Length; index++)
                held.SetValue(Enum.Parse(_keyCodeType, modifiers[index]), index);
            object shortcut = Activator.CreateInstance(_shortcutType, Enum.Parse(_keyCodeType, mainKey), held);
            string originalSerialization = shortcut.ToString();
            if (expectedSerialization != null)
                Check(originalSerialization == expectedSerialization, "the original BepInEx serialization has the expected trigger order");
            string[] originalModifiers = Modifiers(shortcut);
            Check((string)_labelShortcut.Invoke(null, new[] { shortcut }) == expectedLabel,
                "visible label puts held keys before the trigger: " + originalSerialization);
            MethodInfo contentMethod = _drawerType.GetMethod("GetKeyboardShortcutContent", Static);
            object content = contentMethod.Invoke(null, new[] { shortcut });
            Check((string)content.GetType().GetProperty("text").GetValue(content) == expectedLabel &&
                string.IsNullOrEmpty((string)content.GetType().GetProperty("tooltip").GetValue(content)),
                "shared UI content uses the held-key-first label without a tooltip");
            Check(ReferenceEquals(content, contentMethod.Invoke(null, new[] { shortcut })),
                "redrawing an unchanged shortcut reuses its cached UI content");
            Check(shortcut.ToString() == originalSerialization &&
                _shortcutType.GetProperty("MainKey").GetValue(shortcut).ToString() == mainKey &&
                Modifiers(shortcut).SequenceEqual(originalModifiers),
                "displaying a shortcut never rewrites its stored trigger, modifiers or serialization");
        }

        private static string[] Modifiers(object shortcut)
            => ((IEnumerable)_shortcutType.GetProperty("Modifiers").GetValue(shortcut)).Cast<object>()
                .Select(value => value.ToString()).ToArray();

        private static void SaveCompletedBatch(bool automaticSave)
        {
            string path = Path.Combine(_suiteRoot, "completed-" + automaticSave + ".cfg");
            object config = NewConfig(path);
            object left = Bind(config, "Left", "old-left");
            object width = Bind(config, "Width", "old-width");
            _configType.GetMethod("Save").Invoke(config, null);
            byte[] originalBytes = File.ReadAllBytes(path);
            AutomaticSave(config, automaticSave);
            int callbacks = 0;
            _saveWindowConfig.Invoke(null, new object[] { config, (Action)(() =>
            {
                callbacks++;
                Check(!AutomaticSave(config), "intermediate values have automatic saving disabled");
                Value(left, "new-left");
                Check(File.ReadAllBytes(path).SequenceEqual(originalBytes), "first changed value does not persist a partial batch");
                Value(width, "new-width");
                Check(File.ReadAllBytes(path).SequenceEqual(originalBytes), "second changed value remains staged until callback completion");
            }) });
            Check(callbacks == 1 && AutomaticSave(config) == automaticSave,
                "completed batch runs once and restores the caller's automatic-save policy");

            // Reload with a separate real ConfigFile so this checks persistence, not just live entries.
            object reloaded = NewConfig(path);
            Check(Value(Bind(reloaded, "Left", "missing-left")) == "new-left" &&
                Value(Bind(reloaded, "Width", "missing-width")) == "new-width",
                "one completed batch persists both final values even when automatic saving was originally disabled");
        }

        private static void SaveFailedBatch(bool automaticSave)
        {
            string path = Path.Combine(_suiteRoot, "failed-" + automaticSave + ".cfg");
            object config = NewConfig(path);
            object left = Bind(config, "Left", "old-left");
            Bind(config, "Width", "old-width");
            _configType.GetMethod("Save").Invoke(config, null);
            byte[] originalBytes = File.ReadAllBytes(path);
            AutomaticSave(config, automaticSave);
            const string failure = "intentional geometry callback failure";
            Exception observed = null;
            try
            {
                _saveWindowConfig.Invoke(null, new object[] { config, (Action)(() =>
                {
                    Check(!AutomaticSave(config), "failing callback also has automatic saving disabled");
                    Value(left, "unfinished-left");
                    throw new InvalidOperationException(failure);
                }) });
            }
            catch (Exception error)
            {
                observed = Unwrap(error);
            }
            Check(observed is InvalidOperationException && observed.Message == failure,
                "callback failure propagates rather than reporting a successful save");
            Check(AutomaticSave(config) == automaticSave, "callback failure restores the original automatic-save policy");
            Check(File.ReadAllBytes(path).SequenceEqual(originalBytes), "callback failure never persists the partial batch");
            object reloaded = NewConfig(path);
            Check(Value(Bind(reloaded, "Left", "missing-left")) == "old-left" &&
                Value(Bind(reloaded, "Width", "missing-width")) == "old-width",
                "a fresh config still reads the previously committed geometry");
        }

        private static object NewConfig(string path)
        {
            object config = Activator.CreateInstance(_configType, path, false, null);
            AutomaticSave(config, false);
            return config;
        }

        private static object Bind(object config, string key, string defaultValue)
            => _bindString.Invoke(config, new[] { Activator.CreateInstance(_definitionType, "Window", key), defaultValue, null });
        private static bool AutomaticSave(object config) => (bool)_configType.GetProperty("SaveOnConfigSet").GetValue(config);
        private static void AutomaticSave(object config, bool value) => _configType.GetProperty("SaveOnConfigSet").SetValue(config, value);
        private static string Value(object entry) => (string)entry.GetType().GetProperty("Value").GetValue(entry);
        private static void Value(object entry, string value) => entry.GetType().GetProperty("Value").SetValue(entry, value);
        private static Exception Unwrap(Exception error)
            => error is TargetInvocationException invocation && invocation.InnerException != null ? Unwrap(invocation.InnerException) : error;
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Case(string name, Action action, ICollection<string> failures, ref int passed)
        {
            try { action(); passed++; Console.WriteLine("PASS " + name); }
            catch (Exception error) { failures.Add("UiEditing " + name + ": " + Unwrap(error)); }
        }
    }
}
