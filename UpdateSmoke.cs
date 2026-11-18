using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Soundpad;

internal static class UpdateSmoke
{
    internal static async Task Run()
    {
        static void Require(bool condition,string error) {if(!condition)throw new Exception(error);}
        Require(UpdateService.IsNewer("v0.7.0","0.6.0"),"Upgrade comparison");
        Require(!UpdateService.IsNewer("v0.6.0","0.6.0") && !UpdateService.IsNewer("v0.5.9","0.6.0"),"Downgrade or same version");
        Require(UpdateService.IsNewer("v0.7.0","0.7.0-rc.1") && !UpdateService.IsNewer("v0.7.0-rc.1","0.6.0"),"Prerelease rules");
        byte[] bytes=Encoding.UTF8.GetBytes("verified fixture"); string hash=Convert.ToHexString(SHA256.HashData(bytes));
        string Release(bool prerelease=false,string host="https://github.com")=>JsonSerializer.Serialize(new {draft=false,prerelease,tag_name="v0.7.0",assets=new[]{UpdateService.InstalledAsset,UpdateService.PortableAsset,"SHA256SUMS.txt"}.Select(name=>new {name,state="uploaded",size=name=="SHA256SUMS.txt"?180:bytes.Length,browser_download_url=$"{host}/dimaveng/OpenPad/releases/download/v0.7.0/{name}"})});
        var installed=UpdateService.ParseRelease(Release(),"0.6.0",false)!; var portable=UpdateService.ParseRelease(Release(),"0.6.0",true)!;
        Require(installed.AssetName==UpdateService.InstalledAsset && portable.AssetName==UpdateService.PortableAsset,"Mode selection");
        Require(UpdateService.ParseRelease(Release(true),"0.6.0",false)==null,"Prerelease accepted");
        Require(UpdateService.ParseRelease(Release(),"0.8.0",false)==null,"Older release accepted");
        try {UpdateService.ParseRelease(Release(host:"https://example.com"),"0.6.0",false); throw new Exception("Foreign asset accepted");}catch(InvalidDataException) { }
        try {UpdateService.Checksum(hash+"  "+installed.AssetName+"\n"+hash+"  "+installed.AssetName,installed.AssetName);throw new Exception("Duplicate checksum accepted");}catch(InvalidDataException) { }
        foreach(bool corrupt in new[]{false,true})
        {
            using var client=new HttpClient(new FixtureHandler(request=>request.RequestUri!.AbsolutePath.EndsWith("SHA256SUMS.txt")?Encoding.UTF8.GetBytes((corrupt?new string('0',64):hash)+"  "+installed.AssetName):bytes));
            var service=new UpdateService(client); string? package=null;
            try
            {
                package=await service.DownloadAsync(installed,null,CancellationToken.None);
                Require(!corrupt,"Corrupt checksum accepted"); Require(File.ReadAllBytes(package).SequenceEqual(bytes),"Downloaded bytes differ");
                var handoff=UpdateService.PrepareHandoff(package,Path.Combine(Path.GetTempPath(),"OpenPad тест with spaces"),false,123);
                Require(handoff.ArgumentList.Contains("-WaitForProcessId") && handoff.ArgumentList.Contains("123") && handoff.CreateNoWindow,"Handoff contract");
                Require(File.ReadAllText(Path.Combine(Path.GetDirectoryName(package)!,"Apply-Update.ps1")).Contains("-and !$NoLaunch"),"Missing embedded updater script");
            }
            catch(InvalidDataException) when(corrupt) { }
            finally {if(package!=null)Directory.Delete(Path.GetDirectoryName(package)!,true);}
        }
        using(var client=new HttpClient(new FixtureHandler(_=>Encoding.UTF8.GetBytes(Release()))))
        {
            Require(await new UpdateService(client).CheckAsync("0.6.0",false)!=null,"API check");
            using var cancelled=new CancellationTokenSource(); cancelled.Cancel();
            try {await new UpdateService(client).DownloadAsync(installed,null,cancelled.Token);throw new Exception("Cancellation ignored");}catch(OperationCanceledException) { }
        }
    }
    sealed class FixtureHandler(Func<HttpRequestMessage,byte[]> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(respond(request))});}
    }
}
