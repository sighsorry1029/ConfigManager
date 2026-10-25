using BepInEx;
using BepInEx.Configuration;
using ConfigurationManager.Utilities;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static ConfigurationManager.ConfigurationManager;
using static ConfigurationManager.ConfigurationManagerStyles;

namespace ConfigurationManager
{
    internal class SettingEditWindow
    {
        private Rect _windowRect = new Rect(_windowPositionEditSetting.Value, _windowSizeEditSetting.Value);
        private bool _windowGeometryDirty;
        private bool _windowGeometrySaveFailed;
        private Vector2? _pendingWindowSize;

        private const int WindowId = -6800;
        private const string NewItemFieldControlName = "StringListNewItemField";
        private SettingEntryBase setting;

        private static SettingEntryBase _currentKeyboardShortcutToSet;
        private SettingFieldDrawer.ShortcutCaptureState _shortcutCapture;
        private SettingEntryBase _capturedShortcutSetting;
        private bool _shortcutCaptureDrawn;
        public Dictionary<Type, Action> SettingDrawHandlers { get; }

        private static IEnumerable<KeyCode> _keysToCheck;

        private ColorCacheEntry _colorPreview;

        private Vector2 _scrollPosition = Vector2.zero;
        private Vector2 _scrollPositionEnum = Vector2.zero;

        private int listIndex = -1;
        private IList listEnum = null;
        private GUIContent[] _enumContent;

        private Action drawerFunction;

        private string errorText;

        private object valueToSet;
        private readonly SettingFieldDrawer.RangeTextEditor rangeTextEditor =
            new SettingFieldDrawer.RangeTextEditor("CM_Range_Edit");

        private string errorOnSetting;
        private string _externalText;
        private bool _externalTextEdited;

        private readonly List<string> vectorParts = new List<string>();
        private readonly List<float> vectorFloats = new List<float>();
        private readonly List<float> vectorDefault = new List<float>();

        private string colorAsHEX;

        private List<string> separatedStringDefault = new List<string>();
        private List<string> separatedString = new List<string>();
        private string separator;
        private int editStringView;
        private string newItem;

        private ConfigEntryBase dummyCustomDrawerConfigEntry;

        private bool IsStringList => setting != null && setting.SettingType != null && typeof(IList<string>).IsAssignableFrom(setting.SettingType);

        public SettingEditWindow()
        {
            SettingDrawHandlers = new Dictionary<Type, Action>
            {
                { typeof(bool), DrawBoolField},
                { typeof(KeyboardShortcut), DrawKeyboardShortcut},
                { typeof(KeyCode), DrawKeyCode},
                { typeof(Color), DrawColor },
                { typeof(Vector2), DrawVector },
                { typeof(Vector3), DrawVector },
                { typeof(Vector4), DrawVector },
                { typeof(Quaternion), DrawVector },
            };
        }

        public void EditSetting(SettingEntryBase setting)
        {
            if (setting is ExternalSettingEntry external && (external.ReadOnly == true || !external.IsActive)) return;
            if (setting is ConfigSettingEntry config && PathOfValheimanCompatibility.IsLocked(config)) return;
            SettingFieldDrawer.CancelKeyboardShortcutCapture();
            CancelKeyboardShortcutCapture();
            if (this.setting == setting && IsOpen)
            {
                IsOpen = false;
                return;
            }

            this.setting = setting;

            InitializeWindow();

            IsOpen = true;
        }

        private bool _isOpen;
        public bool IsOpen
        {
            get => _isOpen;
            set
            {
                if (_isOpen && !value) FlushWindowGeometry();
                _isOpen = value;
                if (!value)
                {
                    ClearCache();
                    _currentKeyboardShortcutToSet = null;
                }
            }
        }

        internal void ResetWindowGeometry()
        {
            _windowRect = new Rect(_windowPositionEditSetting.Value, _windowSizeEditSetting.Value);
            _windowGeometryDirty = false;
            _windowGeometrySaveFailed = false;
            _pendingWindowSize = null;
        }

        internal void FlushWindowGeometry()
        {
            if (!_windowGeometryDirty) return;
            try { SaveCurrentSizeAndPosition(); }
            catch (Exception ex)
            {
                _windowGeometrySaveFailed = true;
                LogWarning("Could not save setting editor geometry: " + ex.Message);
            }
        }

        internal void CloseUnregisteredExternalSetting()
        {
            if (setting is ExternalSettingEntry external && !external.IsActive) Dispose();
        }

        internal void Dispose()
        {
            IsOpen = false;
            setting = null;
            drawerFunction = null;
            valueToSet = null;
            listEnum = null;
            _enumContent = null;
            dummyCustomDrawerConfigEntry = null;
            _externalText = null;
            _externalTextEdited = false;
            _currentKeyboardShortcutToSet = null;
        }

        public void OnGUI()
        {
            if (!IsOpen)
                return;

            if (!_windowGeometryDirty)
            {
                _windowRect.size = _windowSizeEditSetting.Value;
                _windowRect.position = _windowPositionEditSetting.Value;
            }

            Color color = GUI.backgroundColor;
            GUI.backgroundColor = Palette.WindowBackground;

            Rect drawnWindow = GUI.Window(WindowId, _windowRect, DrawWindow, $"{setting.PluginInfo.Name} {setting.PluginInfo.Version}", GetWindowStyle());
            if (_pendingWindowSize.HasValue)
            {
                drawnWindow.size = _pendingWindowSize.Value;
                _pendingWindowSize = null;
            }
            _windowRect = drawnWindow;

            if (_windowRect.position != _windowPositionEditSetting.Value)
                _windowGeometryDirty = true;
            bool mouseHeld = UnityInput.Current.GetMouseButton(0);
            if (mouseHeld) _windowGeometrySaveFailed = false;
            if (_windowGeometryDirty && !mouseHeld && !_windowGeometrySaveFailed)
                FlushWindowGeometry();

            GUI.backgroundColor = color;
        }

        private void UpdateStringList()
        {
            if (separator.IsNullOrWhiteSpace())
                separator = ",";

            if (setting.SettingType != typeof(string) && !IsStringList)
                return;

            separatedString.Clear();
            if (IsStringList)
                try
                {
                    separatedString.AddRange(valueToSet as IList<string>);
                }
                catch
                {
                    separatedString.AddRange((valueToSet?.ToString() ?? string.Empty).Split(new string[] { separator }, StringSplitOptions.None));
                }
            else
                separatedString.AddRange((valueToSet?.ToString() ?? string.Empty).Split(new string[] { separator }, StringSplitOptions.None));

            separatedStringDefault.Clear();
            if (setting.DefaultValue != null)
                if (IsStringList)
                    try
                    {
                        separatedStringDefault.AddRange((setting.DefaultValue as IList<string>).Select(s => s.Trim()));
                    }
                    catch
                    {
                        separatedStringDefault.AddRange(setting.DefaultValue.ToString().Split(new string[] { separator }, StringSplitOptions.None).Select(s => s.Trim()));
                    }
                else
                    separatedStringDefault.AddRange(setting.DefaultValue.ToString().Split(new string[] { separator }, StringSplitOptions.None).Select(s => s.Trim()));
        }

        private void InitializeWindow()
        {
            ClearCache();
            listEnum = null;
            _enumContent = null;
            listIndex = -1;
            drawerFunction = null;
            valueToSet = setting.SettingType == typeof(Color) ? Utilities.Utils.RoundColorToHEX((Color)setting.Get()) : setting.Get();
            _externalText = (setting as ExternalSettingEntry)?.FormatValue(valueToSet);
            _externalTextEdited = false;
            errorText = string.Empty;
            errorOnSetting = string.Empty;
            colorAsHEX = setting.SettingType == typeof(Color) ? $"#{ColorUtility.ToHtmlStringRGBA((Color)valueToSet)}" : string.Empty;
            separator = ",";
            separatedString.Clear();
            newItem = string.Empty;
            editStringView = 0;
            _scrollPosition = Vector2.zero;
            _scrollPositionEnum = Vector2.zero;

            dummyCustomDrawerConfigEntry = null;
            if (setting is ConfigSettingEntry newSetting && (setting.CustomDrawer != null || setting.CustomHotkeyDrawer != null))
            {
                // Create dummy entry to not change original config entry
                Type genericType = typeof(ConfigEntry<>).MakeGenericType(newSetting.Entry.SettingType);
                ConstructorInfo constructor = AccessTools.Constructor(genericType, new[] {
                    typeof(ConfigFile),
                    typeof(ConfigDefinition),
                    newSetting.Entry.SettingType,
                    typeof(ConfigDescription)
                });

                dummyCustomDrawerConfigEntry = (ConfigEntryBase)constructor.Invoke(new object[] {
                    newSetting.Entry.ConfigFile,
                    newSetting.Entry.Definition,
                    newSetting.Entry.DefaultValue,
                    newSetting.Entry.Description
                });
                dummyCustomDrawerConfigEntry.BoxedValue = newSetting.Get();
            }

            if (setting.AcceptableValueRange.Key != null)
                drawerFunction = DrawRangeField;
            else if (setting.AcceptableValues != null && setting.AcceptableValues.Length > 0 && setting.SettingType.IsInstanceOfType(setting.AcceptableValues.FirstOrDefault(x => x != null)))
                SetAcceptableValuesDrawer();
            else if (setting.SettingType.IsEnum && setting.SettingType != typeof(KeyCode))
            {
                ExternalEnumCache externalCache = setting is ExternalSettingEntry externalEnum
                    ? SettingFieldDrawer.GetExternalEnumCache(externalEnum) : null;
                if (externalCache?.IsEligible == true)
                {
                    listEnum = externalCache.Values;
                    _enumContent = externalCache.Contents;
                }
                else
                    listEnum = Enum.GetValues(setting.SettingType);
                bool isFlags = externalCache?.IsEligible == true ? externalCache.IsFlags :
                    setting.SettingType.GetCustomAttributes(typeof(FlagsAttribute), false).Any();
                if (isFlags)
                    drawerFunction = DrawFlagsField;
                else
                    drawerFunction = DrawEnumListField;
            }
            else
                SettingDrawHandlers.TryGetValue(setting.SettingType, out drawerFunction);

            if (setting.SettingType == typeof(KeyCode) && listEnum == null)
                listEnum = Enum.GetValues(setting.SettingType);

            // Plain enum labels are immutable. Custom description attributes and
            // other acceptable objects may produce different text on each draw.
            if (_enumContent == null && listEnum != null && setting.SettingType.IsEnum)
            {
                try
                {
                    if (listEnum.Cast<object>().All(HasStaticEnumLabel))
                        _enumContent = listEnum.Cast<object>().Select(SettingFieldDrawer.ObjectToGuiContent).ToArray();
                }
                catch
                {
                    // Keep label/attribute failures inside DrawKnownDrawer's existing
                    // error isolation instead of preventing the editor from opening.
                }
            }

            InitListIndex();

            InitVectorParts();

            if (!(setting is ExternalSettingEntry external && external.UsesTextEditor))
                UpdateStringList();
        }

        private static bool HasStaticEnumLabel(object value)
        {
            if (!(value is Enum)) return false;
            MemberInfo member = value.GetType().GetMember(value.ToString()).FirstOrDefault();
            return member == null || member.GetCustomAttributesData().All(attribute =>
                attribute.AttributeType == typeof(System.ComponentModel.DescriptionAttribute) ||
                !typeof(System.ComponentModel.DescriptionAttribute).IsAssignableFrom(attribute.AttributeType));
        }

        private void InitListIndex()
        {
            listIndex = listEnum == null ? -1 : listEnum.IndexOf(valueToSet);
        }

        private void InitVectorParts()
        {
            vectorParts.Clear();
            vectorFloats.Clear();
            vectorDefault.Clear();

            if (setting.SettingType == typeof(Vector2))
            {
                FillVectorList(vectorFloats, (Vector2)valueToSet);
                FillVectorList(vectorDefault, (Vector2)setting.DefaultValue);
            }
            else if (setting.SettingType == typeof(Vector3))
            {
                FillVectorList(vectorFloats, (Vector3)valueToSet);
                FillVectorList(vectorDefault, (Vector3)setting.DefaultValue);
            }
            else if (setting.SettingType == typeof(Vector4))
            {
                FillVectorList(vectorFloats, (Vector4)valueToSet);
                FillVectorList(vectorDefault, (Vector4)setting.DefaultValue);
            }
            else if (setting.SettingType == typeof(Quaternion))
            {
                FillVectorList(vectorFloats, (Quaternion)valueToSet);
                FillVectorList(vectorDefault, (Quaternion)setting.DefaultValue);
            }

            vectorParts.AddRange(vectorFloats.Select(f => f.ToString()));
        }

        private void SetAcceptableValuesDrawer()
        {
            if (setting.SettingType == typeof(KeyCode))
            {
                listEnum = setting.AcceptableValues.Length > 1 ? setting.AcceptableValues : Enum.GetValues(setting.SettingType);
                drawerFunction = DrawKeyCode;
            }
            else
            {
                listEnum = setting.AcceptableValues;
                drawerFunction = DrawEnumListField;
            }
        }

        internal void SaveCurrentSizeAndPosition()
        {
            var size = new Vector2(Mathf.Clamp(_windowRect.size.x, 200f, instance.ScreenWidth / 2), Mathf.Clamp(_windowRect.size.y, 200f, instance.ScreenHeight * 0.9f));
            var position = new Vector2(Mathf.Clamp(_windowRect.position.x, 0f, instance.ScreenWidth - _windowSize.Value.x / 4f), Mathf.Clamp(_windowRect.position.y, 0f, instance.ScreenHeight - HeaderSize * 2));
            SaveWindowConfigChanges(instance.Config, () =>
            {
                _windowSizeEditSetting.Value = size;
                _windowPositionEditSetting.Value = position;
            });
            _windowRect = new Rect(position, size);
            _windowGeometryDirty = false;
            _windowGeometrySaveFailed = false;
            _pendingWindowSize = null;
        }

        private void DrawWindow(int windowID)
        {
            _shortcutCaptureDrawn = false;
            SettingFieldDrawer.RangeTextEditor.BeginWindowEvent();
            try
            {
                DrawWindowContents(windowID);
            }
            finally
            {
                try { FinishUndrawnRangeText(); }
                finally
                {
                    if (IsRecordingKeyboardShortcut && !_shortcutCaptureDrawn)
                        CancelKeyboardShortcutCapture();
                    SettingFieldDrawer.RangeTextEditor.EndWindowEvent();
                }
            }
        }

        private void FinishUndrawnRangeText()
        {
            if (rangeTextEditor.FinishIfUndrawn(out string text)) ApplyRangeText(text);
        }

        private void ApplyRangeText(string text)
        {
            if (setting == null || setting.ReadOnly == true || setting.AcceptableValueRange.Key == null ||
                setting is ConfigSettingEntry config && PathOfValheimanCompatibility.IsLocked(config)) return;
            float min = (float)Convert.ToDouble(setting.AcceptableValueRange.Key, CultureInfo.InvariantCulture);
            float max = (float)Convert.ToDouble(setting.AcceptableValueRange.Value, CultureInfo.InvariantCulture);
            if (SettingFieldDrawer.TryParseRangeText(text, min, max, setting.SettingType, out object parsed))
                valueToSet = parsed;
        }

        private void DrawWindowContents(int windowID)
        {
            var backgroundColor = GUI.backgroundColor;
            GUI.backgroundColor = Palette.EntryBackground;

            GUILayout.BeginVertical(GetSettingWindowBackgroundStyle());

            GUILayout.Space(1f);

            GUILayout.Label($"<b>{setting.Category}</b>", GetLabelStyle(), GUILayout.ExpandWidth(true));

            DrawDelimiterLine();

            GUILayout.Space(1f);

            GUILayout.BeginHorizontal(GUILayout.ExpandHeight(false));
            GUILayout.Label($"{setting.DispName} ", GetLabelStyle(), GUILayout.ExpandWidth(false));
            GUILayout.Label($"({GetTypeRepresentation(setting.SettingType)})", GetLabelStyleInfo(), GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            GUILayout.Label(setting.Description, GetLabelStyle(isDefaultValue: false), GUILayout.ExpandWidth(true));

            if (setting.DefaultValue != null)
            {
                var style = GetLabelStyle();
                var content = new GUIContent(UiText.DefaultValueDescriptionEditWindow);
                float width = style.CalcSize(content).x + 3f;

                GUILayout.BeginHorizontal(GUILayout.ExpandHeight(false));
                GUILayout.Label(UiText.DefaultValueDescriptionEditWindow, style, GUILayout.Width(width));
                GUIContent defaultContent = setting.DefaultValue is KeyboardShortcut shortcut
                    ? SettingFieldDrawer.GetKeyboardShortcutContent(shortcut)
                    : new GUIContent(GetValueRepresentation(setting.DefaultValue, setting.SettingType));
                GUILayout.Label(defaultContent, GetLabelStyleInfo(), GUILayout.ExpandWidth(true));
                GUILayout.EndHorizontal();
            }

            DrawDelimiterLine();

            GUILayout.Space(5f);

            bool povLocked = setting is ConfigSettingEntry config && PathOfValheimanCompatibility.IsLocked(config);
            var guiEnabled = GUI.enabled;
            try
            {
                if (povLocked)
                {
                    GUI.enabled = false;
                    object lockedValue = setting.Get();
                    GUIContent lockedContent = lockedValue is KeyboardShortcut lockedShortcut
                        ? SettingFieldDrawer.GetKeyboardShortcutContent(lockedShortcut)
                        : new GUIContent(lockedValue?.ToString() ?? string.Empty);
                    GUILayout.Label(lockedContent, GetLabelStyle());
                }
                else DrawSettingValue();
            }
            finally
            {
                if (povLocked || !Utilities.ComboBox.IsShown()) GUI.enabled = guiEnabled;
            }

            if (!errorOnSetting.IsNullOrWhiteSpace())
                GUILayout.Label(errorOnSetting, GetLabelStyle());
            else if (setting is ExternalSettingEntry external && !string.IsNullOrEmpty(external.ErrorText))
                GUILayout.Label(external.ErrorText, GetLabelStyle());

            DrawDelimiterLine();

            GUILayout.Space(1f);

            DrawMenuButtons();

            GUILayout.EndVertical();

            GUI.backgroundColor = backgroundColor;

            GUI.DragWindow(new Rect(0, 0, _windowRect.width, HeaderSize));

            if (!SettingFieldDrawer.DrawCurrentDropdown())
                DrawTooltip(_windowRect);

            _windowRect = Utilities.Utils.ResizeWindow(windowID, _windowRect, out bool sizeChanged);

            if (sizeChanged)
            {
                _windowRect.size = new Vector2(Mathf.Clamp(_windowRect.width, 200f, instance.ScreenWidth / 2),
                    Mathf.Clamp(_windowRect.height, 200f, instance.ScreenHeight * 0.9f));
                _pendingWindowSize = _windowRect.size;
                _windowGeometryDirty = true;
            }
        }

        private void DrawLabel(string label, string value)
        {
            bool drawAsBlock = !label.IsNullOrWhiteSpace() && !value.IsNullOrWhiteSpace();
            if (drawAsBlock)
                GUILayout.BeginHorizontal();

            if (!label.IsNullOrWhiteSpace())
                GUILayout.Label(value.IsNullOrWhiteSpace() ? label : label + ":", GetLabelStyle(), GUILayout.ExpandWidth(false));

            if (!value.IsNullOrWhiteSpace())
                GUILayout.Label(value, GetLabelStyleInfo(), GUILayout.ExpandWidth(true));

            if (drawAsBlock)
                GUILayout.EndHorizontal();
        }

        private void DrawInfo(string info) => DrawLabel(null, info);

        private static readonly Dictionary<Type, string> typeMappings = new Dictionary<Type, string>
        {
            { typeof(int), "Integer" },
            { typeof(float), "Float" },
            { typeof(double), "Double" },
            { typeof(decimal), "Decimal" },
            { typeof(bool), "Boolean" },
            { typeof(string), "String" },
            { typeof(long), "Long" },
            { typeof(short), "Short" },
            { typeof(byte), "Byte" },
            { typeof(sbyte), "Signed Byte" },
            { typeof(uint), "Unsigned Integer" },
            { typeof(ulong), "Unsigned Long" },
            { typeof(ushort), "Unsigned Short" },
            { typeof(char), "Character" },
            { typeof(DateTime), "DateTime" },
            { typeof(TimeSpan), "TimeSpan" },
            { typeof(Guid), "GUID" },
            { typeof(KeyValuePair<,>), "Map<Key, Value>" },
            { typeof(object), "Object" },
        };

        private string GetTypeRepresentation(Type type)
        {
            if (!type.IsGenericType)
                return typeMappings.TryGetValue(type, out string friendlyName) ? friendlyName : type.Name;

            Type[] genericArgs = type.GetGenericArguments();
            string elements = string.Join(", ", genericArgs.Select(t => typeMappings.TryGetValue(t, out string name) ? name : t.Name));
            return $"{typeMappings.GetValueOrDefaultPiktiv(type, type.Name)}<{elements}>";
        }

        private string GetValueRepresentation(object value, Type type)
        {
            if (type == typeof(Color))
                return $"#{ColorUtility.ToHtmlStringRGBA((Color)value)}";
            else if (type == typeof(bool))
                return (bool)value ? UiText.EnabledText : UiText.DisabledText;

            return value.ToString();
        }

        private void DrawMenuButtons()
        {
            // A hidden/changed drawer must not leave an older pending value behind Apply.
            FinishUndrawnRangeText();
            ExternalSettingEntry external = setting as ExternalSettingEntry;
            bool externalText = external != null && external.UsesTextEditor;
            bool povLocked = setting is ConfigSettingEntry config && PathOfValheimanCompatibility.IsLocked(config);
            GUILayout.BeginHorizontal();
            {
                var enabled = GUI.enabled;
                GUI.enabled = enabled && !povLocked && !IsPendingValueEqualTo(setting.DefaultValue);
                if (externalText)
                {
                    if (GUILayout.Button(UiText.ResetSettingText, GetButtonStyle(), GUILayout.ExpandWidth(false)))
                        ResetExternalValue();
                }
                else DrawDefaultButton();
                GUI.enabled = enabled;

                GUILayout.Label(UiText.PressEscapeHintEditWindow, GetLabelStyleInfo(), GUILayout.ExpandWidth(true));

                enabled = GUI.enabled;
                GUI.enabled = enabled && !povLocked && !IsPendingValueEqualTo(setting.Get());
                if (GUILayout.Button(UiText.ApplyButtonEditWindow, GetButtonStyle(), GUILayout.ExpandWidth(false)))
                    ApplySettingValue();

                GUI.enabled = enabled;

                if (GUILayout.Button(UiText.CloseText, GetButtonStyle(), GUILayout.ExpandWidth(false)))
                    IsOpen = false;
            }
            GUILayout.EndHorizontal();
        }

        private void ApplySettingValue()
        {
            if (setting is ExternalSettingEntry readOnly && readOnly.ReadOnly == true) return;
            if (setting is ConfigSettingEntry config && PathOfValheimanCompatibility.IsLocked(config)) return;
            if (rangeTextEditor.FinishOnWindowClose(out string pendingRange)) ApplyRangeText(pendingRange);
            if (setting is ExternalSettingEntry external && external.UsesTextEditor)
            {
                // Keep the typed initial/reset value until the user actually edits
                // the text, so a null default is not converted to an empty string.
                bool applied = _externalTextEdited ? external.TrySetText(_externalText) : external.TrySet(valueToSet);
                if (applied) InitializeWindow();
                else errorOnSetting = external.ErrorText;
                return;
            }
            if (valueToSet != null)
            {
                try
                {
                    setting.Set(valueToSet);
                    if (setting is ExternalSettingEntry externalSetting && !string.IsNullOrEmpty(externalSetting.ErrorText))
                    {
                        errorOnSetting = externalSetting.ErrorText;
                        return;
                    }
                    InitializeWindow();
                }
                catch (Exception e)
                {
                    errorOnSetting = e.ToString();
                }
            }
        }

        private void ResetExternalValue()
        {
            if (setting.ReadOnly == true) return;
            valueToSet = setting.DefaultValue;
            _externalText = ((ExternalSettingEntry)setting).FormatValue(valueToSet);
            _externalTextEdited = false;
            errorOnSetting = string.Empty;
        }

        private void SetExternalText(string text)
        {
            if (string.Equals(_externalText, text, StringComparison.Ordinal)) return;
            _externalText = text;
            _externalTextEdited = true;
        }

        private bool IsPendingValueEqualTo(object value)
        {
            if (setting is ExternalSettingEntry external && external.UsesTextEditor && _externalTextEdited)
                return string.Equals(_externalText, setting.SettingType == typeof(string)
                    ? (string)value : external.FormatValue(value), StringComparison.Ordinal);

            if (valueToSet == null || value == null) return valueToSet == value;
            return setting.SettingType == typeof(string)
                ? string.Equals((string)valueToSet, (string)value, StringComparison.Ordinal)
                : IsEqualConfigValues(setting.SettingType, valueToSet, value);
        }

        private void ChangeStringView(int view)
        {
            if (editStringView == view) return;
            // The text editor changes valueToSet; refresh the list before it can
            // overwrite that value with rows from the previous visit.
            if (view > 0) UpdateStringList();
            editStringView = view;
        }

        private void UpdateValueFromStringList() => valueToSet = setting.StrToObj(string.Join(separator, separatedString));

        private void DrawSettingValue()
        {
            var color = GUI.backgroundColor;
            GUI.backgroundColor = Palette.Widget;

            bool drawStringMenu = false;
            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);
            {
                if (setting is ExternalSettingEntry external && external.UsesTextEditor)
                {
                    SetExternalText(setting.SettingType == typeof(string)
                        ? GUILayout.TextArea(_externalText ?? string.Empty, GUILayout.MinHeight(100))
                        : GUILayout.TextField(_externalText ?? string.Empty));
                }
                else if (!DrawCustomField() && !DrawKnownDrawer())
                {
                    if (errorText.Length > 0)
                        GUILayout.Label($"Error:\n{errorText}", GetLabelStyle());

                    DrawUnknownField(out drawStringMenu);
                }
            }
            GUILayout.EndScrollView();

            if (drawStringMenu)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(UiText.EditAsLabelEditWindow, GetLabelStyle(), GUILayout.ExpandWidth(false));
                ChangeStringView(GUILayout.SelectionGrid(editStringView, new[] { UiText.EditAsTextEditWindow, UiText.EditAsListEditWindow }, 2, GetButtonStyle(), GUILayout.ExpandWidth(false)));

                if (editStringView > 0)
                {
                    GUILayout.Label(UiText.SeparatorLabelEditWindow, GetLabelStyle(), GUILayout.ExpandWidth(false));
                    if (separator != (separator = GUILayout.TextField(separator)))
                        UpdateStringList();

                    if (GUILayout.Button(UiText.TrimWhitespaceButtonEditWindow, GetButtonStyle(), GUILayout.ExpandWidth(false)))
                    {
                        separatedString = separatedString.Select(s => s.Trim()).ToList();
                        UpdateValueFromStringList();
                    }
                }
                GUILayout.EndHorizontal();
            }

            GUI.backgroundColor = color;
        }

        private static void DrawDelimiterLine()
        {
            var color = GUI.backgroundColor;
            GUI.backgroundColor = Palette.Widget;
            GUILayout.Label("", GetDelimiterLine(), GUILayout.ExpandWidth(true), GUILayout.Height(2));
            GUI.backgroundColor = color;
        }

        private bool DrawKnownDrawer()
        {
            if (drawerFunction == null)
                return false;

            try
            {
                drawerFunction();
                return true;
            }
            catch (Exception e)
            {
                LogWarning(e);
                errorText = $"{e.GetType().Name} - {e.Message}";
            }
            return false;
        }

        public bool DrawCustomField()
        {
            if (setting.CustomDrawer == null && setting.CustomHotkeyDrawer == null)
                return false;
            if (SettingFieldDrawer.IsSettingFailedToCustomDraw(setting))
            {
                GUILayout.Label("Error when calling custom drawer function.");
                return false;
            }

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

            GUILayout.BeginHorizontal();

            // Some plugins rely on this field to limit width
            int rightColumn = instance.RightColumnWidth;
            instance.SetRightColumnWidth(Mathf.RoundToInt(_windowRect.width * 0.9f));

            try
            {
                GUI.contentColor = IsValueToSetDefaultValue() ? Palette.DefaultValueText : Palette.ChangedValueText;

                if (setting.CustomDrawer != null)
                    setting.CustomDrawer(dummyCustomDrawerConfigEntry);
                else if (setting.CustomHotkeyDrawer != null)
                {
                    var isBeingSet = _currentKeyboardShortcutToSet == setting;
                    var isBeingSetOriginal = isBeingSet;
                    setting.CustomHotkeyDrawer(dummyCustomDrawerConfigEntry, ref isBeingSet);

                    if (isBeingSet != isBeingSetOriginal)
                        _currentKeyboardShortcutToSet = isBeingSet ? setting : null;
                }
                else
                    result = false;
            }
            catch (Exception e)
            {
                SettingFieldDrawer.SetSettingFailedToCustomDraw(setting, e);
                result = false;
            }
            finally
            {
                instance.SetRightColumnWidth(rightColumn);
            }

            GUILayout.EndHorizontal();

            GUI.contentColor = color;
            GUI.skin.textField.fontSize = textFieldFontSize;
            GUI.skin.textArea.fontSize = textAreaFontSize;
            GUI.skin.label.fontSize = labelFontSize;
            GUI.skin.button.fontSize = buttonFontSize;

            if (result && dummyCustomDrawerConfigEntry != null)
                valueToSet = dummyCustomDrawerConfigEntry.BoxedValue;

            return result;
        }

        private void DrawRangeField()
        {
            var value = valueToSet;
            var converted = (float)Convert.ToDouble(value, CultureInfo.InvariantCulture);
            var leftValue = (float)Convert.ToDouble(setting.AcceptableValueRange.Key, CultureInfo.InvariantCulture);
            var rightValue = (float)Convert.ToDouble(setting.AcceptableValueRange.Value, CultureInfo.InvariantCulture);

            float height = GetTextStyle(setting).CalcHeight(new GUIContent(value.ToString()), 100f);

            GUILayout.BeginHorizontal();
            GUILayout.Label(UiText.RangeLabelEditWindow, GetLabelStyle(), GUILayout.ExpandWidth(false));
            GUILayout.Label($"{leftValue} - {rightValue}", GetLabelStyleInfo(), GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(GUILayout.Height(height));

            try
            {
                float result = DrawCenteredHorizontalSlider(converted, leftValue, rightValue, height);

                if (Math.Abs(result - converted) >= Mathf.Abs(rightValue - leftValue) / Math.Pow(10, NumericPrecision + 2))
                {
                    rangeTextEditor.Reset(releaseFocus: true);
                    valueToSet = Convert.ChangeType(Utilities.Utils.RoundWithPrecision(result, NumericPrecision), setting.SettingType, CultureInfo.InvariantCulture);
                    value = valueToSet;
                }

                if (setting.ShowRangeAsPercent == true)
                {
                    SettingFieldDrawer.DrawCenteredLabel($"{Mathf.Abs(result - leftValue) / Mathf.Abs(rightValue - leftValue):P0}", GetLabelStyle(setting));
                }
                else
                {
                    string strVal = Convert.ToString(value, CultureInfo.InvariantCulture);
                    if (rangeTextEditor.Draw(strVal, GetTextStyle(setting), 50f, out string text))
                        ApplyRangeText(text);
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
            GUILayout.Space(height * 0.35f);
            var result = GUILayout.HorizontalSlider(converted, leftValue, rightValue, GetSliderStyle(), GetThumbStyle(), GUILayout.ExpandWidth(true), GUILayout.Height(height));
            GUILayout.EndVertical();
            return result;
        }

        private void DrawUnknownField(out bool drawStringMenu)
        {
            drawStringMenu = false;

            // Try to use user-supplied converters
            if (setting.ObjToStr != null && setting.StrToObj != null)
            {
                string text = setting.ObjToStr(valueToSet).AppendZeroIfFloat(setting.SettingType);

                if (setting.SettingType == typeof(string) || IsStringList)
                {
                    if (editStringView > 0)
                    {
                        DrawEditableList();
                        UpdateValueFromStringList();
                        GUILayout.FlexibleSpace();
                    }
                    else
                    {
                        string result = GUILayout.TextArea(text, GetTextStyle(IsValueToSetDefaultValue()), GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)).AppendZeroIfFloat(setting.SettingType);
                        if (result != text)
                            valueToSet = setting.StrToObj(result);
                    }
                    drawStringMenu = true;
                }
                else
                {
                    string result = GUILayout.TextArea(text, GetTextStyle(IsValueToSetDefaultValue()), GUILayout.ExpandWidth(true)).AppendZeroIfFloat(setting.SettingType);
                    if (result != text)
                        valueToSet = setting.StrToObj(result);
                }
            }
            else
            {
                // Fall back to slow/less reliable method
                var value = valueToSet == null ? "NULL" : Convert.ToString(valueToSet, CultureInfo.InvariantCulture).AppendZeroIfFloat(setting.SettingType);

                if (CanCovert(value, setting.SettingType))
                {
                    var result = GUILayout.TextArea(value, GetTextStyle(IsValueToSetDefaultValue()), GUILayout.ExpandWidth(true)).AppendZeroIfFloat(setting.SettingType);
                    if (result != value)
                        try
                        {
                            valueToSet = Convert.ChangeType(result, setting.SettingType, CultureInfo.InvariantCulture);
                        }
                        catch
                        {
                            // Don't change anything if format is bad
                        }
                }
                else
                {
                    valueToSet = GUILayout.TextArea(value, GetTextStyle(IsValueToSetDefaultValue()), GUILayout.ExpandWidth(true)).AppendZeroIfFloat(setting.SettingType);
                }
            }
        }

        private void DrawEditableList()
        {
            float width = Mathf.Round(GetButtonStyle().CalcSize(new GUIContent("▲")).x);

            for (int i = 0; i < separatedString.Count; i++)
            {
                GUILayout.BeginHorizontal();

                if (GUILayout.Button("✕", GetButtonStyle(), GUILayout.Width(width)))
                {
                    GUILayout.EndHorizontal();
                    separatedString.RemoveAt(i);
                    break;
                }

                separatedString[i] = GUILayout.TextArea(separatedString[i], GetTextStyle(separatedStringDefault.IndexOf(separatedString[i].Trim()) == i), GUILayout.ExpandWidth(true));

                var enabled = GUI.enabled;
                GUI.enabled = i > 0;
                if (GUILayout.Button("▲", GetButtonStyle(), GUILayout.Width(width)))
                    SwapElements(i, i - 1);

                GUI.enabled = i < separatedString.Count - 1;
                if (GUILayout.Button("▼", GetButtonStyle(), GUILayout.Width(width)))
                    SwapElements(i, i + 1);

                GUI.enabled = enabled;

                GUILayout.EndHorizontal();
            }

            GUILayout.BeginHorizontal();

            GUI.SetNextControlName(NewItemFieldControlName);
            newItem = GUILayout.TextField(newItem, GetTextStyle(isDefaultValue:false), GUILayout.ExpandWidth(true));

            if (string.IsNullOrEmpty(newItem) && Event.current.type == EventType.Repaint)
                GUI.Label(GUILayoutUtility.GetLastRect(), UiText.NewValuePlaceholderEditWindow, GetPlaceholderTextStyle());

            if (GUILayout.Button(UiText.AddButtonEditWindow, GetButtonStyle(), GUILayout.ExpandWidth(false)) && !string.IsNullOrWhiteSpace(newItem))
            {
                separatedString.Add(newItem);
                newItem = "";
                GUI.FocusControl(NewItemFieldControlName);
            }

            GUILayout.EndHorizontal();

            void SwapElements(int indexA, int indexB) => (separatedString[indexA], separatedString[indexB]) = (separatedString[indexB], separatedString[indexA]);
        }

        private bool IsValueToSetDefaultValue() => IsEqualConfigValues(setting.SettingType, valueToSet, setting.DefaultValue);

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

        public void ClearCache()
        {
            CancelKeyboardShortcutCapture();
            rangeTextEditor.Reset();
            _currentKeyboardShortcutToSet = null;
            if (_colorPreview == null) return;
            UnityEngine.Object.Destroy(_colorPreview.Tex);
            _colorPreview = null;
        }

        internal void DrawDefaultButton()
        {
            if (setting.HideDefaultButton) return;

            var color = GUI.backgroundColor;
            GUI.backgroundColor = Palette.Widget;

            bool DrawResetButton()
            {
                GUILayout.Space(5);
                return GUILayout.Button(UiText.ResetSettingText, GetButtonStyle(), GUILayout.ExpandWidth(false));
            }

            if (setting.DefaultValue != null)
            {
                if (DrawResetButton())
                {
                    if (setting.SettingType == typeof(Color))
                    {
                        valueToSet = Utilities.Utils.RoundColorToHEX((Color)setting.DefaultValue);
                        colorAsHEX = $"#{ColorUtility.ToHtmlStringRGBA((Color)valueToSet)}";
                    }
                    else
                    {
                        valueToSet = setting.DefaultValue;
                    }

                    if (dummyCustomDrawerConfigEntry != null)
                        dummyCustomDrawerConfigEntry.BoxedValue = setting.DefaultValue;
                    InitListIndex();
                    InitVectorParts();
                    UpdateStringList();
                    ClearCache();
                }
            }
            else if (setting.SettingType.IsClass)
            {
                if (DrawResetButton())
                {
                    valueToSet = null;
                    if (dummyCustomDrawerConfigEntry != null)
                        dummyCustomDrawerConfigEntry.BoxedValue = null;
                    InitListIndex();
                    InitVectorParts();
                    UpdateStringList();
                    ClearCache();
                }
            }

            GUI.backgroundColor = color;
        }

        private void DrawBoolField()
        {
            GUI.backgroundColor = Palette.Widget;
            bool boolVal = (bool)valueToSet;

            Color color = GUI.backgroundColor;
            if (boolVal)
                GUI.backgroundColor = Palette.Enabled;

            bool result = GUILayout.SelectionGrid(boolVal ? 0 : 1, new[] { UiText.EnabledText, UiText.DisabledText }, 2, GetButtonStyle(), GUILayout.ExpandWidth(false)) == 0;
            if (result != boolVal)
                valueToSet = result;

            if (boolVal)
                GUI.backgroundColor = color;
        }

        private void DrawFlagsField()
        {
            var currentValue = Convert.ToInt64(valueToSet);
            var defaultValue = Convert.ToInt64(setting.DefaultValue);

            ExternalEnumCache externalCache = GetExternalFlagsCache();
            var allValues = externalCache == null
                ? Enum.GetValues(setting.SettingType).Cast<Enum>().Select(x => new { name = x.ToString(), val = Convert.ToInt64(x) }).ToArray()
                : null;
            int count = externalCache?.FlagMasks.Length ?? allValues.Length;

            float maxWidth = _windowRect.width * 0.8f;

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

                                GUIStyle style = GetButtonStyle(curr == defValue);

                                // Make sure this horizontal group doesn't extend over window width, if it does then start a new horiz group below
                                var textDimension = (int)style.CalcSize(content).x;
                                if (currentWidth + textDimension > maxWidth && drewAny)
                                    break;

                                drewAny = true;
                                currentWidth += textDimension;
                                GUI.changed = false;

                                if (GUILayout.Button(content, style, GUILayout.ExpandWidth(false)))
                                    curr = !curr;

                                if (GUI.changed)
                                {
                                    if (externalCache != null) SetExternalFlag(index, curr);
                                    else
                                    {
                                        var newValue = curr ? currentValue | mask : currentValue & ~mask;
                                        valueToSet = Enum.ToObject(setting.SettingType, newValue);
                                    }
                                }
                            }
                        }

                        if (!drewAny)
                            index++;
                    }
                    GUILayout.EndHorizontal();
                }

                GUI.changed = false;
            }
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
        }

        private ExternalEnumCache GetExternalFlagsCache()
        {
            if (!(setting is ExternalSettingEntry external)) return null;
            ExternalEnumCache cache = SettingFieldDrawer.GetExternalEnumCache(external);
            return cache.IsEligible && cache.IsFlags ? cache : null;
        }

        private void SetExternalFlag(int index, bool enabled)
            => valueToSet = GetExternalFlagsCache().ChangeFlag(valueToSet, index, enabled);

        private void DrawEnumListField()
        {
            var listContent = _enumContent ?? listEnum.Cast<object>().Select(SettingFieldDrawer.ObjectToGuiContent).ToArray();

            _scrollPositionEnum = GUILayout.BeginScrollView(_scrollPositionEnum, false, false);

            try
            {
                listIndex = GUILayout.SelectionGrid(listIndex, listContent, 1, GetComboBoxStyle());
                if (listEnum != null && listIndex >= 0 && listIndex < listEnum.Count)
                    valueToSet = listEnum[listIndex];
            }
            finally
            {
                GUILayout.EndScrollView();
            }

            GUILayout.FlexibleSpace();
        }

        private void DrawKeyCode()
        {
            if (ReferenceEquals(_currentKeyboardShortcutToSet, setting))
            {
                GUILayout.Label(UiText.SingleKeyCaptureText, GetLabelStyle(), GUILayout.ExpandWidth(true));
                GUIUtility.keyboardControl = -1;

                _keysToCheck ??= UnityInput.Current.SupportedKeyCodes.Except(new[] { KeyCode.Mouse0, KeyCode.None }).ToArray();
                foreach (var key in _keysToCheck)
                {
                    if (UnityInput.Current.GetKeyUp(key))
                    {
                        valueToSet = key;
                        _currentKeyboardShortcutToSet = null;
                        break;
                    }
                }

                if (GUILayout.Button(UiText.CancelText, GetButtonStyle(), GUILayout.ExpandWidth(false)))
                    _currentKeyboardShortcutToSet = null;
            }
            else
            {
                DrawEnumListField();

                if (GUILayout.Button(new GUIContent(UiText.ShortcutKeyText), GetButtonStyle(), GUILayout.ExpandWidth(false)))
                    BeginKeyCodeCapture();
            }
        }

        internal bool IsRecordingKeyboardShortcut => _shortcutCapture?.IsActive == true;

        internal void BeginKeyboardShortcutCapture(int frame)
        {
            if (!IsOpen || !SettingFieldDrawer.CanCaptureShortcut(setting)) return;
            instance?.CancelBuiltInKeyRecordings();
            CancelBuiltInKeyRecording();
            _capturedShortcutSetting = setting;
            _currentKeyboardShortcutToSet = setting;
            _shortcutCapture ??= new SettingFieldDrawer.ShortcutCaptureState();
            _shortcutCapture.Begin(frame);
            _shortcutCaptureDrawn = true;
        }

        internal void BeginKeyCodeCapture()
        {
            if (!IsOpen || !SettingFieldDrawer.HasBuiltInKeyRecorder(setting) || setting.SettingType != typeof(KeyCode)) return;
            instance?.CancelBuiltInKeyRecordings();
            CancelBuiltInKeyRecording();
            _currentKeyboardShortcutToSet = setting;
        }

        internal void CancelBuiltInKeyRecording()
        {
            CancelKeyboardShortcutCapture();
            if (SettingFieldDrawer.HasBuiltInKeyRecorder(_currentKeyboardShortcutToSet)) _currentKeyboardShortcutToSet = null;
        }

        internal void CancelKeyboardShortcutCapture()
        {
            if (_capturedShortcutSetting != null && ReferenceEquals(_currentKeyboardShortcutToSet, _capturedShortcutSetting))
                _currentKeyboardShortcutToSet = null;
            _capturedShortcutSetting = null;
            _shortcutCapture?.Cancel();
            _shortcutCaptureDrawn = false;
        }

        internal void UpdateKeyboardShortcutCapture()
        {
            if (!IsRecordingKeyboardShortcut) return;
            if (!IsOpen || !ReferenceEquals(_capturedShortcutSetting, setting) ||
                !ReferenceEquals(_currentKeyboardShortcutToSet, setting) || !SettingFieldDrawer.CanCaptureShortcut(setting))
            {
                CancelKeyboardShortcutCapture();
                return;
            }
            if (SettingFieldDrawer.PollShortcutCapture(_shortcutCapture, out KeyboardShortcut shortcut))
                CompleteKeyboardShortcutCapture(shortcut);
        }

        internal void CompleteKeyboardShortcutCapture(KeyboardShortcut shortcut)
        {
            bool canApply = IsOpen && _capturedShortcutSetting != null &&
                ReferenceEquals(_capturedShortcutSetting, setting) &&
                ReferenceEquals(_currentKeyboardShortcutToSet, setting) && SettingFieldDrawer.CanCaptureShortcut(setting);
            CancelKeyboardShortcutCapture();
            if (canApply) valueToSet = shortcut; // Only the existing Apply action writes the setting.
        }

        private void DrawKeyboardShortcut()
        {
            if (IsRecordingKeyboardShortcut && ReferenceEquals(_capturedShortcutSetting, setting))
            {
                _shortcutCaptureDrawn = true;
                GUILayout.Label(_shortcutCapture?.NeedsRetry == true ? UiText.ShortcutRetryText : UiText.ShortcutKeysText,
                    GetLabelStyleSettingName(), GUILayout.ExpandWidth(true));
                GUIUtility.keyboardControl = -1;

                if (GUILayout.Button(UiText.CancelText, GetButtonStyle(), GUILayout.ExpandWidth(false)))
                    CancelKeyboardShortcutCapture();
            }
            else
            {
                KeyboardShortcut shortcut = (KeyboardShortcut)valueToSet;
                if (GUILayout.Button(SettingFieldDrawer.GetKeyboardShortcutContent(shortcut), GetButtonStyle(setting), GUILayout.ExpandWidth(true)))
                    BeginKeyboardShortcutCapture(Time.frameCount);

                if (GUILayout.Button(UiText.ClearText, GetButtonStyle(), GUILayout.ExpandWidth(false)))
                {
                    CancelKeyboardShortcutCapture();
                    valueToSet = KeyboardShortcut.Empty;
                    _currentKeyboardShortcutToSet = null;
                }
            }
        }

        private void DrawVectorPart(int position)
        {
            string label = position switch
            {
                0 => "X",
                1 => "Y",
                2 => "Z",
                3 => "W",
                _ => ""
            };

            bool isDefaultValue = Utilities.Utils.TryParseFloat(vectorParts[position], out var x) && vectorDefault[position] == x;

            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label} ", GetLabelStyle(), GUILayout.ExpandWidth(false));
            vectorParts[position] = GUILayout.TextField(vectorParts[position], GetTextStyle(isDefaultValue), GUILayout.ExpandWidth(true)).KeepDigitsAndFirstDot();
            GUILayout.EndHorizontal();
        }

        private void DrawVector()
        {
            for (int i = 0; i < vectorParts.Count; i++)
                DrawVectorPart(i);

            for (int i = 0; i < vectorParts.Count; i++)
                if (Utilities.Utils.TryParseFloat(vectorParts[i], out var x))
                    vectorFloats[i] = x;

            if (setting.SettingType == typeof(Vector2))
                valueToSet = new Vector2(vectorFloats[0], vectorFloats[1]);
            else if (setting.SettingType == typeof(Vector3))
                valueToSet = new Vector3(vectorFloats[0], vectorFloats[1], vectorFloats[2]);
            else if (setting.SettingType == typeof(Vector4))
                valueToSet = new Vector4(vectorFloats[0], vectorFloats[1], vectorFloats[2], vectorFloats[3]);
            else if (setting.SettingType == typeof(Quaternion))
                valueToSet = new Quaternion(vectorFloats[0], vectorFloats[1], vectorFloats[2], vectorFloats[3]);

            GUILayout.FlexibleSpace();
        }

        private void DrawColor()
        {
            Color value = (Color)valueToSet;
            Color defaultColor = Utilities.Utils.RoundColorToHEX((Color)setting.DefaultValue);

            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            DrawHexField(ref value, defaultColor);

            GUILayout.Space(3f);
            GUIHelper.BeginColor(value);
            GUILayout.Label(string.Empty, GUILayout.ExpandWidth(true));

            if (_colorPreview == null)
            {
                _colorPreview = new ColorCacheEntry { Tex = new Texture2D(40, 10, TextureFormat.ARGB32, false), Last = value };
                _colorPreview.Tex.FillTexture(value);
            }
            var cacheEntry = _colorPreview;

            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(GUILayoutUtility.GetLastRect(), cacheEntry.Tex);
            }

            GUIHelper.EndColor();
            GUILayout.Space(3f);

            GUILayout.EndHorizontal();

            GUILayout.Space(2f);

            DrawColorField("Red", ref value, ref value.r, Utilities.Utils.RoundColor(value.r) == Utilities.Utils.RoundColor(defaultColor.r));
            DrawColorField("Green", ref value, ref value.g, Utilities.Utils.RoundColor(value.g) == Utilities.Utils.RoundColor(defaultColor.g));
            DrawColorField("Blue", ref value, ref value.b, Utilities.Utils.RoundColor(value.b) == Utilities.Utils.RoundColor(defaultColor.b));
            DrawColorField("Alpha", ref value, ref value.a, Utilities.Utils.RoundColor(value.a) == Utilities.Utils.RoundColor(defaultColor.a));

            HSLColor defaultHSL = defaultColor;
            HSLColor hsl = value;
            DrawHSLField("Hue", ref hsl, ref hsl.h, Utilities.Utils.RoundWithPrecision(hsl.h, 1) == Utilities.Utils.RoundWithPrecision(defaultHSL.h, 1));
            DrawHSLField("Saturation", ref hsl, ref hsl.s, Utilities.Utils.RoundColor(hsl.s) == Utilities.Utils.RoundColor(defaultHSL.s));
            DrawHSLField("Lightness", ref hsl, ref hsl.l, Utilities.Utils.RoundColor(hsl.l) == Utilities.Utils.RoundColor(defaultHSL.l));

            value = hsl;

            if (value != cacheEntry.Last)
            {
                valueToSet = value;
                cacheEntry.Tex.FillTexture(value);
                cacheEntry.Last = value;
                colorAsHEX = $"#{ColorUtility.ToHtmlStringRGBA(value)}";
            }

            GUILayout.EndVertical();
        }

        private bool DrawHexField(ref Color value, Color defaultValue)
        {
            GUIStyle style = GetTextStyle(value, defaultValue);
            Utilities.Utils.UpdateHexString(ref colorAsHEX, GUILayout.TextField(colorAsHEX, style, GUILayout.Width(style.CalcSize(new GUIContent("#CCCCCCCC.")).x), GUILayout.ExpandWidth(false)));

            bool enabled = GUI.enabled;
            GUI.enabled = !colorAsHEX.Replace("#", "").Equals(ColorUtility.ToHtmlStringRGBA(value), StringComparison.OrdinalIgnoreCase);

            if (GUILayout.Button(UiText.ShortcutKeyText, GetButtonStyle(), GUILayout.ExpandWidth(false)) && ColorUtility.TryParseHtmlString(colorAsHEX, out Color color))
                value = color;

            GUI.enabled = enabled;

            return IsEqualColorConfig(value, defaultValue);
        }

        private void DrawColorField(string fieldLabel, ref Color settingColor, ref float settingValue, bool isDefaultValue)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(fieldLabel, GetLabelStyle(), GUILayout.Width(GetLabelStyle().CalcSize(new GUIContent("Green.")).x), GUILayout.ExpandWidth(false));

            GUIStyle style = GetTextStyle(isDefaultValue);
            Vector2 size = style.CalcSize(new GUIContent("0,000."));

            string currentText = Utilities.Utils.RoundWithPrecision(settingValue, 3).ToString("0.000");

            string valueString = GUILayout.TextField(currentText, style, GUILayout.Width(size.x), GUILayout.ExpandWidth(false));
            if (valueString.StartsWith('1'))
                SetColorValue(ref settingColor, 1f);
            else if (valueString.StartsWith('0') && settingValue == 1f)
                SetColorValue(ref settingColor, 0f);
            else if (Utilities.Utils.TryParseFloat(valueString, out float value))
                SetColorValue(ref settingColor, value);

            if (byte.TryParse(GUILayout.TextField((Utilities.Utils.RoundWithPrecision(settingValue, 3) * 255).ToString("F0"), style, GUILayout.Width(style.CalcSize(new GUIContent("000.")).x), GUILayout.ExpandWidth(false)), out byte valueByte))
                SetColorValue(ref settingColor, valueByte / 255f);

            SetColorValue(ref settingColor, DrawCenteredHorizontalSlider(settingValue, 0f, 1f, size.y));

            GUILayout.EndHorizontal();

            void SetColorValue(ref Color color, float value)
            {
                float roundedValue = Utilities.Utils.RoundWithPrecision(value, 3);
                switch (fieldLabel)
                {
                    case "Red":     color.r = Mathf.Clamp01(roundedValue); break;
                    case "Green":   color.g = Mathf.Clamp01(roundedValue); break;
                    case "Blue":    color.b = Mathf.Clamp01(roundedValue); break;
                    case "Alpha":   color.a = Mathf.Clamp01(roundedValue); break;
                }
            }
        }

        private void DrawHSLField(string fieldLabel, ref HSLColor settingColor, ref float settingValue, bool isDefaultValue)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(fieldLabel, GetLabelStyle(), GUILayout.Width(GetLabelStyle().CalcSize(new GUIContent("Saturation..")).x), GUILayout.ExpandWidth(false));

            GUIStyle style = GetTextStyle(isDefaultValue);
            Vector2 size = style.CalcSize(new GUIContent("000,00."));

            string currentText = Utilities.Utils.RoundWithPrecision(settingValue, fieldLabel == "Hue" ? 2 : 4).ToString(fieldLabel == "Hue" ? "000.00" : "0.0000");

            if (Utilities.Utils.TryParseFloat(GUILayout.TextField(currentText, style, GUILayout.Width(size.x), GUILayout.ExpandWidth(false)), out float value))
                SetColorValue(ref settingColor, value);

            SetColorValue(ref settingColor, DrawCenteredHorizontalSlider(settingValue, 0f, fieldLabel == "Hue" ? 360f : 1f, size.y));

            GUILayout.EndHorizontal();

            void SetColorValue(ref HSLColor color, float value)
            {
                float roundedValue = Utilities.Utils.RoundWithPrecision(value, fieldLabel == "Hue" ? 2 : 4);
                switch (fieldLabel)
                {
                    case "Hue":         color.h = Mathf.Clamp(roundedValue, 0f, 360f); break;
                    case "Saturation":  color.s = Mathf.Clamp01(roundedValue > 1f ? roundedValue / 100 : roundedValue); break;
                    case "Lightness":   color.l = Mathf.Clamp01(roundedValue > 1f ? roundedValue / 100 : roundedValue); break;
                }
            }
        }

        private static void FillVectorList<T>(List<float> list, T value)
        {
            if (value == null) return;

            if (value is Vector2 vector2)
            {
                list.Add(vector2.x);
                list.Add(vector2.y);
            }
            else if (value is Vector3 vector3)
            {
                list.Add(vector3.x);
                list.Add(vector3.y);
                list.Add(vector3.z);
            }
            else if (value is Vector4 vector4)
            {
                list.Add(vector4.x);
                list.Add(vector4.y);
                list.Add(vector4.z);
                list.Add(vector4.w);
            }
            else if (value is Quaternion quaternion)
            {
                list.Add(quaternion.x);
                list.Add(quaternion.y);
                list.Add(quaternion.z);
                list.Add(quaternion.w);
            }
        }

        private sealed class ColorCacheEntry
        {
            public Color Last;
            public Texture2D Tex;
        }
    }
}
