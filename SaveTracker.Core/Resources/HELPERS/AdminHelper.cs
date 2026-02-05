using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace SaveTracker.Resources.HELPERS
{
    public class AdminHelper
    {
        public static async Task<bool> IsAdministrator()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
#if WINDOWS
                var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return await Task.FromResult(result: principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator));
#else
                return false;
#endif
            }
            else
            {
                // Linux/Mac: Return false for now (or implement specific root checks if needed)
                return await Task.FromResult(false);
            }
        }
    }
}
