using System;

namespace ConfigurationManager
{
    internal static class HiddenSettingsParser
    {
        internal static bool TryParseLine(string rawLine, out string setting)
        {
            setting = null;
            if (rawLine == null)
                return false;

            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                return false;

            int firstSeparator = line.IndexOf('=');
            int secondSeparator = firstSeparator < 0 ? -1 : line.IndexOf('=', firstSeparator + 1);
            if (firstSeparator <= 0 || secondSeparator <= firstSeparator + 1 || secondSeparator == line.Length - 1)
                return false;

            string pluginGuid = line.Substring(0, firstSeparator).Trim();
            string section = line.Substring(firstSeparator + 1, secondSeparator - firstSeparator - 1).Trim();
            string key = line.Substring(secondSeparator + 1).Trim();
            if (pluginGuid.Length == 0 || section.Length == 0 || key.Length == 0)
                return false;

            setting = $"{pluginGuid}={section}={key}";
            return true;
        }
    }
}
