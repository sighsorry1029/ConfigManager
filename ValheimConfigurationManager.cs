using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ConfigurationManager
{
    public partial class ConfigurationManager
    {
        internal const string menuButtonName = "ConfigManager";

        internal const string hiddenSettingsFileName = "ConfigManager.hiddensettings.txt";

        public static ConfigEntry<bool> _showMainMenuButton;

        private const string MainMenuButtonCaption = "Mods settings";

        private static readonly Harmony harmony = new Harmony(GUID);
        private static readonly FieldInfo fejdStartupMenuButtonsField = AccessTools.Field(typeof(FejdStartup), "m_menuButtons");
        private static readonly FieldInfo menuCloseMenuStateField = AccessTools.Field(typeof(Menu), "m_closeMenuState");
        private static readonly FieldInfo menuRebuildLayoutField = AccessTools.Field(typeof(Menu), "m_rebuildLayout");
        private static readonly FieldInfo guiScalerMinWidthField = AccessTools.Field(typeof(GuiScaler), "m_minWidth");
        private static readonly FieldInfo guiScalerMinHeightField = AccessTools.Field(typeof(GuiScaler), "m_minHeight");
        private static readonly FieldInfo guiScalerLargeScaleField = AccessTools.Field(typeof(GuiScaler), "m_largeGuiScale");

        private static HashSet<string> hiddenSettings = new HashSet<string>(StringComparer.Ordinal);

        private static DirectoryInfo pluginDirectory;
        private static DirectoryInfo configDirectory;
        private readonly List<FileSystemWatcher> hiddenSettingsWatchers = new List<FileSystemWatcher>();
        private Coroutine hiddenSettingsReloadCoroutine;
        private readonly List<Button> _ownedMenuButtons = new List<Button>();

        void OnEnable()
        {
            _showMainMenuButton = Config.Bind(GeneralSection, "Show main-menu button", true,
                Describe("Add a main-menu button that opens ConfigManager.", GeneralSectionOrder, 800));

            _showMainMenuButton.SettingChanged += MainMenuSettingChanged;

            harmony.PatchAll();

            DisplayingWindowChanged += ConfigurationManager_DisplayingWindowChanged;

            pluginDirectory = new DirectoryInfo(Assembly.GetExecutingAssembly().Location).Parent;
            configDirectory = new DirectoryInfo(Paths.ConfigPath);

            _configFilesEditor?.SetWatching(true);
            SetupHiddenSettingsWatcher();
            SetupMenuButton();
        }

        void OnDisable()
        {
            // Notify the menu and restore the cursor while our event handler is attached.
            try
            {
                DisplayingWindow = false;
            }
            catch (Exception ex)
            {
                LogWarning($"Could not close ConfigManager during disable: {ex.Message}");
            }

            RemoveOwnedMenuButtons();
            if (hiddenSettingsReloadCoroutine != null)
            {
                StopCoroutine(hiddenSettingsReloadCoroutine);
                hiddenSettingsReloadCoroutine = null;
            }

            DisposeHiddenSettingsWatchers();
            _configFilesEditor?.SetWatching(false);
            if (_showMainMenuButton != null)
                _showMainMenuButton.SettingChanged -= MainMenuSettingChanged;
            harmony?.UnpatchSelf();
            DisplayingWindowChanged -= ConfigurationManager_DisplayingWindowChanged;
        }

        private void MainMenuSettingChanged(object sender, EventArgs args)
        {
            SetupMenuButton();
        }

        /// <summary>
        /// Toggle configuration manager window visibility
        /// </summary>
        public void ToggleWindow()
        {
            DisplayingWindow = !DisplayingWindow;
        }

        private void SetupHiddenSettingsWatcher()
        {
            DisposeHiddenSettingsWatchers();

            AddHiddenSettingsWatcher(pluginDirectory);
            if (!string.Equals(pluginDirectory.FullName, configDirectory.FullName, StringComparison.OrdinalIgnoreCase))
                AddHiddenSettingsWatcher(configDirectory);

            ReadConfigs();
        }

        private void AddHiddenSettingsWatcher(DirectoryInfo directory)
        {
            if (directory == null || !directory.Exists)
                return;

            FileSystemWatcher watcher = new FileSystemWatcher(directory.FullName, hiddenSettingsFileName)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                SynchronizingObject = ThreadingHelper.SynchronizingObject
            };

            watcher.Changed += QueueHiddenSettingsReload;
            watcher.Created += QueueHiddenSettingsReload;
            watcher.Renamed += QueueHiddenSettingsReload;
            watcher.Deleted += QueueHiddenSettingsReload;
            watcher.EnableRaisingEvents = true;
            hiddenSettingsWatchers.Add(watcher);
        }

        private void DisposeHiddenSettingsWatchers()
        {
            foreach (FileSystemWatcher watcher in hiddenSettingsWatchers)
                watcher.Dispose();

            hiddenSettingsWatchers.Clear();
        }

        private void QueueHiddenSettingsReload(object sender, FileSystemEventArgs args)
        {
            if (!isActiveAndEnabled)
                return;

            if (hiddenSettingsReloadCoroutine != null)
                StopCoroutine(hiddenSettingsReloadCoroutine);

            hiddenSettingsReloadCoroutine = StartCoroutine(ReloadHiddenSettingsAfterDelay());
        }

        private IEnumerator ReloadHiddenSettingsAfterDelay()
        {
            yield return new WaitForSecondsRealtime(0.2f);
            hiddenSettingsReloadCoroutine = null;
            ReadConfigs();
        }

        private static void ReadConfigs(object sender = null, FileSystemEventArgs eargs = null)
        {
            HashSet<string> loadedSettings = new HashSet<string>(StringComparer.Ordinal);
            bool readFailed = false;

            foreach (string path in GetHiddenSettingsPaths())
            {
                if (!File.Exists(path))
                    continue;

                LogInfo($"Loading {path}");

                try
                {
                    using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (StreamReader reader = new StreamReader(fs))
                    {
                        int lineNumber = 0;
                        string rawLine;
                        while ((rawLine = reader.ReadLine()) != null)
                        {
                            lineNumber++;
                            if (HiddenSettingsParser.TryParseLine(rawLine, out string setting))
                                loadedSettings.Add(setting);
                            else if (!rawLine.IsNullOrWhiteSpace() && !rawLine.TrimStart().StartsWith("#", StringComparison.Ordinal))
                            {
                                readFailed = true;
                                LogWarning($"Invalid hidden setting at {path}:{lineNumber}; keeping the previous list");
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    readFailed = true;
                    LogWarning($"Could not read hidden settings file ({path}): {e.Message}");
                }
            }

            if (readFailed)
                return;

            hiddenSettings = loadedSettings;
            LogInfo($"Loaded {hiddenSettings.Count} local hidden setting(s)");

            if (instance != null && instance._allSettings != null)
                instance.BuildFilteredSettingList();
        }

        private static IEnumerable<string> GetHiddenSettingsPaths()
        {
            string pluginPath = Path.Combine(pluginDirectory.FullName, hiddenSettingsFileName);
            yield return pluginPath;

            string configPath = Path.Combine(configDirectory.FullName, hiddenSettingsFileName);
            if (!string.Equals(pluginPath, configPath, StringComparison.OrdinalIgnoreCase))
                yield return configPath;
        }

        internal static bool IsSettingHidden(string setting) => hiddenSettings.Contains(setting);

        private void ConfigurationManager_DisplayingWindowChanged(object sender, ValueChangedEventArgs<bool> e)
        {
            if (FejdStartup.instance && FejdStartup.instance.m_mainMenu && FejdStartup.instance.m_mainMenu.activeSelf)
            {
                FejdStartup.instance.m_mainMenu.SetActive(value: false);
                FejdStartup.instance.m_mainMenu.SetActive(value: true);
            }

            if (Menu.instance)
            {
                SetMenuCloseState(Menu.instance, DisplayingWindow ? "SettingsOpen" : "CanBeClosed");
                menuRebuildLayoutField?.SetValue(Menu.instance, true);
            }

        }

        private bool HideSettings()
        {
            return hiddenSettings.Count > 0;
        }

        private void SetupMenuButton()
        {
            if (!isActiveAndEnabled)
                return;

            _ownedMenuButtons.RemoveAll(button => !button);
            if (FejdStartup.instance && FejdStartup.instance.m_menuList)
            {
                SetupMainMenuButton(FejdStartup.instance.m_menuList.transform.Find("MenuEntries"));
                fejdStartupMenuButtonsField?.SetValue(FejdStartup.instance, FejdStartup.instance.m_menuList.GetComponentsInChildren<Button>());
            }

            if (Menu.instance)
                SetupMainMenuButton(Menu.instance.m_menuDialog?.Find("MenuEntries"));
        }

        private void SetupMainMenuButton(Transform menuEntries)
        {
            if (menuEntries == null)
                return;

            Transform settings = menuEntries.Find("Settings");
            if (settings == null)
                return;

            GameObject menuButton = menuEntries.Find(menuButtonName)?.gameObject;
            if (menuButton == null)
            {
                menuButton = Instantiate(settings.gameObject, menuEntries);
                menuButton.transform.SetSiblingIndex(settings.GetSiblingIndex() + 1);
                menuButton.name = menuButtonName;

                Button button = menuButton.GetComponent<Button>();
                if (button == null)
                {
                    Destroy(menuButton);
                    return;
                }

                if (button.onClick.GetPersistentEventCount() > 0)
                    button.onClick.SetPersistentListenerState(0, UnityEngine.Events.UnityEventCallState.Off);
                button.onClick.AddListener(ToggleWindow);
                _ownedMenuButtons.Add(button);

                var navigation = button.navigation;
                navigation.selectOnUp = settings.GetComponent<Button>();
                button.navigation = navigation;
            }

            Button modButton = menuButton.GetComponent<Button>();
            Button previousButton = settings.GetComponent<Button>();
            TMP_Text caption = menuButton.GetComponentInChildren<TMP_Text>();
            if (modButton == null || previousButton == null || caption == null)
                return;

            caption.text = MainMenuButtonCaption;
            menuButton.SetActive(_showMainMenuButton.Value);

            var previousNavigation = previousButton.navigation;

            Button nextButton = modButton.navigation.selectOnDown as Button;
            var nextNavigation = nextButton ? nextButton.navigation : default;

            if (_showMainMenuButton.Value)
            {
                previousNavigation.selectOnDown = modButton;
                if (nextButton)
                    nextNavigation.selectOnUp = modButton;
            }
            else
            {
                previousNavigation.selectOnDown = modButton.navigation.selectOnDown;
                if (nextButton)
                    nextNavigation.selectOnUp = modButton.navigation.selectOnUp;
            }

            previousButton.navigation = previousNavigation;
            if (nextButton)
                nextButton.navigation = nextNavigation;
        }

        private void RemoveOwnedMenuButtons()
        {
            foreach (Button button in _ownedMenuButtons)
            {
                if (!button)
                    continue;

                button.onClick.RemoveListener(ToggleWindow);
                Navigation ownNavigation = button.navigation;
                Selectable previous = ownNavigation.selectOnUp;
                Selectable next = ownNavigation.selectOnDown;
                if (previous)
                {
                    Navigation navigation = previous.navigation;
                    if (navigation.selectOnDown == button)
                    {
                        navigation.selectOnDown = next;
                        previous.navigation = navigation;
                    }
                }
                if (next)
                {
                    Navigation navigation = next.navigation;
                    if (navigation.selectOnUp == button)
                    {
                        navigation.selectOnUp = previous;
                        next.navigation = navigation;
                    }
                }

                button.gameObject.SetActive(false);
                // Destroy is deferred; exclude this clone from Find on same-frame enable.
                button.gameObject.name = menuButtonName + " (removing)";
                Destroy(button.gameObject);
            }
            _ownedMenuButtons.Clear();

            if (FejdStartup.instance && FejdStartup.instance.m_menuList)
                fejdStartupMenuButtonsField?.SetValue(FejdStartup.instance,
                    FejdStartup.instance.m_menuList.GetComponentsInChildren<Button>());
        }

        public float GetScreenSizeFactor()
        {
            int minWidth = guiScalerMinWidthField?.GetValue(null) is int width && width > 0 ? width : ScreenSystemWidth;
            int minHeight = guiScalerMinHeightField?.GetValue(null) is int height && height > 0 ? height : ScreenSystemHeight;
            float largeGuiScale = guiScalerLargeScaleField?.GetValue(null) is float scale ? scale : 1f;

            float widthFactor = (float)ScreenSystemWidth / minWidth;
            float heightFactor = (float)ScreenSystemHeight / minHeight;
            return Mathf.Min(widthFactor, heightFactor) * largeGuiScale;
        }

        private static void SetMenuCloseState(Menu menu, string stateName)
        {
            if (menuCloseMenuStateField == null || !menuCloseMenuStateField.FieldType.IsEnum)
                return;

            try
            {
                object state = Enum.Parse(menuCloseMenuStateField.FieldType, stateName);
                menuCloseMenuStateField.SetValue(menu, state);
            }
            catch (ArgumentException e)
            {
                LogWarning($"Could not set Menu close state '{stateName}': {e.Message}");
            }
        }

        [HarmonyPatch(typeof(PlayerController), nameof(PlayerController.TakeInput))]
        [HarmonyPriority(Priority.Last)]
        public static class PlayerController_TakeInput_PreventInput
        {
            public static void Postfix(ref bool __result)
            {
                __result = __result && !instance.DisplayingWindow;
            }
        }

        [HarmonyPatch(typeof(TextInput), nameof(TextInput.IsVisible))]
        [HarmonyPriority(Priority.Last)]
        public static class TextInput_IsVisible_PreventInput
        {
            public static void Postfix(ref bool __result)
            {
                __result = __result || instance.DisplayingWindow;
            }
        }

        [HarmonyPatch]
        public static class Inventory_PreventInput
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(InventoryGrid), nameof(InventoryGrid.OnLeftClick));
                yield return AccessTools.Method(typeof(InventoryGrid), nameof(InventoryGrid.OnRightClick));
                yield return AccessTools.Method(typeof(InventoryGui), nameof(InventoryGui.OnSelectedItem));
                yield return AccessTools.Method(typeof(InventoryGui), nameof(InventoryGui.OnRightClickItem));
                yield return AccessTools.Method(typeof(Toggle), nameof(Toggle.OnSubmit));
                yield return AccessTools.Method(typeof(Toggle), nameof(Toggle.OnPointerClick));
                yield return AccessTools.Method(typeof(Player), nameof(Player.UseHotbarItem));
                yield return AccessTools.Method(typeof(ScrollRect), nameof(ScrollRect.OnScroll));
            }

            [HarmonyPriority(Priority.First)]
            private static bool Prefix() => !instance.DisplayingWindow;
        }

        [HarmonyPatch]
        public static class Button_PreventInput
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(Button), nameof(Button.OnPointerClick));
                yield return AccessTools.Method(typeof(Button), nameof(Button.OnSubmit));
                yield return AccessTools.Method(typeof(Button), "Press");
            }

            [HarmonyPriority(Priority.First)]
            private static bool Prefix(Button __instance) => !instance.DisplayingWindow || __instance.name == menuButtonName;
        }

        [HarmonyPatch]
        public static class ZInput_PreventPlayerInput
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.GetMouseButton));
                yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.GetMouseButtonDown));
                yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.GetMouseButtonUp));
                yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.GetRadialTap));
                yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.GetRadialMultiTap));
            }

            [HarmonyPriority(Priority.First)]
            private static bool Prefix(ref bool __result) => !instance.DisplayingWindow || (__result = false);
        }

        [HarmonyPatch]
        public static class ZInput_Float_PreventMouseInput
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.GetJoyLeftStickX));
                yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.GetJoyLeftStickY));
                yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.GetJoyRTrigger));
                yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.GetJoyLTrigger));
                yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.GetJoyRightStickX));
                yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.GetJoyRightStickY));
                yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.GetMouseScrollWheel));
            }

            [HarmonyPriority(Priority.Last)]
            private static void Postfix(ref float __result)
            {
                if (instance.DisplayingWindow)
                    __result = 0f;
            }
        }

        [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseDelta))]
        public static class ZInput_GetMouseDelta_PreventMouseInput
        {
            [HarmonyPriority(Priority.Last)]
            public static void Postfix(ref Vector2 __result)
            {
                if (instance.DisplayingWindow)
                    __result = Vector2.zero;
            }
        }

        [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.Start))]
        public static class FejdStartup_Start_MenuButton
        {
            public static void Postfix() => instance.SetupMenuButton();
        }

        [HarmonyPatch(typeof(Menu), nameof(Menu.Start))]
        public static class Menu_Start_MenuButton
        {
            public static void Postfix() => instance.SetupMenuButton();
        }

        [HarmonyPatch(typeof(Menu), nameof(Menu.UpdateNavigation))]
        public static class Menu_UpdateNavigation_MenuButton
        {
            public static void Postfix() => instance.SetupMenuButton();
        }
    }
}
