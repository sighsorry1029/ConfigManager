using UnityEngine;

namespace ConfigurationManager
{
    internal readonly struct ThemePalette
    {
        public Color WindowBackground { get; }
        public Color EntryBackground { get; }
        public Color TooltipBackground { get; }
        public Color HeaderBackground { get; }
        public Color HeaderHoverBackground { get; }
        public Color SettingWindowBackground { get; }
        public Color Widget { get; }
        public Color Enabled { get; }
        public Color MainText { get; }
        public Color DefaultValueText { get; }
        public Color ChangedValueText { get; }
        public Color ReadOnlyText { get; }
        public Color FileEditorText { get; }

        private ThemePalette(
            Color windowBackground,
            Color entryBackground,
            Color tooltipBackground,
            Color headerBackground,
            Color headerHoverBackground,
            Color settingWindowBackground,
            Color widget,
            Color enabled,
            Color mainText,
            Color defaultValueText,
            Color changedValueText,
            Color readOnlyText,
            Color fileEditorText)
        {
            WindowBackground = windowBackground;
            EntryBackground = entryBackground;
            TooltipBackground = tooltipBackground;
            HeaderBackground = headerBackground;
            HeaderHoverBackground = headerHoverBackground;
            SettingWindowBackground = settingWindowBackground;
            Widget = widget;
            Enabled = enabled;
            MainText = mainText;
            DefaultValueText = defaultValueText;
            ChangedValueText = changedValueText;
            ReadOnlyText = readOnlyText;
            FileEditorText = fileEditorText;
        }

        public static ThemePalette CreateValheim()
        {
            return new ThemePalette(
                new Color(0f, 0f, 0f, 1f),
                new Color(0.55f, 0.50f, 0.50f, 0.94f),
                new Color(0.55f, 0.50f, 0.45f, 0.95f),
                new Color(0.74f, 0.54f, 0.37f, 0.80f),
                new Color(0.88f, 0.46f, 0f, 0.80f),
                new Color(0.55f, 0.50f, 0.50f, 0.65f),
                new Color(0.88f, 0.46f, 0f, 0.80f),
                new Color(0.88f, 0.46f, 0f, 1f),
                new Color(1f, 0.827f, 0.463f, 1f),
                new Color(1f, 0.827f, 0.463f, 1f),
                new Color(0.90f, 0.90f, 0.90f, 1f),
                Color.gray,
                new Color(0.90f, 0.90f, 0.90f, 1f));
        }
    }
}
