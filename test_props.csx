using System;
using PeNet;
using System.Linq;

var peFile = new PeNet.PeFile("/media/sf_SahredFolder/PYS/STQA/dist/STQA.exe");

if (peFile.ImageResourceDirectory != null)
{
    foreach (var typeEntry in peFile.ImageResourceDirectory.DirectoryEntries)
    {
        if (typeEntry.Id == 3) // RT_ICON
        {
            if (typeEntry.ResourceDirectory?.DirectoryEntries != null)
            {
                var idEntry = typeEntry.ResourceDirectory.DirectoryEntries.FirstOrDefault();
                if (idEntry?.ResourceDirectory?.DirectoryEntries != null)
                {
                    var langEntry = idEntry.ResourceDirectory.DirectoryEntries.FirstOrDefault();
                    if (langEntry?.ResourceDataEntry != null)
                    {
                        Console.WriteLine("ImageResourceDataEntry properties:");
                        foreach (var prop in langEntry.ResourceDataEntry.GetType().GetProperties())
                        {
                            Console.WriteLine($"  {prop.Name}: {prop.GetValue(langEntry.ResourceDataEntry)}");
                        }
                        break;
                    }
                }
            }
            break;
        }
    }
}
