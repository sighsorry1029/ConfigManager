// Based on code made by MarC0 / ManlyMarco
// Copyright 2018 GNU General Public License v3.0

using BepInEx;
using BepInEx.Configuration;
using ConfigurationManager.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using UnityEngine;
using static ConfigurationManager.ConfigurationManager;
using static ConfigurationManager.ConfigurationManagerStyles;

namespace ConfigurationManager
{
    internal class SettingFieldDrawer
    {
        private static IEnumerable<KeyCode> _keysToCheck;

        public static Dictionary<Type, Action<SettingEntryBase>> SettingDrawHandlers { get; }

        private static readonly Dictionary<SettingEntryBase, ComboBox> ComboBoxCache = new Dictionary<SettingEntryBase, ComboBox>();
        private static readonly Dictionary<SettingEntryBase, ColorCacheEntry> ColorCache = new Dictionary<SettingEntryBase, ColorCacheEntry>();
        private static readonly Dictionary<ExternalSettingEntry, ExternalEnumCache> ExternalEnumCaches =
            new Dictionary<ExternalSettingEntry, ExternalEnumCache>();
        private static readonly Dictionary<KeyboardShortcut, GUIContent> ShortcutContents = new Dictionary<KeyboardShortcut, GUIContent>();

        private static ConfigurationManager _instance;

        private static SettingEntryBase _currentKeyboardShortcutToSet;
        private static SettingEntryBase _capturedShortcutSetting;
        private static readonly ShortcutCaptureState MainShortcutCapture = new ShortcutCaptureState();
        private static readonly List<KeyCode> ShortcutPressed = new List<KeyCode>();
        private static readonly List<KeyCode> ShortcutHeld = new List<KeyCode>();
        private static readonly List<KeyCode> ShortcutReleased = new List<KeyCode>();
        private static bool _shortcutCaptureDrawn;
        private static ExternalNumericEditor _activeExternalNumber;
        private static string _cancelledNumericControl;
        private static Rect _activeNumericScreenRect;
        private static bool _activeNumericRectValid;
        private static bool _activeNumericDrawn;

        // Keep input sampling separate from IMGUI so Layout/Repaint cannot record
        // the same press twice. Only newly recorded shortcuts use this policy.
        internal sealed class ShortcutCaptureState
        {
            private KeyCode mainKey;
            private readonly List<KeyCode> modifiers = new List<KeyCode>();
            private bool waitingForRelease;

            internal bool IsActive { get; private set; }
            internal bool NeedsRetry { get; private set; }
            internal int LastProcessedFrame { get; private set; } = int.MinValue;

            internal void Begin(int frame)
            {
                Cancel();
                IsActive = true;
                LastProcessedFrame = frame; // Ignore input from the click that armed recording.
            }

            internal void Cancel()
            {
                IsActive = false;
                NeedsRetry = false;
                waitingForRelease = false;
                mainKey = KeyCode.None;
                modifiers.Clear();
            }

            private static bool IsSupported(KeyCode key) => key != KeyCode.None && key != KeyCode.Mouse0;

            private static bool HasSupportedKey(IList<KeyCode> keys)
            {
                for (int index = 0; index < keys.Count; index++)
                    if (IsSupported(keys[index])) return true;
                return false;
            }

            internal bool ProcessFrame(int frame, IList<KeyCode> pressed, IList<KeyCode> held,
                IList<KeyCode> released, out KeyboardShortcut shortcut)
            {
                shortcut = KeyboardShortcut.Empty;
                if (!IsActive || frame <= LastProcessedFrame) return false;
                LastProcessedFrame = frame;

                if (waitingForRelease)
                {
                    if (!HasSupportedKey(held) && !HasSupportedKey(pressed)) waitingForRelease = false;
                    return false;
                }

                KeyCode newlyPressed = KeyCode.None;
                bool ambiguous = false;
                for (int index = 0; index < pressed.Count; index++)
                {
                    KeyCode key = pressed[index];
                    if (!IsSupported(key)) continue;
                    if (newlyPressed != KeyCode.None && newlyPressed != key) ambiguous = true;
                    newlyPressed = key;
                }
                if (newlyPressed != KeyCode.None)
                {
                    // Boolean polling cannot tell whether another key was released
                    // before or after this press within the same frame.
                    for (int index = 0; index < released.Count; index++)
                        if (IsSupported(released[index]) && released[index] != newlyPressed)
                            ambiguous = true;
                }
                if (ambiguous)
                {
                    mainKey = KeyCode.None;
                    modifiers.Clear();
                    NeedsRetry = true;
                    waitingForRelease = HasSupportedKey(held);
                    return false;
                }

                if (newlyPressed != KeyCode.None)
                {
                    mainKey = newlyPressed;
                    modifiers.Clear();
                    for (int index = 0; index < held.Count; index++)
                    {
                        KeyCode key = held[index];
                        if (IsSupported(key) && key != mainKey && !modifiers.Contains(key)) modifiers.Add(key);
                    }
                    NeedsRetry = false;
                }

                if (mainKey == KeyCode.None) return false;
                for (int index = 0; index < released.Count; index++)
                {
                    KeyCode key = released[index];
                    if (key != mainKey && !modifiers.Contains(key)) continue;
                    shortcut = new KeyboardShortcut(mainKey, modifiers.ToArray());
                    Cancel(); // Detach state before the caller invokes a setting callback.
                    return true;
                }
                return false;
            }
        }

        internal static bool PollShortcutCapture(ShortcutCaptureState capture, out KeyboardShortcut shortcut)
        {
            shortcut = KeyboardShortcut.Empty;
            int frame = Time.frameCount;
            if (!capture.IsActive || frame <= capture.LastProcessedFrame) return false;
            var input = UnityInput.Current;
            _keysToCheck ??= input.SupportedKeyCodes.Except(new[] { KeyCode.Mouse0, KeyCode.None }).ToArray();
            ShortcutPressed.Clear();
            ShortcutHeld.Clear();
            ShortcutReleased.Clear();
            foreach (KeyCode key in _keysToCheck)
            {
                if (input.GetKeyDown(key)) ShortcutPressed.Add(key);
                if (input.GetKey(key)) ShortcutHeld.Add(key);
                if (input.GetKeyUp(key)) ShortcutReleased.Add(key);
            }
            return capture.ProcessFrame(frame, ShortcutPressed, ShortcutHeld, ShortcutReleased, out shortcut);
        }

        internal static bool HasBuiltInKeyRecorder(SettingEntryBase setting)
        {
            if (setting == null || (setting.SettingType != typeof(KeyCode) && setting.SettingType != typeof(KeyboardShortcut)))
                return false;
            return (setting.CustomDrawer == null && setting.CustomHotkeyDrawer == null) || IsSettingFailedToCustomDraw(setting);
        }

        internal static bool CanCaptureShortcut(SettingEntryBase setting)
        {
            if (!HasBuiltInKeyRecorder(setting) || setting.SettingType != typeof(KeyboardShortcut) || setting.ReadOnly == true)
                return false;
            if (setting is ExternalSettingEntry external && !external.IsActive) return false;
            return !(setting is ConfigSettingEntry config) || !PathOfValheimanCompatibility.IsLocked(config);
        }

        internal static bool IsRecordingKeyboardShortcut => MainShortcutCapture.IsActive;

        internal static void BeginKeyboardShortcutCapture(SettingEntryBase setting, int frame)
        {
            if (!CanCaptureShortcut(setting)) return;
            _instance?.CancelBuiltInKeyRecordings();
            CancelBuiltInKeyRecording();
            _currentKeyboardShortcutToSet = setting;
            _capturedShortcutSetting = setting;
            MainShortcutCapture.Begin(frame);
            _shortcutCaptureDrawn = true;
        }

        internal static void BeginKeyCodeCapture(SettingEntryBase setting)
        {
            if (!HasBuiltInKeyRecorder(setting) || setting.SettingType != typeof(KeyCode)) return;
            _instance?.CancelBuiltInKeyRecordings();
            CancelBuiltInKeyRecording();
            _currentKeyboardShortcutToSet = setting;
        }

        // A new built-in recording replaces another built-in recorder in either window.
        // CustomHotkeyDrawer owns its ref state and is deliberately left alone.
        internal static void CancelBuiltInKeyRecording()
        {
            CancelKeyboardShortcutCapture();
            if (HasBuiltInKeyRecorder(_currentKeyboardShortcutToSet)) _currentKeyboardShortcutToSet = null;
        }

        internal static void CancelKeyboardShortcutCapture()
        {
            if (ReferenceEquals(_currentKeyboardShortcutToSet, _capturedShortcutSetting))
                _currentKeyboardShortcutToSet = null;
            _capturedShortcutSetting = null;
            MainShortcutCapture.Cancel();
            _shortcutCaptureDrawn = false;
        }

        internal static void UpdateKeyboardShortcutCapture()
        {
            if (!MainShortcutCapture.IsActive) return;
            SettingEntryBase setting = _capturedShortcutSetting;
            if (!ReferenceEquals(_currentKeyboardShortcutToSet, setting) || !CanCaptureShortcut(setting))
            {
                CancelKeyboardShortcutCapture();
                return;
            }
            if (!PollShortcutCapture(MainShortcutCapture, out KeyboardShortcut shortcut)) return;
            CompleteKeyboardShortcutCapture(shortcut);
        }

        internal static void CompleteKeyboardShortcutCapture(KeyboardShortcut shortcut)
        {
            SettingEntryBase setting = _capturedShortcutSetting;
            bool canWrite = ReferenceEquals(_currentKeyboardShortcutToSet, setting) && CanCaptureShortcut(setting);
            CancelKeyboardShortcutCapture();
            if (!canWrite) return;
            // Keep the existing read-only, PoV and owner setter boundary.
            try { setting.Set(shortcut); }
            catch (Exception ex) { LogWarning("Could not record shortcut for " + setting.SettingID + ": " + ex); }
        }

        internal static void BeginKeyboardShortcutGUI() => _shortcutCaptureDrawn = false;

        internal static void FinishUndrawnKeyboardShortcutCapture()
        {
            if (MainShortcutCapture.IsActive && !_shortcutCaptureDrawn) CancelKeyboardShortcutCapture();
        }

        // General ConfigEntry ranges clamp on submission. ExternalNumericEditor keeps its
        // separate owner-validation and error-retention policy.
        internal sealed class RangeTextEditor
        {
            private static bool windowMouseDown;
            private static Vector2 windowMouseScreenPosition;
            private static int windowEventRevision;

            internal static void BeginWindowEvent()
            {
                Event current = Event.current;
                ++windowEventRevision;
                windowMouseDown = current != null && current.type != EventType.Layout &&
                    (current.rawType == EventType.MouseDown || current.type == EventType.MouseDown);
                if (windowMouseDown)
                    windowMouseScreenPosition = GUIUtility.GUIToScreenPoint(current.mousePosition);
            }

            internal static void EndWindowEvent() => windowMouseDown = false;

            private readonly string controlName;
            private bool editing;
            private bool selectAllPending;
            private bool hasFieldRect;
            private int lastDrawnWindowEvent = -1;
            private Rect fieldScreenRect;
            private string text;
            private string originalText;

            internal RangeTextEditor(string name) => controlName = name;
            internal string DisplayText(string fallback) => editing ? text : fallback;
            internal bool IsEditing => editing;
            internal bool WasDrawnInCurrentWindowEvent => lastDrawnWindowEvent == windowEventRevision;

            private void ReleaseFocusIfOwned()
            {
                if (Event.current != null && GUI.GetNameOfFocusedControl() == controlName)
                    GUI.FocusControl(null);
            }

            internal void Reset(bool releaseFocus = false)
            {
                if (releaseFocus) ReleaseFocusIfOwned();
                editing = false;
                selectAllPending = false;
                hasFieldRect = false;
                text = null;
                originalText = null;
            }

            private bool Finish(out string committed, bool releaseFocus)
            {
                // Merely focusing a high-precision value must not round or save it.
                bool changed = editing && !string.Equals(text, originalText, StringComparison.Ordinal);
                committed = changed ? text : null;
                Reset(releaseFocus);
                return changed;
            }

            internal bool FinishIfUndrawn(out string committed)
            {
                if (!editing || WasDrawnInCurrentWindowEvent)
                {
                    committed = null;
                    return false;
                }
                return Finish(out committed, releaseFocus: true);
            }

            // Window hotkeys/disable can run outside OnGUI.
            internal bool FinishOnWindowClose(out string committed) => Finish(out committed, releaseFocus: false);

            internal bool Draw(string actual, GUIStyle style, float width, out string committed)
            {
                lastDrawnWindowEvent = windowEventRevision;
                bool focusedBefore = GUI.GetNameOfFocusedControl() == controlName;
                bool enter = editing && focusedBefore && Event.current.type == EventType.KeyDown &&
                    (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
                bool clickedOutside = editing && hasFieldRect && windowMouseDown &&
                    !fieldScreenRect.Contains(windowMouseScreenPosition);

                GUI.SetNextControlName(controlName);
                string typed = GUILayout.TextField(DisplayText(actual), style, GUILayout.Width(width));
                bool focused = GUI.GetNameOfFocusedControl() == controlName;
                if (Event.current.type == EventType.Repaint)
                {
                    Rect fieldRect = GUILayoutUtility.GetLastRect();
                    Vector2 minimum = GUIUtility.GUIToScreenPoint(fieldRect.min);
                    Vector2 maximum = GUIUtility.GUIToScreenPoint(fieldRect.max);
                    fieldScreenRect = Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
                    hasFieldRect = true;
                }
                if (focused && !editing)
                {
                    editing = true;
                    originalText = actual;
                    selectAllPending = true;
                }
                if (editing) text = typed;
                if (selectAllPending && focused && Event.current.type == EventType.Repaint)
                {
                    TextEditor editor = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
                    editor.SelectAll();
                    selectAllPending = false;
                }
                if (editing && (enter || !focused || clickedOutside))
                    return Finish(out committed, releaseFocus: true);

                committed = null;
                return false;
            }
        }

        private static readonly Dictionary<SettingEntryBase, RangeTextEditor> RangeTextEditors = new Dictionary<SettingEntryBase, RangeTextEditor>();
        private static readonly HashSet<SettingEntryBase> ActiveRangeTextEditors = new HashSet<SettingEntryBase>();
        private static readonly List<SettingEntryBase> RangeEditorsToFinish = new List<SettingEntryBase>();
        private static readonly List<KeyValuePair<SettingEntryBase, string>> PendingRangeValues = new List<KeyValuePair<SettingEntryBase, string>>();
        private static int nextRangeTextId;
        private static bool finishingRangeTextEditors;

        private static RangeTextEditor GetRangeTextEditor(SettingEntryBase setting)
        {
            if (!RangeTextEditors.TryGetValue(setting, out RangeTextEditor editor))
                RangeTextEditors.Add(setting, editor = new RangeTextEditor("CM_Range_" + ++nextRangeTextId));
            return editor;
        }

        internal static void CancelRangeTextEdit(SettingEntryBase setting)
        {
            if (RangeTextEditors.TryGetValue(setting, out RangeTextEditor editor))
                editor.Reset(releaseFocus: true);
            ActiveRangeTextEditors.Remove(setting);
        }

        private static void ApplyPendingRangeText(SettingEntryBase setting, string text)
        {
            if (text == null || setting.ReadOnly == true || setting.AcceptableValueRange.Key == null)
                return;
            try
            {
                float min = (float)Convert.ToDouble(setting.AcceptableValueRange.Key, CultureInfo.InvariantCulture);
                float max = (float)Convert.ToDouble(setting.AcceptableValueRange.Value, CultureInfo.InvariantCulture);
                if (TryParseRangeText(text, min, max, setting.SettingType, out object parsed) && !Equals(parsed, setting.Get()))
                    setting.Set(parsed); // Preserve PoV, read-only and external owner write checks.
            }
            catch (Exception ex)
            {
                LogWarning("Could not apply pending range value for " + setting.SettingID + ": " + ex);
            }
        }

        internal static void FinishUndrawnRangeTextEditors() => FinishRangeTextEditors(onlyUndrawn: true);
        internal static void FinishPendingRangeTextEditors() => FinishRangeTextEditors(onlyUndrawn: false);

        private static void FinishRangeTextEditors(bool onlyUndrawn)
        {
            if (finishingRangeTextEditors || ActiveRangeTextEditors.Count == 0) return;
            finishingRangeTextEditors = true;
            try
            {
                RangeEditorsToFinish.Clear();
                PendingRangeValues.Clear();
                foreach (SettingEntryBase setting in ActiveRangeTextEditors)
                    if (!RangeTextEditors.TryGetValue(setting, out RangeTextEditor editor) || !editor.IsEditing ||
                        !onlyUndrawn || !editor.WasDrawnInCurrentWindowEvent)
                        RangeEditorsToFinish.Add(setting);

                // Detach all state before invoking setters: a setting callback may rebuild
                // the list and clear these caches while a submission is in progress.
                foreach (SettingEntryBase setting in RangeEditorsToFinish)
                {
                    if (RangeTextEditors.TryGetValue(setting, out RangeTextEditor editor))
                    {
                        string text;
                        bool changed = onlyUndrawn ? editor.FinishIfUndrawn(out text) : editor.FinishOnWindowClose(out text);
                        if (changed) PendingRangeValues.Add(new KeyValuePair<SettingEntryBase, string>(setting, text));
                    }
                    ActiveRangeTextEditors.Remove(setting);
                }
                foreach (var pending in PendingRangeValues) ApplyPendingRangeText(pending.Key, pending.Value);
            }
            finally
            {
                RangeEditorsToFinish.Clear();
                PendingRangeValues.Clear();
                finishingRangeTextEditors = false;
            }
        }

        internal static bool TryParseRangeText(string text, float minimum, float maximum, Type settingType, out object value)
        {
            value = null;
            if (!float.TryParse(text?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float number) ||
                float.IsNaN(number) || float.IsInfinity(number)) return false;
            try
            {
                float rounded = Utilities.Utils.RoundWithPrecision(Mathf.Clamp(number, minimum, maximum), NumericPrecision);
                if (float.IsNaN(rounded) || float.IsInfinity(rounded)) return false;
                value = Convert.ChangeType(rounded, settingType, CultureInfo.InvariantCulture);
                return true;
            }
            catch (FormatException) { return false; }
            catch (OverflowException) { return false; }
            catch (InvalidCastException) { return false; }
        }

        public static bool SettingKeyboardShortcut => _currentKeyboardShortcutToSet != null;

        public static readonly HashSet<string> CustomFieldDrawerFailed = new HashSet<string>();

        static SettingFieldDrawer()
        {
            SettingDrawHandlers = new Dictionary<Type, Action<SettingEntryBase>>
            {
                {typeof(bool), DrawBoolField},
                {typeof(KeyboardShortcut), DrawKeyboardShortcut},
                {typeof(KeyCode), DrawKeyCode},
                {typeof(Color), DrawColor },
                {typeof(Vector2), DrawVector2 },
                {typeof(Vector3), DrawVector3 },
                {typeof(Vector4), DrawVector4 },
                {typeof(Quaternion), DrawQuaternion },
            };
        }

        public SettingFieldDrawer(ConfigurationManager instance)
        {
            _instance = instance;
        }

        public void DrawSettingValue(SettingEntryBase setting)
        {
            var color = GUI.backgroundColor;
            GUI.backgroundColor = Palette.Widget;
            try
            {
                if (DrawCustomField(setting))
                    return;

                if (setting is ExternalSettingEntry external && external.UsesInlineNumericEditor)
                    DrawExternalNumericField(external);
                else if (setting.AcceptableValueRange.Key != null)
                    DrawRangeField(setting);
                else if (setting.AcceptableValues != null)
                    DrawListField(setting);
                else if (DrawFieldBasedOnValueType(setting))
                    return;
                else if (setting.SettingType.IsEnum)
                    DrawEnumField(setting);
                else
                    DrawUnknownField(setting, _instance.RightColumnWidth);
            }
            finally
            {
                GUI.backgroundColor = color;
            }
        }

        public bool DrawCustomField(SettingEntryBase setting)
        {
            if (setting.CustomDrawer == null && setting.CustomHotkeyDrawer == null)
                return false;
            if (IsSettingFailedToCustomDraw(setting))
                return false;

            var color = GUI.contentColor;

            bool result = true;

            var textFieldFontSize = GUI.skin.textField.fontSize;
            var textAreaFontSize = GUI.skin.textArea.fontSize;
            var labelFontSize = GUI.skin.label.fontSize;
            var buttonFontSize = GUI.skin.button.fontSize;

            GUI.skin.textArea.fontSize = fontSize;
            GUI.skin.textField.fontSize = fontSize;
            GUI.skin.label.fontSize = fontSize;
            GUI.skin.button.fontSize = fontSize;

            try
            {
                GUI.contentColor = IsDefaultValue(setting) ? Palette.DefaultValueText : Palette.ChangedValueText;

                if (setting.CustomDrawer != null)
                    setting.CustomDrawer(setting is ConfigSettingEntry newSetting ? newSetting.Entry : null);
                else if (setting.CustomHotkeyDrawer != null)
                {
                    var isBeingSet = _currentKeyboardShortcutToSet == setting;
                    var isBeingSetOriginal = isBeingSet;
                    setting.CustomHotkeyDrawer(setting is ConfigSettingEntry newSetting ? newSetting.Entry : null, ref isBeingSet);

                    if (isBeingSet != isBeingSetOriginal)
                        _currentKeyboardShortcutToSet = isBeingSet ? setting : null;
                }
                else
                    result = false;
            }
            catch (Exception e)
            {
                SetSettingFailedToCustomDraw(setting, e);
                result = false;
            }

            GUI.contentColor = color;
            GUI.skin.textField.fontSize = textFieldFontSize;
            GUI.skin.textArea.fontSize = textAreaFontSize;
            GUI.skin.label.fontSize = labelFontSize;
            GUI.skin.button.fontSize = buttonFontSize;

            return result;
        }

        public static bool IsSettingFailedToCustomDraw(SettingEntryBase setting)
        {
            if (setting == null)
                return false;

            return CustomFieldDrawerFailed.Contains(setting.SettingID);
        }

        public static void SetSettingFailedToCustomDraw(SettingEntryBase setting, Exception e = null)
        {
            CustomFieldDrawerFailed.Add(setting.SettingID);

            if (e != null)
                LogWarning(setting.SettingID + "\n" + e);
        }

        public static void ClearCache()
        {
            CancelKeyboardShortcutCapture();
            FinishPendingRangeTextEditors();
            RangeTextEditors.Clear();
            ActiveRangeTextEditors.Clear();
            ShortcutContents.Clear();
            FinishExternalNumericEdit();
            ClearComboboxCache();
            _currentKeyboardShortcutToSet = null;

            foreach (var tex in ColorCache)
                UnityEngine.Object.Destroy(tex.Value.Tex);
            ColorCache.Clear();
        }

        internal static void Release()
        {
            ClearCache();
            _cancelledNumericControl = null;
            CustomFieldDrawerFailed.Clear();
            _instance = null;
        }

        internal static bool CancelExternalNumericEdit()
        {
            if (_activeExternalNumber == null || !_activeExternalNumber.IsFocused) return false;
            _cancelledNumericControl = _activeExternalNumber.ControlName;
            _activeExternalNumber.Cancel();
            _activeExternalNumber = null;
            _activeNumericRectValid = false;
            return true;
        }

        private static void FinishExternalNumericEdit()
        {
            ExternalNumericEditor editor = _activeExternalNumber;
            _activeExternalNumber = null;
            _activeNumericRectValid = false;
            editor?.HandleInput(editor.Text, false, false, false);
        }

        internal static void BeginExternalNumericGUI()
        {
            _activeNumericDrawn = false;
            if (_cancelledNumericControl != null)
            {
                if (GUI.GetNameOfFocusedControl() == _cancelledNumericControl) GUI.FocusControl(null);
                _cancelledNumericControl = null;
            }
            // Buttons need not take keyboard focus. Submit before their action,
            // including Reset, when clicking outside the active numeric field.
            if (_activeExternalNumber != null && _activeNumericRectValid &&
                Event.current.type == EventType.MouseDown &&
                !_activeNumericScreenRect.Contains(GUIUtility.GUIToScreenPoint(Event.current.mousePosition)))
            {
                FinishExternalNumericEdit();
                GUI.FocusControl(null);
            }
        }

        internal static void EndExternalNumericGUI()
        {
            if (_activeExternalNumber != null && (!_activeNumericDrawn ||
                GUI.GetNameOfFocusedControl() != _activeExternalNumber.ControlName))
                FinishExternalNumericEdit();
        }

        internal static void HandleExternalNumericInput(ExternalSettingEntry setting,
            string text, bool focused, bool enter, bool escape)
        {
            ExternalNumericEditor editor = setting.NumericEditor;
            if (focused && _activeExternalNumber != editor) FinishExternalNumericEdit();
            editor.HandleInput(text, focused, enter, escape);
            if (editor.IsFocused) _activeExternalNumber = editor;
            else if (_activeExternalNumber == editor)
            {
                _activeExternalNumber = null;
                _activeNumericRectValid = false;
            }
        }

        private static void DrawExternalNumericField(ExternalSettingEntry setting)
        {
            ExternalNumericEditor editor = setting.NumericEditor;
            editor.Refresh();
            bool focused = GUI.GetNameOfFocusedControl() == editor.ControlName;
            Event current = Event.current;
            bool keyDown = focused && current.type == EventType.KeyDown;
            bool enter = keyDown && (current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter);
            bool escape = keyDown && current.keyCode == KeyCode.Escape;
            if (enter || escape) current.Use();

            GUI.SetNextControlName(editor.ControlName);
            string text = GUILayout.TextField(editor.Text, GetTextStyle(setting), GUILayout.ExpandWidth(true));
            HandleExternalNumericInput(setting, text,
                GUI.GetNameOfFocusedControl() == editor.ControlName, enter, escape);
            if (escape) GUI.FocusControl(null);
            if (_activeExternalNumber == editor)
            {
                _activeNumericDrawn = true;
                if (current.type == EventType.Repaint)
                {
                    Rect fieldRect = GUILayoutUtility.GetLastRect();
                    Vector2 topLeft = GUIUtility.GUIToScreenPoint(fieldRect.min);
                    Vector2 bottomRight = GUIUtility.GUIToScreenPoint(fieldRect.max);
                    _activeNumericScreenRect = Rect.MinMaxRect(topLeft.x, topLeft.y, bottomRight.x, bottomRight.y);
                    _activeNumericRectValid = true;
                }
            }
        }

        public static void ClearComboboxCache()
        {
            ComboBoxCache.Clear();
            ExternalEnumCaches.Clear();
            ComboBox.CurrentDropdownDrawer = null;
        }

        public static void DrawCenteredLabel(string text, GUIStyle labelStyle)
        {
            GUILayout.BeginVertical(GUILayout.Width(60));
            GUILayout.FlexibleSpace();
            GUILayout.Label(text, labelStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();
        }

        public static bool DrawCategoryHeader(string text)
        {
            return GUILayout.Button(text, GetCategoryStyle(), GUILayout.ExpandWidth(true));
        }

        public static bool DrawCollapsedCategoryHeader(string text, bool isDefaultStyle)
        {
            return GUILayout.Button($"> {text} <", GetCategoryStyle(isDefaultStyle), GUILayout.ExpandWidth(true));
        }

        public static bool DrawPluginHeader(GUIContent content, bool isCollapsed, bool hasCollapsedCategories, bool withHover, out bool toggleCollapseAll)
        {
            GUILayout.BeginHorizontal(GetBackgroundStyle(withHover));

            toggleCollapseAll = false;
            bool state = GUILayout.Button(content, GetHeaderStyle(withHover && !isCollapsed), GUILayout.ExpandWidth(true));

            toggleCollapseAll = !isCollapsed && GUILayout.Button(hasCollapsedCategories ? "v" : "<", GetButtonStyle(), GUILayout.ExpandWidth(false));

            GUILayout.EndHorizontal();

            return state;
        }

        public static bool DrawPluginHeaderSplitViewList(GUIContent content, bool isActivePlugin) => GUILayout.Button(content, GetHeaderSplitViewStyle(isActivePlugin), GUILayout.ExpandWidth(true));
        public static bool DrawPluginCategorySplitViewList(GUIContent content, bool isActiveCategory) => GUILayout.Button(content, GetCategorySplitViewStyle(isActiveCategory), GUILayout.ExpandWidth(true));

        public static bool DrawCurrentDropdown()
        {
            if (ComboBox.CurrentDropdownDrawer != null)
            {
                ComboBox.CurrentDropdownDrawer.Invoke();
                ComboBox.CurrentDropdownDrawer = null;
                return true;
            }
            return false;
        }

        private static void DrawListField(SettingEntryBase setting)
        {
            var acceptableValues = setting.AcceptableValues;
            if (acceptableValues.Length == 0)
                throw new ArgumentException("AcceptableValueListAttribute returned an empty list of acceptable values. You need to supply at least 1 option.");

            if (!setting.SettingType.IsInstanceOfType(acceptableValues.FirstOrDefault(x => x != null)))
                throw new ArgumentException("AcceptableValueListAttribute returned a list with items of type other than the settng type itself.");

            if (setting.SettingType == typeof(KeyCode))
                DrawKeyCode(setting);
            else
                DrawComboboxField(setting, acceptableValues, _instance.currentWindowRect.yMax);
        }

        private static bool DrawFieldBasedOnValueType(SettingEntryBase setting)
        {
            if (SettingDrawHandlers.TryGetValue(setting.SettingType, out var drawMethod))
            {
                drawMethod(setting);
                return true;
            }
            return false;
        }

        private static void DrawBoolField(SettingEntryBase setting)
        {
            GUI.backgroundColor = Palette.Widget;
            bool boolVal = (bool)setting.Get();

            Color color = GUI.backgroundColor;
            if (boolVal)
                GUI.backgroundColor = Palette.Enabled;

            bool result = GUILayout.Toggle(boolVal, boolVal ? UiText.EnabledText : UiText.DisabledText, GetToggleStyle(setting), GUILayout.ExpandWidth(true));
            if (result != boolVal)
                setting.Set(result);

            if (boolVal)
                GUI.backgroundColor = color;
        }

        private static void DrawEnumField(SettingEntryBase setting)
        {
            if (setting is ExternalSettingEntry external)
            {
                ExternalEnumCache cache = GetExternalEnumCache(external);
                if (cache.IsEligible)
                {
                    if (cache.IsFlags)
                        DrawFlagsField(setting, null, (int)(_instance.RightColumnWidth * 0.8f), cache);
                    else
                        DrawComboboxField(setting, cache.Values, _instance.currentWindowRect.yMax, cache);
                    return;
                }
            }
            if (setting.SettingType.GetCustomAttributes(typeof(FlagsAttribute), false).Any())
                DrawFlagsField(setting, Enum.GetValues(setting.SettingType), (int)(_instance.RightColumnWidth * 0.8f));
            else
                DrawComboboxField(setting, Enum.GetValues(setting.SettingType), _instance.currentWindowRect.yMax);
        }

        internal static ExternalEnumCache GetExternalEnumCache(ExternalSettingEntry setting)
        {
            if (!ExternalEnumCaches.TryGetValue(setting, out ExternalEnumCache cache))
            {
                cache = new ExternalEnumCache(setting);
                ExternalEnumCaches.Add(setting, cache);
            }
            return cache;
        }

        private static void DrawFlagsField(SettingEntryBase setting, IList enumValues, int maxWidth,
            ExternalEnumCache externalCache = null)
        {
            var currentValue = Convert.ToInt64(setting.Get());
            var defaultValue = Convert.ToInt64(setting.DefaultValue);

            var allValues = externalCache == null
                ? enumValues.Cast<Enum>().Select(x => new { name = x.ToString(), val = Convert.ToInt64(x) }).ToArray()
                : null;
            int count = externalCache?.FlagMasks.Length ?? allValues.Length;

            // Vertically stack Horizontal groups of the options to deal with the options taking more width than is available in the window
            GUILayout.BeginVertical(GUILayout.MaxWidth(maxWidth));
            {
                for (var index = 0; index < count;)
                {
                    GUILayout.BeginHorizontal();
                    {
                        var currentWidth = 0;
                        bool drewAny = false;
                        for (; index < count; index++)
                        {
                            long mask = externalCache == null ? allValues[index].val : externalCache.FlagMasks[index];

                            // Skip the 0 / none enum value, just uncheck everything to get 0
                            if (mask != 0)
                            {
                                bool curr = (currentValue & mask) == mask;
                                bool defValue = (defaultValue & mask) == mask;
                                GUIContent content = externalCache == null
                                    ? new GUIContent(allValues[index].name) : externalCache.FlagContents[index];

                                GUIStyle style = GetToggleStyle(curr == defValue);

                                // Make sure this horizontal group doesn't extend over window width, if it does then start a new horiz group below
                                var textDimension = (int)style.CalcSize(content).x;
                                if (currentWidth + textDimension > maxWidth && drewAny)
                                    break;

                                drewAny = true;
                                currentWidth += textDimension;
                                GUI.changed = false;

                                var newVal = GUILayout.Toggle(curr, content, style, GUILayout.ExpandWidth(false));
                                if (GUI.changed)
                                {
                                    if (externalCache != null) externalCache.SetFlag(index, newVal);
                                    else
                                    {
                                        var newValue = newVal ? currentValue | mask : currentValue & ~mask;
                                        setting.Set(Enum.ToObject(setting.SettingType, newValue));
                                    }
                                }
                            }
                        }

                        // Endless cycle protection
                        if (!drewAny)
                            index++;
                    }
                    GUILayout.EndHorizontal();
                }

                GUI.changed = false;
            }
            GUILayout.EndVertical();
            // Make sure the reset button is properly spaced
            GUILayout.FlexibleSpace();
        }

        private static void DrawComboboxField(SettingEntryBase setting, IList list, float windowYmax,
            ExternalEnumCache externalCache = null)
        {
            var buttonText = externalCache == null ? ObjectToGuiContent(setting.Get()) : externalCache.GetCurrentContent();
            var dispRect = GUILayoutUtility.GetRect(buttonText, GetButtonStyle(), GUILayout.ExpandWidth(true));

            if (!ComboBoxCache.TryGetValue(setting, out var box))
            {
                box = new ComboBox(dispRect, buttonText,
                                externalCache?.Contents ?? list.Cast<object>().Select(ObjectToGuiContent).ToArray(),
                                externalCache?.DefaultContent ?? ObjectToGuiContent(setting.DefaultValue),
                                GetButtonStyle(), GetButtonStyle(isDefaultValue:false),
                                GetBoxStyle(),
                                GetComboBoxStyle(),
                                windowYmax);

                ComboBoxCache[setting] = box;
            }
            else
            {
                box.Rect = dispRect;
                box.ButtonContent = buttonText;
            }

            if (externalCache != null)
            {
                box.Show(externalCache.SelectionChanged);
                return;
            }
            box.Show(id =>
            {
                if (id >= 0 && id < list.Count)
                    setting.Set(list[id]);
            });
        }

        internal static GUIContent ObjectToGuiContent(object x)
        {
            if (x is Enum)
            {
                var enumType = x.GetType();
                var enumMember = enumType.GetMember(x.ToString()).FirstOrDefault();
                var attr = enumMember?.GetCustomAttributes(typeof(DescriptionAttribute), false).Cast<DescriptionAttribute>().FirstOrDefault();
                if (attr != null)
                    return new GUIContent(attr.Description);
                return new GUIContent(x.ToString().ToProperCase());
            }
            return new GUIContent(x.ToString());
        }

        private static void DrawRangeField(SettingEntryBase setting)
        {
            var value = setting.Get();
            var converted = (float)Convert.ToDouble(value, CultureInfo.InvariantCulture);
            var leftValue = (float)Convert.ToDouble(setting.AcceptableValueRange.Key, CultureInfo.InvariantCulture);
            var rightValue = (float)Convert.ToDouble(setting.AcceptableValueRange.Value, CultureInfo.InvariantCulture);

            float height = GetTextStyle(setting).CalcHeight(new GUIContent(value.ToString()), 100f);
            GUILayout.BeginHorizontal(GUILayout.Height(height));

            try
            {
                float result = DrawCenteredHorizontalSlider(converted, leftValue, rightValue, height);

                if (Math.Abs(result - converted) >= Mathf.Abs(rightValue - leftValue) / Math.Pow(10, NumericPrecision + 2))
                {
                    CancelRangeTextEdit(setting);
                    var newValue = Convert.ChangeType(Utilities.Utils.RoundWithPrecision(result, NumericPrecision), setting.SettingType, CultureInfo.InvariantCulture);
                    setting.Set(newValue);
                    value = setting.Get();
                }

                if (setting.ShowRangeAsPercent == true)
                {
                    DrawCenteredLabel($"{Mathf.Abs(result - leftValue) / Mathf.Abs(rightValue - leftValue):P0}", GetLabelStyle(setting));
                }
                else
                {
                    // Preserve the exact displayed source until a changed edit is committed.
                    string strVal = Convert.ToString(value, CultureInfo.InvariantCulture);
                    RangeTextEditor editor = GetRangeTextEditor(setting);
                    if (editor.Draw(strVal, GetTextStyle(setting), 50f, out string text))
                        ApplyPendingRangeText(setting, text);
                    if (editor.IsEditing) ActiveRangeTextEditors.Add(setting);
                    else ActiveRangeTextEditors.Remove(setting);
                }
            }
            finally
            {
                GUILayout.EndHorizontal();
            }
        }

        private static float DrawCenteredHorizontalSlider(float converted, float leftValue, float rightValue, float height)
        {
            GUILayout.BeginVertical(GUILayout.Height(height));
            GUILayout.Space(height * 0.45f);
            var result = GUILayout.HorizontalSlider(converted, leftValue, rightValue, GetSliderStyle(), GetThumbStyle(), GUILayout.ExpandWidth(true));
            GUILayout.EndVertical();
            return result;
        }

        private void DrawUnknownField(SettingEntryBase setting, int rightColumnWidth)
        {
            GUILayout.BeginVertical();
            GUILayout.Space(4f);

            // Try to use user-supplied converters
            if (setting.ObjToStr != null && setting.StrToObj != null)
            {
                var text = setting.ObjToStr(setting.Get()).AppendZeroIfFloat(setting.SettingType);
                if (text.IsNullOrWhiteSpace() && setting.DefaultValue.ToString() != "")
                    GUI.backgroundColor = Palette.ChangedValueText;

                var result = GUILayout.TextArea(text, GetTextStyle(setting), GUILayout.MaxWidth(rightColumnWidth)).AppendZeroIfFloat(setting.SettingType);
                if (result != text)
                    setting.Set(setting.StrToObj(result));
            }
            else
            {
                // Fall back to slow/less reliable method
                var rawValue = setting.Get();
                var value = rawValue == null ? "NULL" : Convert.ToString(rawValue, CultureInfo.InvariantCulture).AppendZeroIfFloat(setting.SettingType);

                if (value.IsNullOrWhiteSpace() && setting.DefaultValue.ToString() != "")
                    GUI.backgroundColor = Palette.ChangedValueText;

                if (CanCovert(value, setting.SettingType))
                {
                    var result = GUILayout.TextField(value, GetTextStyle(setting), GUILayout.MaxWidth(rightColumnWidth)).AppendZeroIfFloat(setting.SettingType);
                    if (result != value)
                        try
                        {
                            setting.Set(Convert.ChangeType(result, setting.SettingType, CultureInfo.InvariantCulture));
                        }
                        catch
                        {
                            // Don't change anything if format is bad
                        }
                }
                else
                {
                    GUILayout.TextArea(value, GetTextStyle(setting), GUILayout.MaxWidth(rightColumnWidth));
                }
            }

            GUILayout.EndVertical();

            // When using MaxWidth the width will always be less than full window size, use this to fill this gap and push the Reset button to the right edge
            GUILayout.FlexibleSpace();
        }

        private readonly Dictionary<Type, bool> _canCovertCache = new Dictionary<Type, bool>();
        private bool CanCovert(string value, Type type)
        {
            if (_canCovertCache.ContainsKey(type))
                return _canCovertCache[type];

            try
            {
                var _ = Convert.ChangeType(value, type);
                _canCovertCache[type] = true;
                return true;
            }
            catch
            {
                _canCovertCache[type] = false;
                return false;
            }
        }

        private static void DrawKeyCode(SettingEntryBase setting)
        {
            if (ReferenceEquals(_currentKeyboardShortcutToSet, setting))
            {
                GUILayout.Label(UiText.SingleKeyCaptureText, GetLabelStyle(), GUILayout.ExpandWidth(true));
                GUIUtility.keyboardControl = -1;

                var input = UnityInput.Current;
                if (_keysToCheck == null) _keysToCheck = input.SupportedKeyCodes.Except(new[] { KeyCode.Mouse0, KeyCode.None }).ToArray();
                foreach (var key in _keysToCheck)
                {
                    if (input.GetKeyUp(key))
                    {
                        setting.Set(key);
                        _currentKeyboardShortcutToSet = null;
                        break;
                    }
                }

                if (GUILayout.Button(UiText.CancelText, GetButtonStyle(), GUILayout.ExpandWidth(false)))
                    _currentKeyboardShortcutToSet = null;
            }
            else
            {
                var acceptableValues = setting.AcceptableValues?.Length > 1 ? setting.AcceptableValues : Enum.GetValues(setting.SettingType);
                DrawComboboxField(setting, acceptableValues, _instance.currentWindowRect.yMax);

                if (GUILayout.Button(new GUIContent(UiText.ShortcutKeyText), GetButtonStyle(), GUILayout.ExpandWidth(false)))
                    BeginKeyCodeCapture(setting);
            }
        }

        private static void DrawKeyboardShortcut(SettingEntryBase setting)
        {
            if (MainShortcutCapture.IsActive && ReferenceEquals(_capturedShortcutSetting, setting))
            {
                _shortcutCaptureDrawn = true;
                GUILayout.Label(MainShortcutCapture.NeedsRetry ? UiText.ShortcutRetryText : UiText.ShortcutKeysText,
                    GetLabelStyleSettingName(), GUILayout.ExpandWidth(true));
                GUIUtility.keyboardControl = -1;

                if (GUILayout.Button(UiText.CancelText, GetButtonStyle(), GUILayout.ExpandWidth(false)))
                    CancelKeyboardShortcutCapture();
            }
            else
            {
                KeyboardShortcut shortcut = (KeyboardShortcut)setting.Get();
                if (GUILayout.Button(GetKeyboardShortcutContent(shortcut), GetButtonStyle(setting), GUILayout.ExpandWidth(true)) &&
                    CanCaptureShortcut(setting))
                    BeginKeyboardShortcutCapture(setting, Time.frameCount);

                if (GUILayout.Button(UiText.ClearText, GetButtonStyle(), GUILayout.ExpandWidth(false)))
                {
                    CancelKeyboardShortcutCapture();
                    setting.Set(KeyboardShortcut.Empty);
                    _currentKeyboardShortcutToSet = null;
                }
            }
        }

        internal static GUIContent GetKeyboardShortcutContent(KeyboardShortcut shortcut)
        {
            if (!ShortcutContents.TryGetValue(shortcut, out GUIContent content))
            {
                content = new GUIContent(FormatKeyboardShortcutLabel(shortcut));
                ShortcutContents.Add(shortcut, content);
            }
            return content;
        }

        private static void DrawVector2(SettingEntryBase obj)
        {
            var setting = (Vector2)obj.Get();
            var copy = setting;
            setting.x = DrawSingleVectorSlider(setting.x, "X", ((Vector2)obj.DefaultValue).x);
            setting.y = DrawSingleVectorSlider(setting.y, "Y", ((Vector2)obj.DefaultValue).y);
            if (setting != copy) obj.Set(setting);
        }

        private static void DrawVector3(SettingEntryBase obj)
        {
            var setting = (Vector3)obj.Get();
            var copy = setting;
            setting.x = DrawSingleVectorSlider(setting.x, "X", ((Vector3)obj.DefaultValue).x);
            setting.y = DrawSingleVectorSlider(setting.y, "Y", ((Vector3)obj.DefaultValue).y);
            setting.z = DrawSingleVectorSlider(setting.z, "Z", ((Vector3)obj.DefaultValue).z);
            if (setting != copy) obj.Set(setting);
        }

        private static void DrawVector4(SettingEntryBase obj)
        {
            var setting = (Vector4)obj.Get();
            var copy = setting;
            setting.x = DrawSingleVectorSlider(setting.x, "X", ((Vector4)obj.DefaultValue).x);
            setting.y = DrawSingleVectorSlider(setting.y, "Y", ((Vector4)obj.DefaultValue).y);
            setting.z = DrawSingleVectorSlider(setting.z, "Z", ((Vector4)obj.DefaultValue).z);
            setting.w = DrawSingleVectorSlider(setting.w, "W", ((Vector4)obj.DefaultValue).w);
            if (setting != copy) obj.Set(setting);
        }

        private static void DrawQuaternion(SettingEntryBase obj)
        {
            var setting = (Quaternion)obj.Get();
            var copy = setting;
            setting.x = DrawSingleVectorSlider(setting.x, "X", ((Quaternion)obj.DefaultValue).x);
            setting.y = DrawSingleVectorSlider(setting.y, "Y", ((Quaternion)obj.DefaultValue).y);
            setting.z = DrawSingleVectorSlider(setting.z, "Z", ((Quaternion)obj.DefaultValue).z);
            setting.w = DrawSingleVectorSlider(setting.w, "W", ((Quaternion)obj.DefaultValue).w);
            if (setting != copy) obj.Set(setting);
        }

        private static float DrawSingleVectorSlider(float setting, string label, float defaultValue)
        {
            GUILayout.Label(label, GetLabelStyle(), GUILayout.ExpandWidth(false));
            const int precision = NumericPrecision;
            string format = "0." + new string('#', precision);
            string value = GUILayout.TextField(setting.ToString(format, CultureInfo.InvariantCulture), GetTextStyle(setting, defaultValue), GUILayout.ExpandWidth(true));
            if (value.EndsWith(".") || value.EndsWith(","))
                value = string.Concat(value, string.Empty.PadRight(Math.Max(precision - 1, 0), '0'), 1);

            Utilities.Utils.TryParseFloat(value, out var x);
            return x;
        }

        private static void DrawColor(SettingEntryBase obj)
        {
            Color setting = Utilities.Utils.RoundColorToHEX((Color)obj.Get());
            Color defaultColor = Utilities.Utils.RoundColorToHEX((Color)obj.DefaultValue);

            GUILayout.BeginVertical();
            GUILayout.BeginVertical(GetBoxStyle());
            GUILayout.BeginHorizontal();
            bool isDefaultValue = DrawHexField(ref setting, defaultColor);

            GUILayout.Space(3f);
            GUIHelper.BeginColor(setting);
            GUILayout.Label(string.Empty, GUILayout.ExpandWidth(true));

            if (!ColorCache.TryGetValue(obj, out var cacheEntry))
            {
                cacheEntry = new ColorCacheEntry { Tex = new Texture2D(40, 10, TextureFormat.ARGB32, false), Last = setting };
                cacheEntry.Tex.FillTexture(setting);
                ColorCache[obj] = cacheEntry;
            }

            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(GUILayoutUtility.GetLastRect(), cacheEntry.Tex);
            }

            GUIHelper.EndColor();
            GUILayout.Space(3f);

            GUILayout.EndHorizontal();

            GUILayout.Space(2f);
            GUILayout.BeginHorizontal();

            DrawColorField("R", ref setting, ref setting.r, Utilities.Utils.RoundColor(setting.r) == Utilities.Utils.RoundColor(defaultColor.r));
            GUILayout.Space(3f);
            DrawColorField("G", ref setting, ref setting.g, Utilities.Utils.RoundColor(setting.g) == Utilities.Utils.RoundColor(defaultColor.g));
            GUILayout.Space(3f);
            DrawColorField("B", ref setting, ref setting.b, Utilities.Utils.RoundColor(setting.b) == Utilities.Utils.RoundColor(defaultColor.b));
            GUILayout.Space(3f);
            DrawColorField("A", ref setting, ref setting.a, Utilities.Utils.RoundColor(setting.a) == Utilities.Utils.RoundColor(defaultColor.a));

            if (setting != cacheEntry.Last)
            {
                obj.Set(setting);
                cacheEntry.Tex.FillTexture(setting);
                cacheEntry.Last = setting;
            }

            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUILayout.Space(2f);
            GUILayout.EndVertical();
        }

        private static bool DrawHexField(ref Color value, Color defaultValue)
        {
            GUIStyle style = GetTextStyle(value, defaultValue);
            string currentText = $"#{ColorUtility.ToHtmlStringRGBA(value)}";
            Utilities.Utils.UpdateHexString(ref currentText, GUILayout.TextField(currentText, style, GUILayout.Width(style.CalcSize(new GUIContent("#CCCCCCCC.")).x), GUILayout.ExpandWidth(false)));
            if (ColorUtility.TryParseHtmlString(currentText, out Color color))
                value = color;

            return IsEqualColorConfig(value, defaultValue);
        }

        private static void DrawColorField(string fieldLabel, ref Color settingColor, ref float settingValue, bool isDefaultValue)
        {
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUILayout.Label(fieldLabel, GetLabelStyle(), GUILayout.ExpandWidth(true));

            string currentText = Utilities.Utils.RoundWithPrecision(settingValue, 3).ToString("0.000");
            string valueString = GUILayout.TextField(currentText, GetTextStyle(isDefaultValue), GUILayout.MaxWidth(45f), GUILayout.ExpandWidth(true));
            if (valueString.StartsWith('1'))
                SetColorValue(ref settingColor, 1f);
            else if (valueString.StartsWith('0') && settingValue == 1f)
                SetColorValue(ref settingColor, 0f);
            else if (Utilities.Utils.TryParseFloat(valueString, out float value))
                SetColorValue(ref settingColor, value);

            GUILayout.EndHorizontal();
            GUILayout.Space(1f);

            SetColorValue(ref settingColor, GUILayout.HorizontalSlider(settingValue, 0f, 1f, GUILayout.ExpandWidth(true)));

            GUILayout.EndVertical();

            void SetColorValue(ref Color color, float value)
            {
                float roundedValue = Utilities.Utils.RoundWithPrecision(value, 3);
                switch (fieldLabel)
                {
                    case "R": color.r = roundedValue; break;
                    case "G": color.g = roundedValue; break;
                    case "B": color.b = roundedValue; break;
                    case "A": color.a = roundedValue; break;
                }
            }
        }

        private sealed class ColorCacheEntry
        {
            public Color Last;
            public Texture2D Tex;
        }
    }
}
