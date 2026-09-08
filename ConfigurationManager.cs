// Based on code made by MarC0 / ManlyMarco https://github.com/BepInEx/BepInEx.ConfigurationManager on BepInEx version 5
// Copyright 2018 GNU General Public License v3.0
// Coloring and localization are based on aedenthorn's https://github.com/aedenthorn/BepInEx.ConfigurationManager
// Colors drawer is based on Azumatt https://github.com/AzumattDev/BepInEx.ConfigurationManager

using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace ConfigurationManager
{
    [BepInPlugin(GUID, pluginName, ModVersion)]
    [BepInIncompatibility("com.bepis.bepinex.configurationmanager")]
    [BepInIncompatibility("_shudnal.ConfigurationManager")]
    [BepInIncompatibility("blizz.ConfigManager")]
    public partial class ConfigurationManager : BaseUnityPlugin
    {
        public const string GUID = "sighsorry.ConfigManager";
        public const string pluginName = "ConfigManager";
        public const string ModVersion = "1.0.1";
        public const string Version = ModVersion;

        internal static ConfigurationManager instance;
        private static SettingFieldDrawer _fieldDrawer;

        private const int WindowId = -68;

        private const string SearchBoxName = "searchBox";
        private bool _focusSearchBox;
        private string _searchString = string.Empty;

        /// <summary>
        /// Event fired every time the manager window is shown or hidden.
        /// </summary>
        public event EventHandler<ValueChangedEventArgs<bool>> DisplayingWindowChanged;

        /// <summary>
        /// Disable the hotkey check used by config manager. If enabled you have to set <see cref="DisplayingWindow"/> to show the manager.
        /// </summary>
        public bool OverrideHotkey;

        private bool _displayingWindow;
        private bool _obsoleteCursor;

        private string _modsWithoutSettings;

        private List<SettingEntryBase> _allSettings;
        private long _externalSettingsRevision = -1;
        private List<PluginSettingsData> _filteredSetings = new List<PluginSettingsData>();

        public Rect DefaultWindowRect { get; private set; }
        public Rect currentWindowRect;
        private Vector2 _settingWindowScrollPos;
        private readonly Dictionary<string, Vector2> _settingWindowCategoriesScrollPos = new Dictionary<string, Vector2>();

        #region Compat
        internal Rect SettingWindowRect
        {
            get => currentWindowRect;
            private set
            {
                currentWindowRect = value;
            }
        }
        /// <summary>
        /// Window is visible and is blocking the whole screen. This is true until the user moves the window, which lets it run while user interacts with the game.
        /// </summary>
        public bool IsWindowFullscreen => false;

        /// <summary>
        /// Window scale factor
        /// </summary>
        public float ScaleFactor => GetScreenSizeFactor();

        /// <summary>
        /// Screen width with scale factor
        /// </summary>
        public float ScreenWidth => ScreenSystemWidth / ScaleFactor;

        /// <summary>
        /// Screen height with scale factor
        /// </summary>
        public float ScreenHeight => ScreenSystemHeight / ScaleFactor;

        public static bool isTempWindowUnity6000 = true; // Unity 6000+ runs in temporary ~300×200 window when bepinex is loading mods (on Awake)

        public int ScreenSystemWidth => isTempWindowUnity6000 ? Display.main.systemWidth : Screen.width;

        public int ScreenSystemHeight => isTempWindowUnity6000 ? Display.main.systemHeight : Screen.height;

        #endregion

        private PropertyInfo _curLockState;
        private PropertyInfo _curVisible;
        private int _previousCursorLockState;
        private bool _previousCursorVisible;

        internal static Texture2D WindowBackground { get; private set; }
        internal static Texture2D EntryBackground { get; private set; }
        internal static Texture2D TooltipBackground { get; private set; }
        internal static Texture2D HeaderBackground { get; private set; }
        internal static Texture2D HeaderBackgroundHover { get; private set; }
        internal static Texture2D SettingWindowBackground { get; private set; }

        internal int LeftColumnWidth { get; private set; }
        internal int RightColumnWidth { get; private set; }
        internal int PluginListColumnWidth { get; private set; }
        internal int SettingsListColumnWidth { get; private set; }

        internal void SetRightColumnWidth(int value) => RightColumnWidth = value;

        internal const string GeneralSection = "General";
        internal const string InternalSection = "Internal";

        internal const int GeneralSectionOrder = 500;

        private const bool PluginsInitiallyCollapsed = true;
        private const float SettingLabelWidth = 0.4f;
        private const float SplitViewPluginListWidth = 0.33f;
        internal const int NumericPrecision = 3;

        private static readonly KeyboardShortcut ResetWindowLayoutShortcut =
            new KeyboardShortcut(KeyCode.F1, KeyCode.LeftControl);

        public static ConfigEntry<bool> _showAdvanced;
        public static ConfigEntry<bool> _showKeybinds;
        public static ConfigEntry<bool> _loggingEnabled;

        public static ConfigEntry<KeyboardShortcut> _keybind;
        public static ConfigEntry<int> _textSize;

        public static ConfigEntry<bool> _splitView;
        public static ConfigEntry<Vector2> _windowPosition;
        public static ConfigEntry<Vector2> _windowSize;

        public static ConfigEntry<Vector2> _windowPositionTextEditor;
        public static ConfigEntry<Vector2> _windowSizeTextEditor;
        public static ConfigEntry<bool> _showEmptyFolders;
        public static ConfigEntry<bool> _showTrashBin;
        public static ConfigEntry<bool> _showFullName;

        public static ConfigEntry<int> _textEditorFontSize;
        public static ConfigEntry<bool> _textEditorWordWrap;

        public static ConfigEntry<Vector2> _windowPositionEditSetting;
        public static ConfigEntry<Vector2> _windowSizeEditSetting;

        internal static ThemePalette Palette { get; } = ThemePalette.CreateValheim();

        internal static void LogInfo(object data)
        {
            if (_loggingEnabled?.Value == true)
                instance.Logger.LogInfo(data);
        }

        internal static void LogWarning(object data)
        {
            instance?.Logger.LogWarning(data);
        }

        internal static void LogError(object data)
        {
            instance?.Logger.LogError(data);
        }

        private void Awake()
        {
            instance = this;
            _fieldDrawer = new SettingFieldDrawer(this);

            _keybind = Config.Bind(GeneralSection, "Open manager shortcut", new KeyboardShortcut(KeyCode.F1),
                Describe("Toggle the ConfigManager window.", GeneralSectionOrder, 1000));
            _textSize = Config.Bind(GeneralSection, "Font size", 14,
                Describe("Font size used throughout ConfigManager.", GeneralSectionOrder, 900,
                    new AcceptableValueRange<int>(10, 30)));

            _loggingEnabled = Config.Bind(InternalSection, "Verbose logging", false,
                HiddenDescription("Write diagnostic ConfigManager messages."));
            _splitView = Config.Bind(InternalSection, "Split view", true,
                HiddenDescription("Current layout selected from the window toolbar."));
            _showAdvanced = Config.Bind(InternalSection, "Show advanced entries", false,
                HiddenDescription("Current Advanced filter state."));
            _showKeybinds = Config.Bind(InternalSection, "Keybind-only filter", false,
                HiddenDescription("Current keybind filter state."));
            _showEmptyFolders = Config.Bind(InternalSection, "Show empty folders", false,
                HiddenDescription("Current file editor folder filter."));
            _showTrashBin = Config.Bind(InternalSection, "Show trash bin", true,
                HiddenDescription("Current file editor trash-bin filter."));
            _showFullName = Config.Bind(InternalSection, "Show active file path", true,
                HiddenDescription("Show the complete path of the active file."));
            _textEditorFontSize = Config.Bind(InternalSection, "File editor font size", 14,
                HiddenDescription("Font size selected in the file editor.", new AcceptableValueRange<int>(8, 36)));
            _textEditorWordWrap = Config.Bind(InternalSection, "File editor word wrap", true,
                HiddenDescription("Word-wrap state selected in the file editor."));

            CalculateDefaultWindowRect();

            _windowPosition = Config.Bind(InternalSection, "Manager window position", GetDefaultManagerWindowPosition(),
                HiddenDescription("Main window position."));
            _windowSize = Config.Bind(InternalSection, "Manager window size", GetDefaultManagerWindowSize(),
                HiddenDescription("Main window size."));
            _windowPositionTextEditor = Config.Bind(InternalSection, "File editor window position", GetDefaultTextEditorWindowPosition(),
                HiddenDescription("File editor position."));
            _windowSizeTextEditor = Config.Bind(InternalSection, "File editor window size", GetDefaultTextEditorWindowSize(),
                HiddenDescription("File editor size."));
            _windowPositionEditSetting = Config.Bind(InternalSection, "Setting editor window position", GetDefaultEditSettingWindowPosition(),
                HiddenDescription("Setting editor position."));
            _windowSizeEditSetting = Config.Bind(InternalSection, "Setting editor window size", GetDefaultEditSettingWindowSize(),
                HiddenDescription("Setting editor size."));

            currentWindowRect = new Rect(_windowPosition.Value, _windowSize.Value);

            _configFilesEditor = new ConfigFilesEditor();
            _configSettingWindow = new SettingEditWindow();
        }

        private static ConfigDescription Describe(
            string description,
            int categoryOrder,
            int order,
            AcceptableValueBase acceptableValues = null,
            bool advanced = false,
            bool browsable = true)
        {
            return new ConfigDescription(description, acceptableValues, new ConfigurationManagerAttributes
            {
                CategoryOrder = categoryOrder,
                Order = order,
                IsAdvanced = advanced,
                Browsable = browsable
            });
        }

        private static ConfigDescription HiddenDescription(string description, AcceptableValueBase acceptableValues = null)
        {
            return Describe(description, 0, 0, acceptableValues, browsable: false);
        }

        private Vector2 GetDefaultManagerWindowPosition() => DefaultWindowRect.position;
        private Vector2 GetDefaultManagerWindowSize() => DefaultWindowRect.size;

        private Vector2 GetDefaultTextEditorWindowPosition() => new Vector2(GetDefaultManagerWindowPosition().x + GetDefaultManagerWindowSize().x + 20f, GetDefaultManagerWindowPosition().y);
        private Vector2 GetDefaultTextEditorWindowSize() => new Vector2(ScreenSystemWidth - GetDefaultTextEditorWindowPosition().x - GetDefaultManagerWindowPosition().x, GetDefaultManagerWindowSize().y + GetDefaultManagerWindowPosition().y);

        private Vector2 GetDefaultEditSettingWindowSize() => new Vector2(500f, 500f);
        private Vector2 GetDefaultEditSettingWindowPosition() => new Vector2(GetDefaultManagerWindowPosition().x + GetDefaultManagerWindowSize().x + 10f, GetDefaultManagerWindowPosition().y + (GetDefaultManagerWindowSize().y - GetDefaultEditSettingWindowSize().y) / 2);

        void OnDestroy()
        {
            RemoveOwnedMenuButtons();
            _configFilesEditor?.Dispose();
            _configSettingWindow?.Dispose();
            _configSettingWindow = null;
            _allSettings = null;
            _filteredSetings.Clear();
            SettingFieldDrawer.Release();
            _fieldDrawer = null;
            instance = null;
        }

        void Start()
        {
            // Use reflection to keep compatibility with unity 4.x since it doesn't have Cursor
            var tCursor = typeof(Cursor);
            _curLockState = tCursor.GetProperty("lockState", BindingFlags.Static | BindingFlags.Public);
            _curVisible = tCursor.GetProperty("visible", BindingFlags.Static | BindingFlags.Public);

            if (_curLockState == null && _curVisible == null)
            {
                _obsoleteCursor = true;

                _curLockState = typeof(Screen).GetProperty("lockCursor", BindingFlags.Static | BindingFlags.Public);
                _curVisible = typeof(Screen).GetProperty("showCursor", BindingFlags.Static | BindingFlags.Public);
            }

            // Check if user has permissions to write config files to disk
            try { Config.Save(); }
            catch (IOException ex) { Logger.Log(LogLevel.Message | LogLevel.Warning, "WARNING: Failed to write to config directory, expect issues!\nError message:" + ex.Message); }
            catch (UnauthorizedAccessException ex) { Logger.Log(LogLevel.Message | LogLevel.Warning, "WARNING: Permission denied to write to config directory, expect issues!\nError message:" + ex.Message); }

            isTempWindowUnity6000 = false;
        }

        void Update()
        {
            if (DisplayingWindow && _externalSettingsRevision != ExternalSettingsRegistry.Revision)
                BuildSettingList();

            if (DisplayingWindow)
                SetUnlockCursor(0, true);

            if (OverrideHotkey)
                return;

            if (ResetWindowLayoutShortcut.IsDown())
                ResetWindowSizeAndPosition();

            if (!DisplayingWindow && _keybind.Value.IsDown())
                DisplayingWindow = true;
            else if (DisplayingWindow && (UnityInput.Current.GetKeyDown(KeyCode.Escape) || _keybind.Value.IsDown()))
                if (_configSettingWindow.IsOpen)
                    _configSettingWindow.IsOpen = false;
                else if (UnityInput.Current.GetKeyDown(KeyCode.Escape) && SettingFieldDrawer.CancelExternalNumericEdit())
                    return;
                else
                    DisplayingWindow = false;
        }

        void LateUpdate()
        {
            if (DisplayingWindow)
                SetUnlockCursor(0, true);
        }

        /// <summary>
        /// Is the config manager main window displayed on screen
        /// </summary>
        public bool DisplayingWindow
        {
            get => _displayingWindow;
            set
            {
                if (_displayingWindow == value)
                    return;

                _displayingWindow = value;

                SettingFieldDrawer.ClearCache();

                CreateBackgrounds();

                if (_displayingWindow)
                {
                    BuildSettingList();

                    _focusSearchBox = false;

                    // Do through reflection for unity 4 compat
                    if (_curLockState != null)
                    {
                        _previousCursorLockState = _obsoleteCursor ? Convert.ToInt32((bool)_curLockState.GetValue(null, null)) : (int)_curLockState.GetValue(null, null);
                        _previousCursorVisible = (bool)_curVisible.GetValue(null, null);
                    }
                }
                else
                {
                    if (!_previousCursorVisible || _previousCursorLockState != 0) // 0 = CursorLockMode.None
                        SetUnlockCursor(_previousCursorLockState, _previousCursorVisible);
                }

                DisplayingWindowChanged?.Invoke(this, new ValueChangedEventArgs<bool>(value));
            }
        }

        /// <summary>
        /// String currently entered into the search box
        /// </summary>
        public string SearchString
        {
            get => _searchString;
            private set
            {
                if (value == null)
                    value = string.Empty;

                if (_searchString == value)
                    return;

                _searchString = value;

                BuildFilteredSettingList();
            }
        }

        /// <summary>
        /// Register a custom setting drawer for a given type. The action is ran in OnGui in a single setting slot.
        /// Do not use any Begin / End layout methods, and avoid raising height from standard.
        /// </summary>
        public static void RegisterCustomSettingDrawer(Type settingType, Action<SettingEntryBase> onGuiDrawer)
        {
            if (settingType == null) throw new ArgumentNullException(nameof(settingType));
            if (onGuiDrawer == null) throw new ArgumentNullException(nameof(onGuiDrawer));

            if (SettingFieldDrawer.SettingDrawHandlers.ContainsKey(settingType))
                LogInfo("Tried to add a setting drawer for type " + settingType.FullName + " while one already exists.");
            else
                SettingFieldDrawer.SettingDrawHandlers[settingType] = onGuiDrawer;
        }

        /// <summary>
        /// Register settings owned outside BaseUnityPlugin.Config. The same GUID replaces
        /// its previous registration; disposing an older handle cannot remove its replacement.
        /// Values and persistence stay with the supplied callbacks. Dispose when the owner stops.
        /// </summary>
        public static IDisposable RegisterExternalSettings(string guid, string name, string version,
            IEnumerable<ExternalSetting> settings)
        {
            return ExternalSettingsRegistry.Register(guid, name, version, settings);
        }

        private sealed class PluginSettingsData
        {
            public BepInPlugin Info;
            public List<PluginSettingsGroupData> Categories;
            private bool _collapsed;

            public bool Collapsed
            {
                get => _collapsed;
                set
                {
                    _collapsed = value;
                    Height = 0;
                }
            }

            public bool Selected
            {
                get => instance != null && instance._selectedPlugin == Info.GUID;
                set
                {
                    if (instance != null)
                        instance._selectedPlugin = value ? Info.GUID : string.Empty;
                }
            }

            public bool ShowCategories
            {
                get => instance != null && (instance._showPluginCategories == Info.GUID || instance.IsSearching);
                set
                {
                    if (instance != null)
                        instance._showPluginCategories = value ? Info.GUID : string.Empty;
                }
            }

            public sealed class PluginSettingsGroupData
            {
                public string ID;
                public string Name;
                public List<SettingEntryBase> Settings;
                public bool Collapsed;
                public bool Selected
                {
                    get => instance != null && instance._selectedCategory == ID;
                    set
                    {
                        if (instance != null)
                            instance._selectedCategory = value ? ID : string.Empty;
                    }
                }
                public override string ToString() => Name;
            }

            public int Height { get; set; }

            public override string ToString() => Info == null ? "" : $"{Info.Name} {Info.Version}";
        }
    }
}
