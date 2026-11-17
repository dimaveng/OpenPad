using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NAudio.Wave;

namespace Soundpad;

internal static class UiSmoke
{
    public static void Run(App app,string output)
    {
        app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        Directory.CreateDirectory(output);
        string data=Path.Combine(Path.GetTempPath(),"OpenPad-UI-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(data);
        Environment.SetEnvironmentVariable("SOUNDPAD_DATA",data);
        var prefs=new Preferences { Language="ru" };
        string fixture=Path.Combine(data,"silence.wav");
        using(var writer=new WaveFileWriter(fixture,new WaveFormat(48000,16,1)))writer.Write(new byte[9600],0,9600);
        for(int i=1;i<=18;i++) prefs.Clips.Add(new Clip { Name=$"Клип {i:00} — тест интерфейса",Path=fixture });
        File.WriteAllText(Path.Combine(data,"library.json"),JsonSerializer.Serialize(prefs));
        var window=new MainWindow { Width=1240,Height=820 };
        window.Loaded+=(_,_)=>window.Dispatcher.BeginInvoke(new Action(()=>
        {
            try
            {
                window.Status.Text="Демонстрационная библиотека · 18 звуков";
                ((Button)window.Tiles.Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(!window.Status.Text.StartsWith(L10n.T("Играет: "),StringComparison.Ordinal))throw new Exception("Clip launch failed");
                window.ClipGain.Value=150; window.Bass.Value=6;
                var clipState=JsonSerializer.Deserialize<Preferences>(File.ReadAllText(Path.Combine(data,"library.json")))!;
                if(clipState.Clips[0].Gain!=150 || clipState.Clips[0].Bass!=6)throw new Exception("Effect controls not saved");
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
                window.Width=1440; window.Height=900;
                Capture(window,Path.Combine(output,"list-large.png"));
                window.Search.Text="07";
                if(window.Rows.Children.Count!=1)throw new Exception("List search failed");
                window.Search.Text=""; window.Width=1020; window.Height=720;
                Capture(window,Path.Combine(output,"list-small.png"));
                foreach(var control in new FrameworkElement[]{window.Output,window.Microphone,window.Monitor,window.VoiceGain,window.LanguagePicker})
                    if(control.ActualWidth<40 || control.ActualHeight<3)throw new Exception("Required control has no layout space");
                var saved=JsonSerializer.Deserialize<Preferences>(File.ReadAllText(Path.Combine(data,"library.json")))!;
                if(saved.ViewMode!="list")throw new Exception("View preference not saved");
                window.Close();
                var restored=new MainWindow();
                restored.Show();
                if(restored.Rows.Visibility!=Visibility.Visible || restored.Rows.Children.Count!=18)throw new Exception("View preference not restored");
                restored.TileMode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(restored.Tiles.Children.Count!=18)throw new Exception("Tiles restore failed");
                restored.SelectionModeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                ((Button)restored.Tiles.Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                ((Button)restored.Tiles.Children[1]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(restored.SelectionCount.Text!="Выбрано: 2")throw new Exception("Multi selection failed");
                restored.ListMode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(restored.SelectionCount.Text!="Выбрано: 2")throw new Exception("Selection lost on view change");
                restored.DeleteSelectionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(restored.Rows.Children.Count!=16)throw new Exception("Bulk removal failed");
                restored.UndoRemovalButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(restored.Rows.Children.Count!=18)throw new Exception("Bulk undo failed");
                restored.Search.Text="07"; restored.SelectVisibleForTest();
                if(restored.SelectionCount.Text!="Выбрано: 1")throw new Exception("Filtered selection failed");
                restored.Search.Text="";
                Capture(restored,Path.Combine(output,"selection.png"));
                restored.Close();
                File.WriteAllText(Path.Combine(output,"ui-test.txt"),"PASS: tiles/list switching, search, save/reload, scrolling, multi selection, view persistence, bulk removal and undo, filtered selection, renders.");
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
