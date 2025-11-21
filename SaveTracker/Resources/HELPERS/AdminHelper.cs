using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SaveTracker.Resources.HELPERS
{
    public class AdminHelper
    {
        public static async Task<bool> IsAdministrator()
        {
            var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return await Task.FromResult(result: principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator));
        }
    }
}
