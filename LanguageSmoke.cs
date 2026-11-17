using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Soundpad;

internal static class LanguageSmoke
{
    public static void CheckResources()
    {
        foreach(var entry in L10n.Entries)
        {
            var expected=Regex.Matches(entry.Value["ru"],@"\{\d+(?::[^}]+)?\}").Select(m=>m.Value).Order().ToArray();
            foreach(var language in new[]{"uk","en","ru"})
            {
                if(!entry.Value.TryGetValue(language,out var value) || string.IsNullOrWhiteSpace(value))throw new Exception("Missing translation: "+entry.Key+" / "+language);
                var actual=Regex.Matches(value,@"\{\d+(?::[^}]+)?\}").Select(m=>m.Value).Order().ToArray();
                if(!expected.SequenceEqual(actual))throw new Exception("Translation placeholders differ: "+entry.Key);
            }
        }
        if(L10n.Detect("uk-UA")!="uk" || L10n.Detect("ua")!="uk" || L10n.Detect("ru-RU")!="ru" || L10n.Detect("en-GB")!="en" || L10n.Detect("fr-FR")!="en")throw new Exception("Language detection failed");
        foreach(var language in new[]{"uk","en","ru"})
        {
            L10n.Set(language);
            if(L10n.F("Выбрано: {0}",3)=="Выбрано: {0}")throw new Exception("Format failed");
        }
    }
    public static void Run(App app,string output)
    {
        app.ShutdownMode=ShutdownMode.OnExplicitShutdown; Directory.CreateDirectory(output);
        string data=Path.Combine(Path.GetTempPath(),"OpenPad-Languages-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(data);
        Environment.SetEnvironmentVariable("SOUNDPAD_DATA",data);
        app.Dispatcher.BeginInvoke(new Action(()=>
        {
            MainWindow? window=null;
            try
            {
                CheckResources();
                foreach(var culture in new[]{"uk-UA","en-US","ru-RU","fr-FR"})
                {
                    CultureInfo.CurrentUICulture=CultureInfo.GetCultureInfo(culture);
                    var clip=new Clip { Name="Мій звук / My sound / Мой звук",Path="unchanged.wav",Gain=375,Bass=12,Favorite=true };
                    var prefs=new Preferences { Clips=new(){clip},Master=175,VoiceGain=85,Monitor="Не прослушивать" };
                    File.WriteAllText(Path.Combine(data,"library.json"),JsonSerializer.Serialize(prefs));
                    window=new MainWindow {Width=1020,Height=720}; window.Show(); window.UpdateLayout();
                    string detected=L10n.Detect(culture);
                    if(L10n.Current!=detected || window.LanguagePicker.SelectedIndex!=(detected=="uk"?0:detected=="en"?1:2))throw new Exception("First-start detection failed: "+culture);
                    if(window.TileMode.Content?.ToString()!=L10n.T("Плитки"))throw new Exception("XAML resources not applied");
                    foreach(var language in new[]{"uk","en","ru"})
                    {
                        window.LanguagePicker.SelectedIndex=language=="uk"?0:language=="en"?1:2; window.UpdateLayout();
                        if(L10n.Current!=language || window.TileMode.Content?.ToString()!=L10n.T("Плитки"))throw new Exception("Live switch failed");
                        if(window.Master.Value!=175 || window.VoiceGain.Value!=85)throw new Exception("Language change modified gain");
                        var saved=JsonSerializer.Deserialize<Preferences>(File.ReadAllText(Path.Combine(data,"library.json")))!;
                        if(saved.Language!=language || saved.Clips[0].Name!=clip.Name || saved.Clips[0].Path!=clip.Path || saved.Clips[0].Gain!=375 || !saved.Clips[0].Favorite)throw new Exception("Locale migration changed library");
                        window.SelectionModeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        ((Button)window.Tiles.Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        if(window.SelectionCount.Text!=L10n.F("Выбрано: {0}",1))throw new Exception("Selection translation failed");
                        window.SelectionModeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        if(culture=="uk-UA")Capture(window,Path.Combine(output,language+"-small.png"));
                    }
                    // Manual choice must win over a different system language on the next start.
                    window.LanguagePicker.SelectedIndex=1; window.Close(); window=null;
                    CultureInfo.CurrentUICulture=CultureInfo.GetCultureInfo("uk-UA");
                    window=new MainWindow(); window.Show(); window.UpdateLayout();
                    if(L10n.Current!="en")throw new Exception("Saved preference ignored");
                    window.Close(); window=null;
                }
                File.WriteAllText(Path.Combine(output,"language-test.txt"),"PASS: 3 complete dictionaries, placeholders, first-run detection and fallback, legacy settings migration, live switching, saved preference, unchanged clips/gain, small-window renders.");
                app.Shutdown(0);
            }
            catch(Exception ex) { File.WriteAllText(Path.Combine(output,"language-test.txt"),ex.ToString()); window?.Close(); app.Shutdown(1); }
            finally { Directory.Delete(data,true); }
        }),DispatcherPriority.ApplicationIdle);
    }
    static void Capture(Window window,string path)
    {
        window.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32); bitmap.Render(window);
        var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file=File.Create(path); encoder.Save(file);
    }
}
