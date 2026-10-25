using System;
using UnityEngine;
using static ConfigurationManager.ConfigurationManager;

namespace ConfigurationManager
{
    public class ConfigurationManagerStyles
    {
        private static GUIStyle windowStyle;
        private static GUIStyle labelStyle;
        private static GUIStyle labelStyleSettingName;
        private static GUIStyle labelStyleInfo;
        private static GUIStyle labelStyleValueDefault;
        private static GUIStyle labelStyleValueChanged;
        private static GUIStyle textStyle;
        private static GUIStyle textStyleValueDefault;
        private static GUIStyle textStyleValueChanged;
        private static GUIStyle toggleStyle;
        private static GUIStyle toggleStyleValueDefault;
        private static GUIStyle toggleStyleValueChanged;
        private static GUIStyle buttonStyle;
        private static GUIStyle buttonStyleValueDefault;
        private static GUIStyle buttonStyleValueChanged;
        private static GUIStyle comboBoxStyle;
        private static GUIStyle boxStyle;
        private static GUIStyle sliderStyle;
        private static GUIStyle thumbStyle;
        private static GUIStyle categoryHeaderStyleDefault;
        private static GUIStyle categoryHeaderStyleChanged;
        private static GUIStyle pluginHeaderStyle;
        private static GUIStyle pluginHeaderStyleActive;
        private static GUIStyle pluginHeaderStyleSplitView;
        private static GUIStyle pluginHeaderStyleSplitViewActive;
        private static GUIStyle pluginCategoryStyleSplitView;
        private static GUIStyle pluginCategoryStyleSplitViewActive;
        private static GUIStyle backgroundStyle;
        private static GUIStyle backgroundStyleWithHover;
        private static GUIStyle categoryBackgroundStyle;
        private static GUIStyle categoryHeaderBackgroundStyle;
        private static GUIStyle categoryHeaderBackgroundStyleWithHover;
        private static GUIStyle settingWindowBackgroundStyle;
        private static GUIStyle categorySplitViewBackgroundStyle;
        private static GUIStyle tooltipStyle;
        private static GUIStyle fileEditorFileStyle;
        private static GUIStyle fileEditorFileStyleActive;
        private static GUIStyle fileEditorDirectoryStyle;
        private static GUIStyle fileEditorDirectoryStyleActive;
        private static GUIStyle fileEditorRenameFileField;
        private static GUIStyle fileEditorErrorText;
        private static GUIStyle fileEditorTextArea;
        private static GUIStyle delimiterLine;
        private static GUIStyle placeholderText;

        private static bool _stylesInitialized;
        private static StyleInputs _styleInputs;

        private struct StyleInputs : IEquatable<StyleInputs>
        {
            internal GUISkin Skin;
            internal Font SkinFont;
            internal int FontSize;
            internal int EditorFontSize;
            internal bool EditorWordWrap;
            internal Texture2D EntryBackground;
            internal Texture2D TooltipBackground;
            internal Texture2D HeaderBackground;
            internal Texture2D HeaderBackgroundHover;
            internal Texture2D SettingWindowBackground;

            public bool Equals(StyleInputs other)
            {
                return ReferenceEquals(Skin, other.Skin)
                    && ReferenceEquals(SkinFont, other.SkinFont)
                    && FontSize == other.FontSize
                    && EditorFontSize == other.EditorFontSize
                    && EditorWordWrap == other.EditorWordWrap
                    && ReferenceEquals(EntryBackground, other.EntryBackground)
                    && ReferenceEquals(TooltipBackground, other.TooltipBackground)
                    && ReferenceEquals(HeaderBackground, other.HeaderBackground)
                    && ReferenceEquals(HeaderBackgroundHover, other.HeaderBackgroundHover)
                    && ReferenceEquals(SettingWindowBackground, other.SettingWindowBackground);
            }
        }

        public static int fontSize = 14;

        public static void CreateStyles()
        {
            var inputs = new StyleInputs
            {
                Skin = GUI.skin,
                SkinFont = GUI.skin.font,
                FontSize = Mathf.Clamp(_textSize.Value, 10, 30),
                EditorFontSize = _textEditorFontSize.Value,
                EditorWordWrap = _textEditorWordWrap.Value,
                EntryBackground = EntryBackground,
                TooltipBackground = TooltipBackground,
                HeaderBackground = HeaderBackground,
                HeaderBackgroundHover = HeaderBackgroundHover,
                SettingWindowBackground = SettingWindowBackground
            };
            if (_stylesInitialized && fontSize == inputs.FontSize && _styleInputs.Equals(inputs))
                return;
            // Keep the same metrics from Layout through Repaint.
            if (_stylesInitialized && Event.current.type != EventType.Layout)
                return;

            fontSize = inputs.FontSize;
            if (_textSize.Value != inputs.FontSize)
                _textSize.Value = inputs.FontSize;

            windowStyle = new GUIStyle(GUI.skin.window);
            windowStyle.normal.textColor = Palette.MainText;
            windowStyle.fontSize = fontSize;
            windowStyle.onNormal.textColor = Palette.MainText;

            labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.normal.textColor = Palette.MainText;
            labelStyle.fontSize = fontSize;

            labelStyleSettingName = new GUIStyle(labelStyle);
            labelStyleSettingName.wordWrap = true;
            labelStyleSettingName.clipping = TextClipping.Clip;

            labelStyleInfo = new GUIStyle(labelStyleSettingName);
            labelStyleInfo.normal.textColor = Palette.ReadOnlyText;

            labelStyleValueDefault = new GUIStyle(labelStyle);
            labelStyleValueDefault.normal.textColor = Palette.DefaultValueText;

            labelStyleValueChanged = new GUIStyle(labelStyle);
            labelStyleValueChanged.normal.textColor = Palette.ChangedValueText;

            textStyle = new GUIStyle(GUI.skin.textArea);
            textStyle.normal.textColor = Palette.MainText;
            textStyle.fontSize = fontSize;

            textStyleValueDefault = new GUIStyle(textStyle);
            textStyleValueDefault.normal.textColor = Palette.DefaultValueText;

            textStyleValueChanged = new GUIStyle(textStyle);
            textStyleValueChanged.normal.textColor = Palette.ChangedValueText;

            buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.normal.textColor = Palette.MainText;
            buttonStyle.onNormal.textColor = Palette.ChangedValueText;
            buttonStyle.fontSize = fontSize;

            buttonStyleValueDefault = new GUIStyle(buttonStyle);
            buttonStyleValueDefault.normal.textColor = Palette.DefaultValueText;
            buttonStyleValueDefault.onNormal.textColor = Palette.DefaultValueText;

            buttonStyleValueChanged = new GUIStyle(buttonStyle);
            buttonStyleValueChanged.normal.textColor = Palette.ChangedValueText;
            buttonStyleValueChanged.onNormal.textColor = Palette.ChangedValueText;

            categoryHeaderStyleDefault = new GUIStyle(labelStyle)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                stretchWidth = true
            };
            categoryHeaderStyleDefault.padding.top = categoryHeaderStyleDefault.padding.bottom = 1;

            categoryHeaderStyleChanged = new GUIStyle(categoryHeaderStyleDefault);
            categoryHeaderStyleChanged.normal.textColor = Palette.ChangedValueText;
            categoryHeaderStyleChanged.onNormal.textColor = Palette.ChangedValueText;

            pluginHeaderStyle = new GUIStyle(categoryHeaderStyleDefault);

            pluginHeaderStyleActive = new GUIStyle(pluginHeaderStyle);
            pluginHeaderStyleActive.normal.textColor = Palette.ChangedValueText;

            pluginHeaderStyleSplitView = new GUIStyle(labelStyle);
            pluginHeaderStyleSplitView.margin.top = pluginHeaderStyleSplitView.margin.bottom = 2;
            pluginHeaderStyleSplitView.padding = new RectOffset();
            pluginHeaderStyleSplitView.alignment = TextAnchor.MiddleLeft;
            pluginHeaderStyleSplitView.wordWrap = false;

            pluginHeaderStyleSplitViewActive = new GUIStyle(pluginHeaderStyleSplitView);
            pluginHeaderStyleSplitViewActive.normal.textColor = Palette.ChangedValueText;
            pluginHeaderStyleSplitViewActive.onNormal.textColor = Palette.ChangedValueText;
            pluginHeaderStyleSplitViewActive.fontStyle = FontStyle.Bold;

            pluginCategoryStyleSplitView = new GUIStyle(pluginHeaderStyleSplitView);
            pluginCategoryStyleSplitView.margin.top = pluginCategoryStyleSplitView.margin.bottom = 2;
            pluginCategoryStyleSplitView.clipping = TextClipping.Clip;
            pluginCategoryStyleSplitView.hover.textColor = Palette.ChangedValueText;
            pluginCategoryStyleSplitView.hover.background = HeaderBackground;

            pluginCategoryStyleSplitViewActive = new GUIStyle(pluginCategoryStyleSplitView);
            pluginCategoryStyleSplitViewActive.normal.textColor = Palette.ChangedValueText;
            pluginCategoryStyleSplitViewActive.onNormal.textColor = Palette.ChangedValueText;
            pluginCategoryStyleSplitViewActive.fontStyle = FontStyle.Bold;

            toggleStyle = new GUIStyle(GUI.skin.toggle);
            toggleStyle.normal.textColor = Palette.MainText;
            toggleStyle.onNormal.textColor = Palette.MainText;
            toggleStyle.fontSize = fontSize;
            toggleStyle.imagePosition = ImagePosition.ImageLeft;
            toggleStyle.padding.top = 2;
            toggleStyle.padding.left = 16;
            toggleStyle.margin.top = 5;

            toggleStyleValueDefault = new GUIStyle(toggleStyle);
            toggleStyleValueDefault.normal.textColor = Palette.DefaultValueText;
            toggleStyleValueDefault.onNormal.textColor = Palette.DefaultValueText;

            toggleStyleValueChanged = new GUIStyle(toggleStyle);
            toggleStyleValueChanged.normal.textColor = Palette.ChangedValueText;
            toggleStyleValueChanged.onNormal.textColor = Palette.ChangedValueText;

            boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.normal.textColor = Palette.MainText;
            boxStyle.onNormal.textColor = Palette.MainText;
            boxStyle.fontSize = fontSize;

            comboBoxStyle = new GUIStyle(GUI.skin.button);
            comboBoxStyle.normal = boxStyle.normal;
            comboBoxStyle.normal.textColor = Palette.DefaultValueText;
            comboBoxStyle.hover.background = comboBoxStyle.normal.background;
            comboBoxStyle.hover.textColor = Palette.ChangedValueText;
            comboBoxStyle.fontSize = fontSize;
            comboBoxStyle.border = boxStyle.border;
            comboBoxStyle.stretchHeight = true;
            comboBoxStyle.padding.top = 1;
            comboBoxStyle.padding.bottom = 1;
            comboBoxStyle.margin.top = 0;
            comboBoxStyle.margin.bottom = 0;

            backgroundStyle = new GUIStyle(GUI.skin.box);
            backgroundStyle.normal.textColor = Palette.MainText;
            backgroundStyle.fontSize = fontSize;
            backgroundStyle.normal.background = EntryBackground;

            backgroundStyleWithHover = new GUIStyle(backgroundStyle);
            backgroundStyleWithHover.hover.background = TooltipBackground;

            categoryBackgroundStyle = new GUIStyle(backgroundStyle);
            categoryBackgroundStyle.margin.bottom = 6;
            categoryBackgroundStyle.margin.top = 0;

            categoryHeaderBackgroundStyle = new GUIStyle(backgroundStyle);
            categoryHeaderBackgroundStyle.normal.background = HeaderBackground;
            categoryHeaderBackgroundStyle.margin.bottom = 0;
            categoryHeaderBackgroundStyle.padding.bottom = 0;
            categoryHeaderBackgroundStyle.padding.top = 0;

            categoryHeaderBackgroundStyleWithHover = new GUIStyle(categoryHeaderBackgroundStyle);
            categoryHeaderBackgroundStyleWithHover.hover.background = HeaderBackgroundHover;

            settingWindowBackgroundStyle = new GUIStyle(backgroundStyle);
            settingWindowBackgroundStyle.normal.background = SettingWindowBackground;

            categorySplitViewBackgroundStyle = new GUIStyle(backgroundStyleWithHover);
            categorySplitViewBackgroundStyle.padding = new RectOffset();
            categorySplitViewBackgroundStyle.margin = new RectOffset(20, 4, 2, 2);

            tooltipStyle = new GUIStyle(GUI.skin.box);
            tooltipStyle.normal.textColor = Palette.MainText;
            tooltipStyle.fontSize = fontSize;
            tooltipStyle.wordWrap = true;
            tooltipStyle.alignment = TextAnchor.MiddleLeft;
            tooltipStyle.normal.background = TooltipBackground;
            tooltipStyle.padding.left = 10;
            tooltipStyle.padding.right = 10;
            tooltipStyle.richText = false;

            sliderStyle = new GUIStyle(GUI.skin.horizontalSlider);

            thumbStyle = new GUIStyle(GUI.skin.horizontalSliderThumb);

            fileEditorDirectoryStyle = new GUIStyle(toggleStyle);
            fileEditorDirectoryStyle.wordWrap = false;
            fileEditorDirectoryStyle.margin = new RectOffset(4, 4, 4, 4);
            fileEditorDirectoryStyle.fontStyle = FontStyle.Bold;

            fileEditorDirectoryStyleActive = new GUIStyle(fileEditorDirectoryStyle);
            fileEditorDirectoryStyleActive.normal.textColor = Palette.ChangedValueText;
            fileEditorDirectoryStyleActive.onNormal.textColor = Palette.ChangedValueText;

            fileEditorFileStyle = new GUIStyle(labelStyle);
            fileEditorFileStyle.padding.left = 6;
            fileEditorFileStyle.wordWrap = false;
            fileEditorFileStyle.margin.bottom = 2;
            fileEditorFileStyle.margin.top = 2;

            fileEditorFileStyleActive = new GUIStyle(fileEditorFileStyle);
            fileEditorFileStyleActive.normal.textColor = Palette.ChangedValueText;
            fileEditorFileStyleActive.onNormal.textColor = Palette.ChangedValueText;

            fileEditorRenameFileField = new GUIStyle(textStyleValueChanged);
            fileEditorRenameFileField.wordWrap = true;

            fileEditorErrorText = new GUIStyle(labelStyle);
            fileEditorErrorText.normal.textColor = Color.red;

            fileEditorTextArea = new GUIStyle(GUI.skin.textArea);
            fileEditorTextArea.padding = new RectOffset(5, 5, 5, 5);
            fileEditorTextArea.margin = new RectOffset(1, 1, 3, 3);
            fileEditorTextArea.wordWrap = inputs.EditorWordWrap;
            fileEditorTextArea.richText = false;
            fileEditorTextArea.alignment = TextAnchor.UpperLeft;
            fileEditorTextArea.fontSize = inputs.EditorFontSize;
            fileEditorTextArea.normal.textColor = Palette.FileEditorText;

            delimiterLine = new GUIStyle();
            delimiterLine.normal.background = EntryBackground;
            delimiterLine.fixedHeight = 2;

            placeholderText = new GUIStyle(textStyleValueDefault);
            placeholderText.normal.textColor = Color.gray;

            _styleInputs = inputs;
            _stylesInitialized = true;
            // Combo boxes retain style references; numeric and hotkey edits do not.
            SettingFieldDrawer.ClearComboboxCache();
        }

        public static GUIStyle GetWindowStyle() => windowStyle;
        public static GUIStyle GetCategoryStyle(bool isDefaultStyle = true) => isDefaultStyle ? categoryHeaderStyleDefault : categoryHeaderStyleChanged;
        public static GUIStyle GetHeaderStyle(bool isActive) => isActive ? pluginHeaderStyleActive : pluginHeaderStyle;
        public static GUIStyle GetHeaderSplitViewStyle(bool isActivePlugin = false) => isActivePlugin ? pluginHeaderStyleSplitViewActive : pluginHeaderStyleSplitView;
        public static GUIStyle GetCategorySplitViewStyle(bool isActiveCategory = false) => isActiveCategory ? pluginCategoryStyleSplitViewActive : pluginCategoryStyleSplitView;
        public static GUIStyle GetCategorySplitViewBackgroundStyle() => categorySplitViewBackgroundStyle;
        public static GUIStyle GetSliderStyle() => sliderStyle;
        public static GUIStyle GetThumbStyle() => thumbStyle;
        public static GUIStyle GetBoxStyle() => boxStyle;
        public static GUIStyle GetTooltipStyle() => tooltipStyle;
        public static GUIStyle GetBackgroundStyle(bool withHover = false) => withHover ? backgroundStyleWithHover : backgroundStyle;
        public static GUIStyle GetCategoryBackgroundStyle() => categoryBackgroundStyle;
        public static GUIStyle GetCategoryHeaderBackgroundStyle(bool withHover = false) => withHover ? categoryHeaderBackgroundStyleWithHover : categoryHeaderBackgroundStyle;
        public static GUIStyle GetSettingWindowBackgroundStyle() => settingWindowBackgroundStyle;
        public static GUIStyle GetComboBoxStyle() => comboBoxStyle;
        public static GUIStyle GetToggleStyle() => toggleStyle;
        public static GUIStyle GetToggleStyle(bool isDefaultValue = true) => isDefaultValue ? toggleStyleValueDefault : toggleStyleValueChanged;
        public static GUIStyle GetToggleStyle(SettingEntryBase setting) => GetToggleStyle(IsDefaultValue(setting));
        public static GUIStyle GetLabelStyle() => labelStyle;
        public static GUIStyle GetLabelStyle(bool isDefaultValue = true) => isDefaultValue ? labelStyleValueDefault : labelStyleValueChanged;
        public static GUIStyle GetLabelStyle(SettingEntryBase setting) => GetLabelStyle(IsDefaultValue(setting));
        public static GUIStyle GetLabelStyleInfo() => labelStyleInfo;
        public static GUIStyle GetLabelStyleSettingName() => labelStyleSettingName;
        public static GUIStyle GetButtonStyle() => buttonStyle;
        public static GUIStyle GetButtonStyle(bool isDefaultValue = true) => isDefaultValue ? buttonStyleValueDefault : buttonStyleValueChanged;
        public static GUIStyle GetButtonStyle(SettingEntryBase setting) => GetButtonStyle(IsDefaultValue(setting));
        public static GUIStyle GetTextStyle(bool isDefaultValue = true) => isDefaultValue ? textStyleValueDefault : textStyleValueChanged;
        public static GUIStyle GetTextStyle(SettingEntryBase setting) => GetTextStyle(IsDefaultValue(setting));
        public static GUIStyle GetTextStyle(float setting, float defaultValue) => GetTextStyle(setting == defaultValue);
        public static GUIStyle GetTextStyle(Color setting, Color defaultValue) => GetTextStyle(IsEqualColorConfig(setting, defaultValue));
        public static GUIStyle GetFileStyle(bool isActive) => isActive ? fileEditorFileStyleActive : fileEditorFileStyle;
        public static GUIStyle GetDirectoryStyle(bool isActive) => isActive ? fileEditorDirectoryStyleActive : fileEditorDirectoryStyle;
        public static GUIStyle GetFileNameFieldStyle() => fileEditorRenameFileField;
        public static GUIStyle GetFileNameErrorStyle() => fileEditorErrorText;
        public static GUIStyle GetFileEditorTextArea() => fileEditorTextArea;
        public static GUIStyle GetDelimiterLine() => delimiterLine;
        public static GUIStyle GetPlaceholderTextStyle() => placeholderText;
        public static bool IsEqualColorConfig(Color setting, Color defaultValue) => ColorUtility.ToHtmlStringRGBA(setting) == ColorUtility.ToHtmlStringRGBA(defaultValue);
        internal static bool IsDefaultValue(SettingEntryBase setting)
        {
            if (setting?.DefaultValue == null || setting.Get() == null)
                return true;

            try
            {
                return IsEqualConfigValues(setting.SettingType, setting.Get(), setting.DefaultValue);
            }
            catch
            {
                return true;
            }
        }
        internal static bool IsEqualConfigValues(Type type, object value1, object value2)
        {
            return type switch
            {
                Type t when t == typeof(Color) => IsEqualColorConfig((Color)value1, (Color)value2),
                Type t when t == typeof(Vector2) => (Vector2)value1 == (Vector2)value2,
                Type t when t == typeof(Vector3) => (Vector3)value1 == (Vector3)value2,
                Type t when t == typeof(Vector4) => (Vector4)value1 == (Vector4)value2,
                Type t when t == typeof(Quaternion) => (Quaternion)value1 == (Quaternion)value2,
                _ => value1.ToString().Equals(value2.ToString(), StringComparison.OrdinalIgnoreCase)
            };
        }
    }
}
