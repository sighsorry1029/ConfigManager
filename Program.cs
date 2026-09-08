using System;
using System.Collections.Generic;
using System.Reflection;

namespace ConfigurationManager.Tests
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length != 0 && args.Length != 4)
            {
                Console.Error.WriteLine("Usage: HiddenSettingsParser.Tests.exe [assemblyPath corePath managedPath temporaryRoot]");
                return 2;
            }

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

            if (args.Length == 4) ExternalSettingsTests.Run(args, failures);

            if (failures.Count == 0)
            {
                Console.WriteLine("HiddenSettingsParser: all tests passed");
                return 0;
            }

            foreach (string failure in failures)
                Console.Error.WriteLine(failure);

            return 1;
        }

        internal static void RunAttributeTests(Assembly assembly, ICollection<string> failures)
        {
            try
            {
                Type entryType = assembly.GetType("ConfigurationManager.ExternalSettingEntry", true);
                object entry = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(entryType);
                MethodInfo copy = entryType.BaseType.GetMethod("SetFromAttributes", BindingFlags.Instance | BindingFlags.NonPublic);
                copy.Invoke(entry, new object[] { new object[]
                {
                    new FieldTags.ConfigurationManagerAttributes { Order = 12, Category = "field", ReadOnly = true },
                    new PropertyTags.ConfigurationManagerAttributes { Order = 23, Category = "property", ReadOnly = null },
                    new InvalidTags.ConfigurationManagerAttributes()
                }, null });
                Type baseType = entryType.BaseType;
                if ((int)baseType.GetProperty("Order").GetValue(entry) != 23 ||
                    (string)baseType.GetProperty("Category").GetValue(entry) != "property" ||
                    (bool?)baseType.GetProperty("ReadOnly").GetValue(entry) != true ||
                    (int)baseType.GetProperty("CategoryOrder").GetValue(entry) != 42)
                    throw new Exception("Field/property tags, ordering, null preservation or failure isolation changed.");
                Console.WriteLine("PASS external attribute field/property compatibility");
            }
            catch (Exception ex)
            {
                failures.Add("Attribute compatibility: " + ex);
            }
        }

        private static class FieldTags
        {
            public sealed class ConfigurationManagerAttributes
            {
                public int Order;
                public string Category;
                public bool? ReadOnly;
            }
        }

        private static class PropertyTags
        {
            public sealed class ConfigurationManagerAttributes
            {
                public int Order { get; set; }
                public string Category { get; set; }
                public bool? ReadOnly { get; set; }
            }
        }

        private static class InvalidTags
        {
            public sealed class ConfigurationManagerAttributes
            {
                public string Order => throw new InvalidOperationException("Rejected metadata getter");
                public string ReadOnly => "invalid metadata type";
                public int CategoryOrder => 42;
            }
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
