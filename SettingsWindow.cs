using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static ConfigurationManager.ConfigurationManagerStyles;

namespace ConfigurationManager
{
    public partial class ConfigurationManager
    {
        internal const int HeaderSize = 20;
        internal const int DefaultWidth = 750;
        internal const int DefaultHeight = 900;

        internal float scaleFactor;
        internal Matrix4x4 guiMatrix;

        private float lastClickTime;
        private Vector2 lastClickPosition;
        private const float DoubleClickThreshold = 0.3f;

        private ConfigFilesEditor _configFilesEditor;
        private SettingEditWindow _configSettingWindow;

        internal string _selectedCategory;
        internal string _selectedPlugin;
        internal string _showPluginCategories;

        public bool SplitView
        {
            get => _splitView == null || _splitView.Value;
            set
            {
                if (_splitView != null)
                    _splitView.Value = value;
            }
        }

        void OnGUI()
        {
            if (DisplayingWindow)
            {
                CreateStyles();
                SetUnlockCursor(0, true);

                if (scaleFactor != (scaleFactor = ScaleFactor))
                    guiMatrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scaleFactor, scaleFactor, 1f));

                currentWindowRect.size = _windowSize.Value;
                currentWindowRect.position = _windowPosition.Value;

                GUI.tooltip = "";

                var originalMatrix = GUI.matrix;
                GUI.matrix = guiMatrix;

                GUI.Box(currentWindowRect, GUIContent.none, GUIStyle.none);
                var color = GUI.backgroundColor;
                GUI.backgroundColor = Palette.WindowBackground;

                CalculateSettingsColumnsWidth(currentWindowRect.width);

                currentWindowRect = GUILayout.Window(WindowId, currentWindowRect, SettingsWindow, UiText.WindowTitle, GetWindowStyle());

                if (!UnityInput.Current.GetKeyDown(KeyCode.Mouse0) && (currentWindowRect.position != _windowPosition.Value))
                    SaveCurrentSizeAndPosition();

                GUI.backgroundColor = color;

                _configFilesEditor.OnGUI();

                _configSettingWindow.OnGUI();

                GUI.matrix = originalMatrix;
            }
        }

        private void CalculateSettingsColumnsWidth(float width)
        {
            PluginListColumnWidth = Mathf.RoundToInt(width * SplitViewPluginListWidth);
            SettingsListColumnWidth = Mathf.RoundToInt(SplitView ? width - PluginListColumnWidth : width);

            LeftColumnWidth = Mathf.Max(200, Mathf.RoundToInt(Mathf.Clamp(SettingsListColumnWidth * SettingLabelWidth, width * 0.1f, width * 0.6f)) - fontSize / 2);
            RightColumnWidth = Mathf.Max(200, Mathf.RoundToInt(Mathf.Clamp(SettingsListColumnWidth - LeftColumnWidth - fontSize - 90 - fontSize, width * 0.3f, width * 0.8f)));
        }

        internal void SaveCurrentSizeAndPosition()
        {
            _windowSize.Value = new Vector2(Mathf.Clamp(currentWindowRect.size.x, 500f, ScreenWidth), Mathf.Clamp(currentWindowRect.size.y, 200f, ScreenHeight));
            _windowPosition.Value = new Vector2(Mathf.Clamp(currentWindowRect.position.x, 0f, ScreenWidth - _windowSize.Value.x / 4f), Mathf.Clamp(currentWindowRect.position.y, 0f, ScreenHeight - HeaderSize * 2));
            Config.Save();
            SettingFieldDrawer.ClearComboboxCache();
        }

        internal void ResetWindowSizeAndPosition()
        {
            CalculateDefaultWindowRect();

            _windowSize.Value = GetDefaultManagerWindowSize();
            _windowPosition.Value = GetDefaultManagerWindowPosition();
            _windowSizeTextEditor.Value = GetDefaultTextEditorWindowSize();
            _windowPositionTextEditor.Value = GetDefaultTextEditorWindowPosition();
            _windowPositionEditSetting.Value = GetDefaultEditSettingWindowPosition();
            _windowSizeEditSetting.Value = GetDefaultEditSettingWindowSize();

            Config.Save();
            SettingFieldDrawer.ClearComboboxCache();
        }

        private void HandleHeaderDblClick(Rect titleBarRect)
        {
            if (UnityInput.Current.GetMouseButtonDown(0) && titleBarRect.Contains(Event.current.mousePosition))
            {
                float time = (float)Math.Round(Time.realtimeSinceStartup, 1);
                if (lastClickPosition == Event.current.mousePosition && time != lastClickTime && time - lastClickTime < DoubleClickThreshold)
                    ResetWindowSizeAndPosition();

                lastClickTime = time;
                lastClickPosition = Event.current.mousePosition;
            }
        }

        private void SettingsWindow(int id)
        {
            SettingFieldDrawer.BeginExternalNumericGUI();
            var headerRect = new Rect(0, 0, currentWindowRect.width, HeaderSize);
            HandleHeaderDblClick(headerRect);

            GUI.DragWindow(headerRect);
            DrawWindowHeader();

            var backgroundColor = GUI.backgroundColor;
            GUI.backgroundColor = Palette.EntryBackground;

            if (SplitView)
                DrawSplitView();
            else
                DrawListView();

            GUI.backgroundColor = backgroundColor;

            if (!SettingFieldDrawer.DrawCurrentDropdown())
                DrawTooltip(currentWindowRect);

            currentWindowRect = Utilities.Utils.ResizeWindow(id, currentWindowRect, out var sizeChanged);

            if (sizeChanged)
                SaveCurrentSizeAndPosition();
            SettingFieldDrawer.EndExternalNumericGUI();
        }

        private void DrawSplitView()
        {
            GUILayout.BeginHorizontal();
            {
                GUILayout.BeginVertical(GUILayout.Width(PluginListColumnWidth));

                _settingWindowScrollPos = GUILayout.BeginScrollView(_settingWindowScrollPos, false, true);

                try
                {
                    _filteredSetings.Do(DrawPluginInSplitViewList);

                    GUILayout.Space(5);
                    GUILayout.Label(UiText.NoOptionsPluginsText + ": " + _modsWithoutSettings, GetLabelStyle());
                    GUILayout.Space(5);
                }
                finally
                {
                    GUILayout.EndScrollView();
                }

                GUILayout.EndVertical();

                GUILayout.Space(5f);

                var plugin = _filteredSetings.FirstOrDefault(plg => plg.Selected) ?? _filteredSetings.FirstOrDefault();

                if (plugin != null)
                {
                    plugin.Collapsed = false;
                    if (plugin.Selected != (plugin.Selected = true))
                        plugin.ShowCategories = true;

                    GUILayout.BeginVertical(GUILayout.MaxWidth(SettingsListColumnWidth));

                    bool hasCollapsedCategories = plugin.Categories.Any(cat => cat.Collapsed);
                    SettingFieldDrawer.DrawPluginHeader(GetPluginHeaderName(plugin, showGuid: true), plugin.Collapsed, hasCollapsedCategories, withHover:false, out var toggleCollapseAll);

                    _settingWindowCategoriesScrollPos[plugin.Info.GUID] = GUILayout.BeginScrollView(_settingWindowCategoriesScrollPos.TryGetValue(plugin.Info.GUID, out Vector2 scrollPos) ? scrollPos : Vector2.zero, false, true);
                    try
                    {
                        DrawPluginCategories(plugin, hasCollapsedCategories, toggleCollapseAll);
                    }
                    finally
                    {
                        GUILayout.EndScrollView();
                    }

                    GUILayout.EndVertical();
                }
                else
                {
                    GUILayout.FlexibleSpace();
                }
            }
            GUILayout.EndHorizontal();
        }

        private void DrawListView()
        {
            _settingWindowScrollPos = GUILayout.BeginScrollView(_settingWindowScrollPos, false, true);

            var scrollPosition = _settingWindowScrollPos.y;
            var scrollHeight = currentWindowRect.height;

            GUILayout.BeginVertical();
            try
            {
                float currentHeight = 0;

                foreach (var plugin in _filteredSetings)
                {
                    var visible = plugin.Height == 0 || currentHeight + plugin.Height >= scrollPosition && currentHeight <= scrollPosition + scrollHeight;

                    if (visible)
                    {
                        try
                        {
                            DrawSinglePlugin(plugin);
                        }
                        catch (ArgumentException)
                        {
                            // Needed to avoid GUILayout: Mismatched LayoutGroup.Repaint crashes on large lists
                        }

                        if (Event.current.type == EventType.Repaint)
                            plugin.Height = (int)GUILayoutUtility.GetLastRect().height;
                    }
                    else
                    {
                        try
                        {
                            if (plugin.Height > 0)
                                GUILayout.Space(plugin.Height);
                        }
                        catch (ArgumentException)
                        {
                            // Needed to avoid GUILayout: Mismatched LayoutGroup.Repaint crashes on large lists
                        }
                    }

                    currentHeight += plugin.Height + 1;
                }

                GUILayout.Space(20);
                GUILayout.Label(UiText.NoOptionsPluginsText + ": " + _modsWithoutSettings, GetLabelStyle());
                GUILayout.Space(10);
            }
            finally
            {
                GUILayout.EndVertical();
                GUILayout.EndScrollView();
            }
        }

        private void DrawWindowHeader()
        {
            var backgroundColor = GUI.backgroundColor;
            GUI.backgroundColor = Palette.EntryBackground;

            GUILayout.BeginHorizontal();
            {
                var enabled = GUI.enabled;
                GUI.enabled = !IsSearching;

                if (_showAdvanced.Value != (_showAdvanced.Value = GUILayout.Toggle(_showAdvanced.Value, new GUIContent(UiText.AdvancedText, UiText.AdvancedTextTooltip), GetToggleStyle(), GUILayout.ExpandWidth(false))))
                    BuildFilteredSettingList();

                if (_showKeybinds.Value != (_showKeybinds.Value = GUILayout.Toggle(_showKeybinds.Value, new GUIContent(UiText.ShortcutsText, UiText.ShortcutsTextTooltip), GetToggleStyle(), GUILayout.ExpandWidth(false))))
                    BuildFilteredSettingList();

                GUI.enabled = enabled;

                GUILayout.Space(15f);

                DrawSearchBox();

                GUILayout.Space(15f);

                if (GUILayout.Button(UiText.ToggleTextEditorText, GetButtonStyle(), GUILayout.ExpandWidth(false)))
                    _configFilesEditor.IsOpen = !_configFilesEditor.IsOpen;

                GUILayout.Space(15f);

                var maxString = UiText.ViewModeListViewText.Length > UiText.ViewModeSplitViewText.Length ? UiText.ViewModeListViewText : UiText.ViewModeSplitViewText;
                if (GUILayout.Button(SplitView ? UiText.ViewModeListViewText : UiText.ViewModeSplitViewText, GetButtonStyle(), GUILayout.ExpandWidth(false), GUILayout.Width(GetButtonStyle().CalcSize(new GUIContent(maxString)).x)))
                    SplitView = !SplitView;

                if (GUILayout.Button(UiText.CloseText, GetButtonStyle(), GUILayout.ExpandWidth(false)))
                    DisplayingWindow = false;
            }
            GUILayout.EndHorizontal();

            GUI.backgroundColor = backgroundColor;
        }

        private void DrawSearchBox()
        {
            var backgroundColor = GUI.backgroundColor;
            GUI.backgroundColor = Palette.EntryBackground;

            GUI.SetNextControlName(SearchBoxName);
            SearchString = GUILayout.TextField(SearchString, GetTextStyle(), GUILayout.ExpandWidth(true));

            if (string.IsNullOrEmpty(SearchString) && Event.current.type == EventType.Repaint)
                GUI.Label(GUILayoutUtility.GetLastRect(), UiText.SearchText, GetPlaceholderTextStyle());

            if (_focusSearchBox)
            {
                GUI.FocusWindow(WindowId);
                GUI.FocusControl(SearchBoxName);
                _focusSearchBox = false;
            }

            GUI.backgroundColor = Palette.Widget;

            if (GUILayout.Button(UiText.ClearText, GetButtonStyle(), GUILayout.ExpandWidth(false)))
                SearchString = string.Empty;

            GUI.backgroundColor = backgroundColor;
        }

        private void DrawSinglePlugin(PluginSettingsData plugin)
        {
            GUILayout.BeginVertical();

            try
            {
                var hasCollapsedCategories = plugin.Categories.Any(cat => cat.Collapsed);

                if (SettingFieldDrawer.DrawPluginHeader(GetPluginHeaderName(plugin), plugin.Collapsed, hasCollapsedCategories, withHover:true, out var toggleCollapseAll) && !IsSearching)
                    plugin.Collapsed = !plugin.Collapsed;

                if (IsSearching || !plugin.Collapsed)
                    DrawPluginCategories(plugin, hasCollapsedCategories, toggleCollapseAll);
            }
            finally
            {
                GUILayout.EndVertical();
            }
        }

        private GUIContent GetPluginHeaderName(PluginSettingsData plugin, bool showGuid = false) => new GUIContent($"{plugin.Info.Name.TrimStart('!')} {plugin.Info.Version}{(showGuid ? $" ({plugin.Info.GUID})" : "")}");

        private void DrawPluginInSplitViewList(PluginSettingsData plugin)
        {
            var backgroundColor = GUI.backgroundColor;
            GUI.backgroundColor = Palette.EntryBackground;

            GUILayout.BeginHorizontal(GetBackgroundStyle(withHover: true));

            if (SettingFieldDrawer.DrawPluginHeaderSplitViewList(GetPluginHeaderName(plugin), plugin.Selected))
            {
                plugin.Selected = true;
                plugin.ShowCategories = !plugin.ShowCategories;
            }

            GUILayout.EndHorizontal();

            if (IsSearching || plugin.Selected && plugin.ShowCategories && (plugin.Categories.Count > 1))
            {
                GUILayout.BeginVertical(GetCategorySplitViewBackgroundStyle());
                plugin.Categories.Do(DrawPluginCategorySplitViewCollapsableList);
                GUILayout.EndVertical();
            }

            GUI.backgroundColor = backgroundColor;

            void DrawPluginCategorySplitViewCollapsableList(PluginSettingsData.PluginSettingsGroupData category)
            {
                GUILayout.BeginHorizontal();
                if (SettingFieldDrawer.DrawPluginCategorySplitViewList(new GUIContent(category.Name), category.Selected))
                {
                    plugin.Selected = true;
                    category.Selected = !category.Selected;
                    if (category.Selected)
                        category.Collapsed = false;
                }
                GUILayout.EndHorizontal();
            }
        }

        private void DrawPluginCategories(PluginSettingsData plugin, bool hasCollapsedCategories, bool toggleCollapseAll = false)
        {
            bool hasSelectedCategory = SplitView && plugin.Categories.Any(cat => cat.Selected);

            plugin.Categories.Do(category => DrawSingleCategory(plugin, hasCollapsedCategories, hasSelectedCategory, toggleCollapseAll, category));
        }

        private void DrawSingleCategory(PluginSettingsData plugin, bool hasCollapsedCategories, bool hasSelectedCategory, bool toggleCollapseAll, PluginSettingsData.PluginSettingsGroupData category)
        {
            if (hasSelectedCategory && !category.Selected)
                return;

            var backgroundColor = GUI.backgroundColor;
            GUI.backgroundColor = Palette.EntryBackground;

            if (!string.IsNullOrEmpty(category.Name))
            {
                if (toggleCollapseAll && !IsSearching)
                    category.Collapsed = !hasCollapsedCategories;

                GUILayout.BeginVertical(GetCategoryHeaderBackgroundStyle(withHover: true), GUILayout.ExpandHeight(false));
                if (category.Collapsed && !IsSearching ? SettingFieldDrawer.DrawCollapsedCategoryHeader(category.Name, category.Settings.All(IsDefaultValue)) : SettingFieldDrawer.DrawCategoryHeader(category.Name) && !IsSearching)
                    category.Collapsed = !category.Collapsed;
                GUILayout.EndVertical();
            }

            if (category.Settings.Any() && (!category.Collapsed || IsSearching))
            {
                GUILayout.BeginVertical(GetCategoryBackgroundStyle());
                try
                {
                    category.Settings.Do(DrawSingleSetting);
                }
                finally
                {
                    GUILayout.EndVertical();
                }
            }

            GUI.backgroundColor = backgroundColor;
        }

        private void DrawSingleSetting(SettingEntryBase setting)
        {
            var contentColor = GUI.contentColor;
            var guiEnabled = GUI.enabled;

            if (setting.ReadOnly == true)
            {
                GUI.contentColor = Palette.ReadOnlyText;
                if (setting is ExternalSettingEntry) GUI.enabled = false;
            }

            GUILayout.BeginHorizontal(GUILayout.MaxWidth(SettingsListColumnWidth));

            try
            {
                DrawSettingName(setting);
                if (setting is ExternalSettingEntry external && external.UsesTextEditor && !external.UsesInlineNumericEditor)
                {
                    GUILayout.Label(new GUIContent(external.GetPreviewText(), external.ReadOnly == true
                            ? external.Description : "Use Edit to change and apply this value."),
                        GetLabelStyle(), GUILayout.ExpandWidth(true));
                }
                else
                    _fieldDrawer.DrawSettingValue(setting);
                DrawDefaultButton(setting);
            }
            catch (FormatException)
            {
                LogInfo($"Incorrect input: {setting.PluginInfo.Name} - {setting.Category} - {setting.DispName}");
            }
            catch (Exception ex)
            {
                Logger.Log(LogLevel.Error, $"Failed to draw setting {setting.PluginInfo.Name} - {setting.Category} - {setting.DispName}:\n{ex}");
                GUILayout.Label("Failed to draw this field, check log for details.", GetLabelStyle());
            }

            GUILayout.EndHorizontal();

            if (setting is ExternalSettingEntry externalSetting && !string.IsNullOrEmpty(externalSetting.ErrorText))
                GUILayout.Label(externalSetting.ErrorText, GetLabelStyle());

            if (!Utilities.ComboBox.IsShown())
                GUI.enabled = guiEnabled;

            GUI.contentColor = contentColor;
        }

        private void DrawSettingName(SettingEntryBase setting)
        {
            if (setting.HideSettingName) return;

            var color = GUI.backgroundColor;
            GUI.backgroundColor = Palette.Widget;

            GUILayout.BeginHorizontal(GUILayout.Width(LeftColumnWidth), GUILayout.MaxWidth(LeftColumnWidth));
            GUILayout.Label(new GUIContent(setting.DispName.TrimStart('!'), setting.Description), GetLabelStyleSettingName(), GUILayout.ExpandWidth(true));
            if (!(setting is ExternalSettingEntry external && external.UsesInlineNumericEditor) &&
                GUILayout.Button(new GUIContent(UiText.EditText, setting.Description), GetButtonStyle(), GUILayout.ExpandWidth(false)))
                _configSettingWindow.EditSetting(setting);

            GUILayout.EndHorizontal();

            GUI.backgroundColor = color;
        }

        internal static void DrawDefaultButton(SettingEntryBase setting)
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
                    if (setting is ExternalSettingEntry external && external.UsesInlineNumericEditor)
                        external.NumericEditor.Reset();
                    else
                        setting.Set(setting.DefaultValue);
                }
            }
            else if (setting.SettingType.IsClass)
            {
                if (DrawResetButton())
                    setting.Set(null);
            }

            GUI.backgroundColor = color;
        }

        public void BuildSettingList()
        {
            // Capture before collecting: a concurrent registration cannot be mistaken
            // for one already included in the newly built list.
            _externalSettingsRevision = ExternalSettingsRegistry.Revision;
            _configSettingWindow?.CloseUnregisteredExternalSetting();
            SettingFieldDrawer.ClearCache();
            SettingSearcher.CollectSettings(out var results, out var modsWithoutSettings);

            _modsWithoutSettings = string.Join(", ", modsWithoutSettings.Select(x => x.TrimStart('!')).OrderBy(x => x).ToArray());
            _allSettings = results.ToList();

            BuildFilteredSettingList();
        }

        public bool IsSearching => SearchString.Length > 1;

        public void BuildFilteredSettingList()
        {
            IEnumerable<SettingEntryBase> results = _allSettings;
            var categoryOrders = _allSettings
                .Where(setting => setting.PluginInfo != null)
                .GroupBy(setting => Tuple.Create(setting.PluginInfo.GUID, setting.Category))
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(setting => setting.CategoryOrder).Where(order => order != 0).DefaultIfEmpty(0).Max());

            if (HideSettings())
                results = results.Where(x => x is ConfigSettingEntry configEntry ? !configEntry.ShouldBeHidden() :
                    !(x is ExternalSettingEntry externalEntry) || !externalEntry.ShouldBeHidden());

            if (IsSearching)
            {
                string[] searchTokens = SearchString.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                results = results.Where(x => ContainsSearchString(x, searchTokens));
            }
            else
            {
                if (!_showAdvanced.Value)
                    results = results.Where(x => x.IsAdvanced != true);
                if (_showKeybinds.Value)
                    results = results.Where(x => IsKeyboardShortcut(x));
            }

            const bool settingsAreCollapsed = PluginsInitiallyCollapsed;

            var nonDefaultCollapsedPluginState = new HashSet<string>();
            var collapsedCategoryState = new Dictionary<Tuple<string, string>, bool>();
            foreach (var pluginSettings in _filteredSetings)
            {
                if (pluginSettings.Collapsed != settingsAreCollapsed)
                {
                    nonDefaultCollapsedPluginState.Add(pluginSettings.Info.Name);
                }

                foreach (var category in pluginSettings.Categories)
                    collapsedCategoryState[Tuple.Create(pluginSettings.Info.Name, category.Name)] = category.Collapsed;
            }

            _filteredSetings = results
                .GroupBy(x => x.PluginInfo)
                .Select(pluginSettings =>
                {
                    var originalCategoryOrder = pluginSettings.Select(x => x.Category).Distinct().ToList();

                    var categories = pluginSettings
                        .GroupBy(x => x.Category)
                        .Select(x => new
                        {
                            Group = x,
                            OriginalOrder = originalCategoryOrder.IndexOf(x.Key),
                            ExplicitOrder = categoryOrders.TryGetValue(Tuple.Create(pluginSettings.Key.GUID, x.Key), out int categoryOrder)
                                ? categoryOrder
                                : 0
                        })
                        .OrderByDescending(x => x.ExplicitOrder)
                        .ThenBy(x => x.OriginalOrder)
                        .Select(x => new PluginSettingsData.PluginSettingsGroupData {
                            ID = $"{pluginSettings.Key.GUID}-{x.Group.Key}",
                            Name = x.Group.Key,
                            Settings = x.Group.OrderByDescending(set => set.Order).ThenBy(set => set.DispName).ToList(),
                            Collapsed = collapsedCategoryState.TryGetValue(Tuple.Create(pluginSettings.Key.Name, x.Group.Key), out var collapsed)
                                ? collapsed
                                : originalCategoryOrder.Count > 20 && x.Group.All(IsDefaultValue)
                        });

                    return new PluginSettingsData
                    {
                        Info = pluginSettings.Key,
                        Categories = categories.ToList(),
                        Collapsed = nonDefaultCollapsedPluginState.Contains(pluginSettings.Key.Name) ? !settingsAreCollapsed : settingsAreCollapsed
                    };
                })
                .OrderBy(x => x.Info.Name)
                .ThenBy(x => x.Info.GUID)
                .ToList();
        }

        private static bool IsKeyboardShortcut(SettingEntryBase x) => x.SettingType == typeof(KeyboardShortcut);

        private static bool ContainsSearchString(SettingEntryBase setting, string[] searchStrings)
        {
            var combinedSearchTarget = setting.PluginInfo.Name + "\n" +
                                       setting.PluginInfo.GUID + "\n" +
                                       setting.DispName + "\n" +
                                       setting.Category + "\n" +
                                       setting.Description + "\n" +
                                       setting.DefaultValue + "\n" +
                                       setting.SettingType.Name + "\n" +
                                       setting.Get();

            return searchStrings.All(s => combinedSearchTarget.IndexOf(s, StringComparison.InvariantCultureIgnoreCase) >= 0);
        }

        private void CalculateDefaultWindowRect()
        {
            var width = Mathf.Min(ScreenSystemWidth, DefaultWidth * (SplitView ? 1f + SplitViewPluginListWidth : 1f));
            var height = Mathf.Min(ScreenSystemHeight, DefaultHeight);
            var offset = Mathf.RoundToInt(Mathf.Min(ScreenSystemWidth - width, ScreenSystemHeight - height)) / 16f;

            DefaultWindowRect = new Rect(offset, offset, width, height);

            CalculateSettingsColumnsWidth(DefaultWindowRect.width);
        }

        internal static void DrawTooltip(Rect area)
        {
            if (!string.IsNullOrEmpty(GUI.tooltip))
            {
                var currentEvent = Event.current;

                var color = GUI.backgroundColor;
                GUI.backgroundColor = Palette.TooltipBackground;

                float width = 0f;
                GUIStyle style = GetTooltipStyle();
                string tooltip = GUI.tooltip.Replace("\r\n", "\n").Replace("\r", "\n");

                foreach (string line in tooltip.Split('\n'))
                {
                    style.CalcMinMaxWidth(new GUIContent(line), out _, out float w);
                    if (w > width)
                        width = w;
                }

                width = Mathf.Min(width + 2f, area.width * 0.8f);

                var height = GetTooltipStyle().CalcHeight(new GUIContent(tooltip), width) + 10f;

                var x = currentEvent.mousePosition.x + width > area.width
                    ? area.width - width
                    : currentEvent.mousePosition.x;

                var y = currentEvent.mousePosition.y + 25 + height > area.height
                    ? currentEvent.mousePosition.y - height
                    : currentEvent.mousePosition.y + 25;

                GUI.Box(new Rect(x, y, width, height), tooltip, GetTooltipStyle());
                GUI.backgroundColor = color;
            }
        }

        private void CreateBackgrounds()
        {
            if (WindowBackground == null)
            {
                var background = new Texture2D(1, 1, TextureFormat.ARGB32, false);
                background.SetPixel(0, 0, Palette.WindowBackground);
                background.Apply();
                WindowBackground = background;

                var entryBackground = new Texture2D(1, 1, TextureFormat.ARGB32, false);
                entryBackground.SetPixel(0, 0, Palette.EntryBackground);
                entryBackground.Apply();
                EntryBackground = entryBackground;

                var tooltipBackground = new Texture2D(1, 1, TextureFormat.ARGB32, false);
                tooltipBackground.SetPixel(0, 0, Palette.TooltipBackground);
                tooltipBackground.Apply();
                TooltipBackground = tooltipBackground;

                var headerBackground = new Texture2D(1, 1, TextureFormat.ARGB32, false);
                headerBackground.SetPixel(0, 0, Palette.HeaderBackground);
                headerBackground.Apply();
                HeaderBackground = headerBackground;

                var headerBackgroundHover = new Texture2D(1, 1, TextureFormat.ARGB32, false);
                headerBackgroundHover.SetPixel(0, 0, Palette.HeaderHoverBackground);
                headerBackgroundHover.Apply();
                HeaderBackgroundHover = headerBackgroundHover;

                var settingWindowBackground = new Texture2D(1, 1, TextureFormat.ARGB32, false);
                settingWindowBackground.SetPixel(0, 0, Palette.SettingWindowBackground);
                settingWindowBackground.Apply();
                SettingWindowBackground = settingWindowBackground;
            }
        }

        private void SetUnlockCursor(int lockState, bool cursorVisible)
        {
            if (_curLockState != null)
            {
                if (_obsoleteCursor)
                    _curLockState.SetValue(null, Convert.ToBoolean(lockState), null);
                else
                    _curLockState.SetValue(null, lockState, null);

                _curVisible.SetValue(null, cursorVisible, null);
            }
        }
    }
}
