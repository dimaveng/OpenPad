using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Soundpad;

internal sealed record ReleaseUpdate(string Version, string AssetName, Uri Download, Uri Checksums, long Size);

internal sealed class UpdateService
{
    internal const string Repository="dimaveng/OpenPad";
    internal const string InstalledAsset="OpenPad-update-win-x64.zip", PortableAsset="OpenPad-portable-win-x64.zip";
    internal const long MaxPackage=512L*1024*1024;
    readonly HttpClient client;
    internal UpdateService(HttpClient? client=null) { this.client=client ?? SharedClient; }
    static readonly HttpClient SharedClient=MakeClient();
    static HttpClient MakeClient()
    {
        var client=new HttpClient {Timeout=TimeSpan.FromMinutes(10)};
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OpenPad/"+VersionInfo.Current);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
    internal static bool IsPortable=>File.Exists(Path.Combine(AppContext.BaseDirectory,"portable.marker"));
    internal static Version? StableVersion(string text)=>Regex.IsMatch(text,@"^v?\d+\.\d+\.\d+$") && Version.TryParse(text.TrimStart('v'),out var v)?v:null;
    internal static bool IsNewer(string release,string current)
    {
        var next=StableVersion(release);
        var installed=StableVersion(current.Split('-')[0]);
        return next!=null && installed!=null && (next>installed || next==installed && current.Contains('-'));
    }
    internal static Uri AssetUri(string text,string tag,string name)
    {
        var expected=$"https://github.com/{Repository}/releases/download/{tag}/{name}";
        if(!Uri.TryCreate(text,UriKind.Absolute,out var uri) || uri.AbsoluteUri!=expected)throw new InvalidDataException("Unexpected release asset URL.");
        return uri;
    }
    internal static ReleaseUpdate? ParseRelease(string json,string current,bool portable)
    {
        using var document=JsonDocument.Parse(json);
        var release=document.RootElement;
        if(release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean())return null;
        string tag=release.GetProperty("tag_name").GetString() ?? "";
        if(!IsNewer(tag,current))return null;
        string name=portable?PortableAsset:InstalledAsset;
        var assets=release.GetProperty("assets").EnumerateArray().ToArray();
        JsonElement Find(string filename)
        {
            var matches=assets.Where(a=>a.GetProperty("name").GetString()==filename && a.GetProperty("state").GetString()=="uploaded").ToArray();
            if(matches.Length!=1)throw new InvalidDataException("Missing or duplicate release asset: "+filename);
            return matches[0];
        }
        var package=Find(name); var sums=Find("SHA256SUMS.txt"); long size=package.GetProperty("size").GetInt64();
        if(size<=0 || size>MaxPackage || sums.GetProperty("size").GetInt64()>65536)throw new InvalidDataException("Unexpected release asset size.");
        return new(tag.TrimStart('v'),name,AssetUri(package.GetProperty("browser_download_url").GetString()!,tag,name),AssetUri(sums.GetProperty("browser_download_url").GetString()!,tag,"SHA256SUMS.txt"),size);
    }
    public async Task<ReleaseUpdate?> CheckAsync(string current,bool portable,CancellationToken cancellation=default)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var response=await client.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest",HttpCompletionOption.ResponseHeadersRead,timeout.Token);
        if(response.StatusCode==HttpStatusCode.NotFound)return null;
        response.EnsureSuccessStatusCode();
        return ParseRelease(System.Text.Encoding.UTF8.GetString(await ReadLimited(response,1024*1024,timeout.Token)),current,portable);
    }
    static async Task<byte[]> ReadLimited(HttpResponseMessage response,int limit,CancellationToken cancellation)
    {
        using var stream=await response.Content.ReadAsStreamAsync(cancellation); using var memory=new MemoryStream(); var buffer=new byte[8192]; int n;
        while((n=await stream.ReadAsync(buffer,cancellation))>0) { if(memory.Length+n>limit)throw new InvalidDataException("Response too large."); await memory.WriteAsync(buffer.AsMemory(0,n),cancellation); }
        return memory.ToArray();
    }
    internal static string Checksum(string text,string filename)
    {
        var hashes=text.Split('\n').Select(line=>Regex.Match(line.Trim(),@"^([a-fA-F0-9]{64})\s+\*?(.+)$"))
            .Where(m=>m.Success && m.Groups[2].Value==filename).Select(m=>m.Groups[1].Value.ToUpperInvariant()).ToArray();
        if(hashes.Length!=1)throw new InvalidDataException("Missing or duplicate SHA-256 entry.");
        return hashes[0];
    }
    public async Task<string> DownloadAsync(ReleaseUpdate release,IProgress<double>? progress,CancellationToken cancellation)
    {
        string folder=Path.Combine(Path.GetTempPath(),"OpenPad-Update-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            using var sums=await client.GetAsync(release.Checksums,HttpCompletionOption.ResponseHeadersRead,cancellation); sums.EnsureSuccessStatusCode();
            string hash=Checksum(System.Text.Encoding.UTF8.GetString(await ReadLimited(sums,65536,cancellation)),release.AssetName);
            using var response=await client.GetAsync(release.Download,HttpCompletionOption.ResponseHeadersRead,cancellation); response.EnsureSuccessStatusCode();
            if(response.Content.Headers.ContentLength is long length && length!=release.Size)throw new InvalidDataException("Download size mismatch.");
            string target=Path.Combine(folder,release.AssetName),partial=target+".partial";
            using(var stream=await response.Content.ReadAsStreamAsync(cancellation))
            await using(var output=new FileStream(partial,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true))
            using(var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer=new byte[81920]; long total=0; int n;
                while((n=await stream.ReadAsync(buffer,cancellation))>0)
                {
                    total+=n; if(total>release.Size || total>MaxPackage)throw new InvalidDataException("Download too large.");
                    digest.AppendData(buffer,0,n); await output.WriteAsync(buffer.AsMemory(0,n),cancellation); progress?.Report((double)total/release.Size*100);
                }
                if(total!=release.Size || Convert.ToHexString(digest.GetHashAndReset())!=hash)throw new InvalidDataException("SHA-256 verification failed.");
            }
            File.Move(partial,target); return target;
        }
        catch { Directory.Delete(folder,true); throw; }
    }
    internal static ProcessStartInfo PrepareHandoff(string package,string destination,bool portable,int processId)
    {
        string folder=Path.GetDirectoryName(package)!,script=Path.Combine(folder,"Apply-Update.ps1");
        using(var source=typeof(UpdateService).Assembly.GetManifestResourceStream("OpenPad.Update.ps1")!)
        using(var output=File.Create(script))source.CopyTo(output);
        var info=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe")) {UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=folder};
        foreach(string arg in new[]{"-NoProfile","-ExecutionPolicy","Bypass","-File",script,"-Destination",Path.GetFullPath(destination),"-Payload",Path.GetFullPath(package),"-WaitForProcessId",processId.ToString(),"-Update","-SkipCable"})info.ArgumentList.Add(arg);
        if(portable)info.ArgumentList.Add("-Portable");
        return info;
    }
}
