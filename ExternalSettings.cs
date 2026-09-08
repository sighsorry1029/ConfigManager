using BepInEx;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using UnityEngine;

namespace ConfigurationManager
{
    /// <summary>A setting whose value and persistence belong to an external owner.</summary>
    public sealed class ExternalSetting
    {
        public ExternalSetting(string category, string name, string description, Type settingType,
            object defaultValue, Func<object> getter, Action<object> setter)
        {
            if (string.IsNullOrWhiteSpace(category)) throw new ArgumentException("A category is required.", nameof(category));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A setting name is required.", nameof(name));
            SettingType = settingType ?? throw new ArgumentNullException(nameof(settingType));
            if (!Accepts(settingType, defaultValue)) throw new ArgumentException("Default value does not match the setting type.", nameof(defaultValue));
            Category = category;
            Name = name;
            Description = description ?? string.Empty;
            DefaultValue = defaultValue;
            Getter = getter ?? throw new ArgumentNullException(nameof(getter));
            Setter = setter;
        }

        public string Category { get; }
        public string Name { get; }
        public string Description { get; }
        public Type SettingType { get; }
        public object DefaultValue { get; }
        /// <summary>A null setter exposes a live value without allowing edits or Reset.</summary>
        public bool ReadOnly => Setter == null;
        internal Func<object> Getter { get; }
        internal Action<object> Setter { get; }

        internal static bool Accepts(Type type, object value) => value == null
            ? !type.IsValueType || Nullable.GetUnderlyingType(type) != null
            : type.IsInstanceOfType(value);
    }

    // Ordinary external enums have immutable metadata. A derived description
    // attribute may compute its text dynamically and keeps the existing drawer.
    internal sealed class ExternalEnumCache
    {
        private readonly ExternalSettingEntry _entry;
        private readonly Dictionary<object, GUIContent> _labels = new Dictionary<object, GUIContent>();
        private object _currentValue;
        private GUIContent _currentContent;

        internal ExternalEnumCache(ExternalSettingEntry entry)
        {
            _entry = entry;
            Type type = entry.SettingType;
            if (!type.IsEnum) return;
            IsFlags = type.IsDefined(typeof(FlagsAttribute), false);
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
                foreach (CustomAttributeData attribute in field.GetCustomAttributesData())
                    if (attribute.AttributeType != typeof(DescriptionAttribute) &&
                        typeof(DescriptionAttribute).IsAssignableFrom(attribute.AttributeType)) return;

            Values = Enum.GetValues(type);
            Contents = new GUIContent[Values.Count];
            for (int i = 0; i < Values.Count; i++)
            {
                object value = Values[i];
                GUIContent content = SettingFieldDrawer.ObjectToGuiContent(value);
                Contents[i] = content;
                _labels[value] = content;
            }
            if (IsFlags)
            {
                var masks = new List<long>();
                var labels = new List<GUIContent>();
                for (int i = 0; i < Values.Count; i++)
                {
                    long mask = Convert.ToInt64(Values[i]);
                    if (mask == 0) continue;
                    masks.Add(mask);
                    labels.Add(Contents[i]);
                }
                FlagMasks = masks.ToArray();
                FlagContents = labels.ToArray();
            }
            DefaultContent = ContentFor(entry.DefaultValue);
            SelectionChanged = Select;
            IsEligible = true;
        }

        internal bool IsEligible { get; }
        internal bool IsFlags { get; }
        internal IList Values { get; }
        internal GUIContent[] Contents { get; }
        internal long[] FlagMasks { get; }
        internal GUIContent[] FlagContents { get; }
        internal GUIContent DefaultContent { get; }
        internal Action<int> SelectionChanged { get; }

        private GUIContent ContentFor(object value) => _labels.TryGetValue(value, out GUIContent content)
            ? content : SettingFieldDrawer.ObjectToGuiContent(value);

        internal GUIContent GetCurrentContent()
        {
            object value = _entry.Get();
            if (_currentContent == null || !Equals(_currentValue, value))
            {
                _currentValue = value;
                _currentContent = ContentFor(value);
            }
            return _currentContent;
        }

        private void Select(int index)
        {
            if (index >= 0 && index < Values.Count) _entry.Set(Values[index]);
        }

        internal object ChangeFlag(object current, int index, bool enabled)
        {
            long bits = Convert.ToInt64(current);
            long mask = FlagMasks[index];
            return Enum.ToObject(_entry.SettingType, enabled ? bits | mask : bits & ~mask);
        }

        internal void SetFlag(int index, bool enabled)
        {
            if (index >= 0 && index < FlagMasks.Length)
                _entry.Set(ChangeFlag(_entry.Get(), index, enabled));
        }
    }

    internal sealed class ExternalSettingEntry : SettingEntryBase
    {
        private Func<object> _getter;
        private Action<object> _setter;
        private object _lastValue;
        private string _getError;
        private string _setError;
        private string _previewValue;
        private string _previewText;
        private ExternalNumericEditor _numericEditor;

        internal ExternalSettingEntry(BepInPlugin owner, ExternalSetting setting)
        {
            PluginInfo = owner;
            Category = setting.Category;
            DispName = setting.Name;
            Description = setting.Description;
            SettingType = setting.SettingType;
            DefaultValue = setting.DefaultValue;
            ReadOnly = setting.ReadOnly;
            _lastValue = DefaultValue;
            _getter = setting.Getter;
            _setter = setting.Setter;
        }

        public override Type SettingType { get; }
        internal bool IsActive => Volatile.Read(ref _getter) != null;
        internal string ErrorText => _setError ?? _getError;
        internal bool UsesTextEditor => SettingType == typeof(string) ||
            (!SettingType.IsEnum && SettingType != typeof(bool) &&
             (SettingType.IsPrimitive || SettingType == typeof(decimal)));
        internal bool UsesInlineNumericEditor => !SettingType.IsEnum &&
            Type.GetTypeCode(SettingType) >= TypeCode.SByte &&
            Type.GetTypeCode(SettingType) <= TypeCode.Decimal;
        internal ExternalNumericEditor NumericEditor => _numericEditor ??
            (_numericEditor = new ExternalNumericEditor(this));
        internal void ClearSetError() => _setError = null;

        public override object Get()
        {
            Func<object> getter = Volatile.Read(ref _getter);
            if (getter == null) return _lastValue;
            try
            {
                object value = getter();
                if (!ExternalSetting.Accepts(SettingType, value))
                    throw new InvalidOperationException("Getter returned a value of the wrong type.");
                _lastValue = value;
                _getError = null;
            }
            catch (Exception ex)
            {
                _getError = "Could not read setting: " + ex.Message;
            }
            return _lastValue;
        }

        protected override void SetValue(object newVal)
        {
            TrySet(newVal);
        }

        internal bool TrySet(object value)
        {
            Action<object> setter = Volatile.Read(ref _setter);
            if (setter == null)
            {
                _setError = IsActive ? "This setting is read-only." : "This setting is no longer registered.";
                return false;
            }
            try
            {
                if (!ExternalSetting.Accepts(SettingType, value))
                    throw new ArgumentException("Value does not match the setting type.");
                setter(value);
                _setError = null;
                return true;
            }
            catch (Exception ex)
            {
                _setError = "Could not apply setting: " + ex.Message;
                return false;
            }
        }

        internal bool TrySetText(string text)
        {
            if (Volatile.Read(ref _setter) == null) return TrySet(null);
            object value;
            try
            {
                value = SettingType == typeof(string) ? text :
                    TypeDescriptor.GetConverter(SettingType).ConvertFromInvariantString(text);
            }
            catch (Exception ex)
            {
                _setError = "Invalid value: " + ex.Message;
                return false;
            }
            return TrySet(value);
        }

        internal string FormatValue(object value) => value is IFormattable formatted
            ? formatted.ToString(null, CultureInfo.InvariantCulture)
            : value?.ToString() ?? string.Empty;

        internal string GetPreviewText()
        {
            string text = FormatValue(Get());
            if (!string.Equals(text, _previewValue, StringComparison.Ordinal))
            {
                _previewValue = text;
                int end = text.IndexOfAny(new[] { '\r', '\n' });
                int length = Math.Min(end < 0 ? text.Length : end, 100);
                _previewText = text.Substring(0, length) + (length < text.Length ? " ..." : string.Empty);
            }
            return _previewText ?? string.Empty;
        }

        internal bool ShouldBeHidden() => ConfigurationManager.IsSettingHidden(
            $"{PluginInfo.GUID}={Category}={DispName}");

        internal void Deactivate()
        {
            Interlocked.Exchange(ref _getter, null);
            Interlocked.Exchange(ref _setter, null);
        }
    }

    // One buffer per displayed numeric entry. Owner reads refresh clean fields;
    // only Enter or a focus transition submits pending text to the owner.
    internal sealed class ExternalNumericEditor
    {
        private static int _nextControlId;
        private readonly ExternalSettingEntry _entry;
        private object _displayValue;
        private bool _hasDisplayValue;

        internal ExternalNumericEditor(ExternalSettingEntry entry)
        {
            _entry = entry;
            ControlName = "ExternalNumber" + Interlocked.Increment(ref _nextControlId)
                .ToString(CultureInfo.InvariantCulture);
        }

        internal string ControlName { get; }
        internal string Text { get; private set; } = string.Empty;
        internal bool IsFocused { get; private set; }
        internal bool HasPendingText { get; private set; }
        internal bool IsActive => _entry.IsActive;

        internal void Refresh()
        {
            if (!IsFocused && !HasPendingText) ReadValue();
        }

        private void ReadValue()
        {
            object value = _entry.Get();
            if (_hasDisplayValue && Equals(value, _displayValue)) return;
            _displayValue = value;
            _hasDisplayValue = true;
            Text = _entry.FormatValue(value);
        }

        internal void HandleInput(string text, bool focused, bool enter, bool escape)
        {
            bool lostFocus = IsFocused && !focused;
            IsFocused = focused;
            if (escape)
            {
                Cancel();
                return;
            }
            if (!string.Equals(Text, text, StringComparison.Ordinal))
            {
                Text = text;
                HasPendingText = true;
            }
            if (enter || lostFocus)
            {
                if (HasPendingText) Commit();
                else if (lostFocus) Refresh();
            }
        }

        private void Commit()
        {
            if (!HasPendingText || !IsActive || !_entry.TrySetText(Text)) return;
            HasPendingText = false;
            _hasDisplayValue = false;
            ReadValue();
        }

        internal void Cancel()
        {
            IsFocused = false;
            HasPendingText = false;
            _entry.ClearSetError();
            _hasDisplayValue = false;
            ReadValue();
        }

        internal void Reset()
        {
            if (_entry.TrySet(_entry.DefaultValue)) Cancel();
        }
    }

    // Registration never touches Unity or invokes an owner's callbacks. The UI
    // observes Revision and snapshots this registry only when rebuilding its list.
    internal static class ExternalSettingsRegistry
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, Registration> Registrations =
            new Dictionary<string, Registration>(StringComparer.Ordinal);
        private static long _revision;

        internal static long Revision => Interlocked.Read(ref _revision);

        internal static IDisposable Register(string guid, string name, string version,
            IEnumerable<ExternalSetting> settings)
        {
            if (string.IsNullOrWhiteSpace(guid)) throw new ArgumentException("An owner GUID is required.", nameof(guid));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("An owner name is required.", nameof(name));
            if (!System.Version.TryParse(version, out _)) throw new ArgumentException("A valid version is required.", nameof(version));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            var owner = new BepInPlugin(guid, name, version);
            var definitions = new HashSet<Tuple<string, string>>();
            var entries = new List<ExternalSettingEntry>();
            foreach (ExternalSetting setting in settings)
            {
                if (setting == null) throw new ArgumentException("Settings cannot contain null.", nameof(settings));
                if (!definitions.Add(Tuple.Create(setting.Category, setting.Name)))
                    throw new ArgumentException("Setting category/name pairs must be unique.", nameof(settings));
                entries.Add(new ExternalSettingEntry(owner, setting));
            }
            var registration = new Registration(guid, entries.ToArray());
            lock (Sync)
            {
                if (Registrations.TryGetValue(guid, out Registration previous)) previous.Deactivate();
                Registrations[guid] = registration;
                Interlocked.Increment(ref _revision);
            }
            return registration;
        }

        internal static SettingEntryBase[] Snapshot()
        {
            lock (Sync)
                return Registrations.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .SelectMany(pair => pair.Value.Entries).Cast<SettingEntryBase>().ToArray();
        }

        private sealed class Registration : IDisposable
        {
            private readonly string _guid;
            internal readonly ExternalSettingEntry[] Entries;

            internal Registration(string guid, ExternalSettingEntry[] entries)
            {
                _guid = guid;
                Entries = entries;
            }

            internal void Deactivate()
            {
                foreach (ExternalSettingEntry entry in Entries) entry.Deactivate();
            }

            public void Dispose()
            {
                lock (Sync)
                {
                    if (!Registrations.TryGetValue(_guid, out Registration current) ||
                        !ReferenceEquals(current, this)) return;
                    Registrations.Remove(_guid);
                    Deactivate();
                    Interlocked.Increment(ref _revision);
                }
            }
        }
    }
}
