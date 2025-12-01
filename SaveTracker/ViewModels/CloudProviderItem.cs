using static CloudConfig;

namespace SaveTracker.ViewModels
{
    public class CloudProviderItem
    {
        public CloudProvider Provider { get; set; }
        public string DisplayName { get; set; } = string.Empty;

        public CloudProviderItem(CloudProvider provider, string displayName)
        {
            Provider = provider;
            DisplayName = displayName;
        }
    }
}
