using System;
using System.Runtime.InteropServices;
using SaveTracker.Resources.LOGIC.Tracking.Linux;
using SaveTracker.Resources.LOGIC.Tracking.Windows;

namespace SaveTracker.Resources.LOGIC.Tracking
{
    public static class GameProcessTrackerFactory
    {
        public static IGameProcessTracker Create()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return new LinuxGameProcessTracker();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return new WindowsGameProcessTracker();
            }
            else
            {
                throw new PlatformNotSupportedException("Only Windows and Linux are supported.");
            }
        }
    }
}
