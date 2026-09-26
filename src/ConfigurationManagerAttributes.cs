namespace DropSafety
{
    /// <summary>
    /// Display hints for the ConfigurationManager plugin, passed as a ConfigDescription tag.
    /// ConfigurationManager matches this class by name and copies its public fields.
    /// Higher Order is listed first.
    /// </summary>
    internal sealed class ConfigurationManagerAttributes
    {
        public int? Order;
    }
}
