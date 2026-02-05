using System;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

namespace SaveTracker.Resources.HELPERS
{
    public static class NetworkHelper
    {
        public static async Task<bool> IsInternetAvailableAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (!NetworkInterface.GetIsNetworkAvailable())
                        return false;

                    // Ping a reliable host to ensure actual connectivity
                    // Google DNS is usually a good choice
                    using (var ping = new Ping())
                    {
                        var reply = ping.Send("8.8.8.8", 1000); // 1s timeout
                        return reply.Status == IPStatus.Success;
                    }
                }
                catch
                {
                    return false;
                }
            });
        }

        public static bool IsInternetAvailable()
        {
            try
            {
                if (!NetworkInterface.GetIsNetworkAvailable())
                    return false;

                using (var ping = new Ping())
                {
                    var reply = ping.Send("8.8.8.8", 1000);
                    return reply.Status == IPStatus.Success;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
