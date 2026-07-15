using System;
using System.Collections.Generic;

namespace ConfigurationManager.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            var failures = new List<string>();

            ExpectValid("plugin.guid=General=Show HUD", "plugin.guid=General=Show HUD", failures);
            ExpectValid("  plugin.guid = General = Show HUD  ", "plugin.guid=General=Show HUD", failures);
            ExpectValid("plugin.guid=General=Key=With=Equals", "plugin.guid=General=Key=With=Equals", failures);

            ExpectInvalid(null, failures);
            ExpectInvalid(string.Empty, failures);
            ExpectInvalid("   ", failures);
            ExpectInvalid("# comment", failures);
            ExpectInvalid("plugin.guid=General", failures);
            ExpectInvalid("=General=Show HUD", failures);
            ExpectInvalid("plugin.guid==Show HUD", failures);
            ExpectInvalid("plugin.guid=General=", failures);

            if (failures.Count == 0)
            {
                Console.WriteLine("HiddenSettingsParser: all tests passed");
                return 0;
            }

            foreach (string failure in failures)
                Console.Error.WriteLine(failure);

            return 1;
        }

        private static void ExpectValid(string input, string expected, ICollection<string> failures)
        {
            if (!HiddenSettingsParser.TryParseLine(input, out string actual) || actual != expected)
                failures.Add($"Expected valid '{input}' -> '{expected}', got '{actual ?? "<null>"}'");
        }

        private static void ExpectInvalid(string input, ICollection<string> failures)
        {
            if (HiddenSettingsParser.TryParseLine(input, out string actual))
                failures.Add($"Expected invalid '{input ?? "<null>"}', got '{actual}'");
        }
    }
}
