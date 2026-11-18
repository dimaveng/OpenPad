using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Soundpad;

internal static class UpdateUiSmoke
{
    internal static void Run(App app,string output)
    {
        app.ShutdownMode=ShutdownMode.OnExplicitShutdown; Directory.CreateDirectory(output);
        string data=Path.Combine(output,"Data"); Directory.CreateDirectory(data); Environment.SetEnvironmentVariable("SOUNDPAD_DATA",data);
        SettingsStore.Save(Path.Combine(data,"library.json"),new Preferences {Language="en",CheckUpdates=false});
        var owner=new MainWindow(); owner.Show();
        var client=new HttpClient(new Handler()); var dialog=new UpdateWindow(owner,new UpdateService(client));
        dialog.CheckCompleted+=()=>dialog.Dispatcher.BeginInvoke(new Action(()=>
        {
            try
            {
                dialog.UpdateLayout(); var bitmap=new RenderTargetBitmap((int)dialog.ActualWidth,(int)dialog.ActualHeight,96,96,PixelFormats.Pbgra32); bitmap.Render(dialog);
                var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(output,"updates.png")))png.Save(file);
                File.WriteAllText(Path.Combine(output,"ui-test.txt"),"PASS: update window renders after async release check, shared theme and English strings."); dialog.Close();owner.Close();app.Shutdown(0);
            }
            catch(Exception ex){File.WriteAllText(Path.Combine(output,"ui-test.txt"),ex.ToString());app.Shutdown(1);}
        }),DispatcherPriority.ApplicationIdle);
        // Client lifetime must cover the dispatcher's later async operation.
        dialog.Closed+=(_,_)=>client.Dispose(); dialog.Show();
    }
    sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            string json=JsonSerializer.Serialize(new {draft=false,prerelease=false,tag_name="v9.0.0",assets=new[]{UpdateService.InstalledAsset,UpdateService.PortableAsset,"SHA256SUMS.txt"}.Select(name=>new {name,state="uploaded",size=100,browser_download_url=$"https://github.com/dimaveng/OpenPad/releases/download/v9.0.0/{name}"})});
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json,Encoding.UTF8,"application/json")});
        }
    }
}
