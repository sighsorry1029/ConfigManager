namespace ConfigurationManager
{
    /// <summary>
    /// Display metadata consumed by ConfigurationManager through ConfigDescription.Tags.
    /// Nullable values let a tag override only the properties it needs.
    /// </summary>
    internal sealed class ConfigurationManagerAttributes
    {
        public string Category { get; set; }
        public string DispName { get; set; }
        public int? CategoryOrder { get; set; }
        public int? Order { get; set; }
        public bool? Browsable { get; set; }
        public bool? IsAdvanced { get; set; }
        public bool? ReadOnly { get; set; }
        public bool? HideDefaultButton { get; set; }
    }
}
