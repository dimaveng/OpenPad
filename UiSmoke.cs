using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Soundpad;

internal static class UiSmoke
{
    public static void Run(App app,string output)
    {
        app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        Directory.CreateDirectory(output);
        string data=Path.Combine(Path.GetTempPath(),"OpenPad-UI-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(data);
        Environment.SetEnvironmentVariable("SOUNDPAD_DATA",data);
        var prefs=new Preferences();
        for(int i=1;i<=18;i++) prefs.Clips.Add(new Clip { Name=$"Клип {i:00} — тест интерфейса",Path=$"sample-{i:00}.wav" });
        File.WriteAllText(Path.Combine(data,"library.json"),JsonSerializer.Serialize(prefs));
        var window=new MainWindow { Width=1240,Height=820 };
        window.Loaded+=(_,_)=>window.Dispatcher.BeginInvoke(new Action(()=>
        {
            try
            {
                window.Status.Text="Демонстрационная библиотека · 18 звуков";
                Capture(window,Path.Combine(output,"tiles.png"));
                foreach(var scroll in Descendants<ScrollViewer>(window).Where(s=>s.ScrollableHeight>0))
                {
                    scroll.ScrollToEnd(); window.UpdateLayout();
                    if(scroll.VerticalOffset<=0)throw new Exception("Vertical scrolling failed");
                    scroll.ScrollToTop(); window.UpdateLayout();
                }
                window.ListMode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(window.Rows.Children.Count!=18 || window.Tiles.Visibility!=Visibility.Collapsed)throw new Exception("List switch failed");
                Capture(window,Path.Combine(output,"list.png"));
                window.Search.Text="07";
                if(window.Rows.Children.Count!=1)throw new Exception("List search failed");
                window.Search.Text=""; window.Width=1020; window.Height=720;
                Capture(window,Path.Combine(output,"list-small.png"));
                var saved=JsonSerializer.Deserialize<Preferences>(File.ReadAllText(Path.Combine(data,"library.json")))!;
                if(saved.ViewMode!="list")throw new Exception("View preference not saved");
                window.Close();
                var restored=new MainWindow();
                if(restored.Rows.Visibility!=Visibility.Visible || restored.Rows.Children.Count!=18)throw new Exception("View preference not restored");
                restored.TileMode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(restored.Tiles.Children.Count!=18)throw new Exception("Tiles restore failed");
                restored.Close();
                File.WriteAllText(Path.Combine(output,"ui-test.txt"),"PASS: tiles/list switching, list search, save/reload, scrolling, 1240x820 and 1020x720 renders.");
                app.Shutdown(0);
            }
            catch(Exception ex) { File.WriteAllText(Path.Combine(output,"ui-test.txt"),ex.ToString()); window.Close(); app.Shutdown(1); }
            finally { Directory.Delete(data,true); }
        }),DispatcherPriority.ApplicationIdle);
        window.Show();
    }
    static void Capture(Window window,string path)
    {
        window.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);
        bitmap.Render(window); var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream=File.Create(path); encoder.Save(stream);
    }
    static IEnumerable<T> Descendants<T>(DependencyObject parent) where T:DependencyObject
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {
            var child=VisualTreeHelper.GetChild(parent,i);
            if(child is T match)yield return match;
            foreach(var descendant in Descendants<T>(child))yield return descendant;
        }
    }
}
