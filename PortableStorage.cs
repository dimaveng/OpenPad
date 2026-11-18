using System.IO;
using System.Security.Cryptography;

namespace Soundpad;

internal static class PortableStorage
{
    internal static string ImportAudio(string path,string dataDirectory)
    {
        string full=Path.GetFullPath(path),data=Path.GetFullPath(dataDirectory);
        if(full.StartsWith(data+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))return full;
        string folder=Path.Combine(data,"Sounds"); Directory.CreateDirectory(folder);
        using var source=File.OpenRead(full); string hash=Convert.ToHexString(SHA256.HashData(source)); source.Position=0;
        string target=Path.Combine(folder,hash+Path.GetExtension(full).ToLowerInvariant());
        if(File.Exists(target))return target;
        string temp=target+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var output=File.Create(temp))source.CopyTo(output);
            try {File.Move(temp,target);} catch(IOException) when(File.Exists(target)) { }
            return target;
        }
        finally {if(File.Exists(temp))File.Delete(temp);}
    }
}
