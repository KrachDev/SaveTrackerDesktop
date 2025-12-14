using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace SaveTracker.Resources.HELPERS
{
    /// <summary>
    /// Helper class for JSON serialization that works with trimming enabled
    /// </summary>
    public static class JsonHelper
    {
        /// <summary>
        /// Gets JsonSerializerOptions with reflection-based serialization enabled for trimmed applications
        /// </summary>
        public static JsonSerializerOptions GetOptions(bool writeIndented = false, bool propertyNameCaseInsensitive = false)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = writeIndented,
                PropertyNameCaseInsensitive = propertyNameCaseInsensitive
            };

            // Enable reflection-based serialization for trimmed applications
            // This allows JsonSerializer to work when trimming is enabled
            options.TypeInfoResolver = new DefaultJsonTypeInfoResolver();

            return options;
        }

        /// <summary>
        /// Default options with indented formatting
        /// </summary>
        public static JsonSerializerOptions DefaultIndented => GetOptions(writeIndented: true);

        /// <summary>
        /// Default options with case-insensitive property names
        /// </summary>
        public static JsonSerializerOptions DefaultCaseInsensitive => GetOptions(propertyNameCaseInsensitive: true);

        /// <summary>
        /// Default options with both indented and case-insensitive
        /// </summary>
        public static JsonSerializerOptions DefaultIndentedCaseInsensitive => GetOptions(writeIndented: true, propertyNameCaseInsensitive: true);
    }
}

