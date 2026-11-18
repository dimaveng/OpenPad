using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace Soundpad;

public partial class MainWindow
{
    readonly HashSet<Clip> marked=new();
    List<(int Index,Clip Clip)>? lastRemoved;
    bool selectionMode, packageBusy;
    IEnumerable<Clip> VisibleClips()=>prefs.Clips.Where(c=>(!favorites||c.Favorite)&&c.Name.Contains(Search.Text,StringComparison.OrdinalIgnoreCase));
    void ToggleSelection(object sender,RoutedEventArgs e)
    {
        selectionMode=!selectionMode;
        if(!selectionMode)marked.Clear();
        Render();
    }
    void SelectVisible(object sender,RoutedEventArgs e)
    {
        selectionMode=true; foreach(var c in VisibleClips())marked.Add(c); Render();
    }
    internal void SelectVisibleForTest()=>SelectVisible(this,new());
    void ActivateClip(Clip c)
    {
        if(selectionMode || Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            selectionMode=true; if(!marked.Add(c))marked.Remove(c); Render();
        }
        else Play(c);
    }
    void DeleteMarked(object sender,RoutedEventArgs e)=>RemoveClips(marked.ToHashSet());
    void RemoveClips(HashSet<Clip> clips)
    {
        var removed=prefs.Clips.Select((c,i)=>(Index:i,Clip:c)).Where(x=>clips.Contains(x.Clip)).ToList();
        if(removed.Count==0)return;
        var before=prefs.Clips.ToList();
        prefs.Clips.RemoveAll(c=>clips.Contains(c));
        if(!Save()) { prefs.Clips=before; return; }
        lastRemoved=removed; marked.ExceptWith(clips);
        if(selected!=null && clips.Contains(selected)) { selected=null; SelectedLabel.Text=L10n.T("Выбери звук в библиотеке"); }
        Render(); Status.Text=L10n.F("Убрано из библиотеки: {0}. Файлы сохранены. Доступна отмена.",removed.Count);
    }
    void UndoRemoval(object sender,RoutedEventArgs e)
    {
        if(lastRemoved==null)return;
        var before=prefs.Clips.ToList();
        foreach(var item in lastRemoved) prefs.Clips.Insert(Math.Min(item.Index,prefs.Clips.Count),item.Clip);
        if(!Save()) { prefs.Clips=before; return; }
        lastRemoved=null; Render(); Status.Text=L10n.T("Удаление из библиотеки отменено");
    }
    void UpdateSelectionBar()
    {
        marked.RemoveWhere(c=>!prefs.Clips.Contains(c)); SelectionToolbar.Visibility=selectionMode?Visibility.Visible:Visibility.Collapsed; if(selected==null)SelectedFormat.Text="";
        SelectionModeButton.Content=selectionMode?L10n.T("Завершить"):L10n.T("Выбрать звуки");
        SelectionCount.Text=L10n.F("Выбрано: {0}",marked.Count);
        DeleteSelectionButton.IsEnabled=marked.Count>0;
        UndoRemovalButton.IsEnabled=lastRemoved!=null;
        ExportPackageButton.Content=L10n.T("Экспорт .opl"); ExportPackageButton.ToolTip=marked.Count>0?L10n.F("Экспортировать выбранные звуки: {0}",marked.Count):L10n.T("Экспортировать всю библиотеку");
    }
    static Clip CopyClip(Clip c)=>new() { Path=c.Path,Name=c.Name,Gain=c.Gain,Bass=c.Bass,Effects=c.Effects,Favorite=c.Favorite };
    void ImportPackage(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog { Filter="OpenPad Library|*.opl" };
        if(dialog.ShowDialog()==true)_=ImportPackageAsync(dialog.FileName);
    }
    async Task ImportPackageAsync(string path)
    {
        if(packageBusy || audioImportBusy)return;
        packageBusy=true; IsEnabled=false; Status.Text=L10n.T("Проверка и импорт .opl…");
        try
        {
            var existing=prefs.Clips.Select(CopyClip).ToList();
            using var result=await Task.Run(()=>OplPackage.Import(path,dataDir,existing));
            prefs.Clips.AddRange(result.Clips);
            if(!Save()) { foreach(var c in result.Clips)prefs.Clips.Remove(c); return; }
            result.Commit(); Render(); Status.Text=L10n.F("Импорт .opl: добавлено {0}, повторов пропущено {1}",result.Clips.Count,result.Skipped);
        }
        catch(Exception ex) { Status.Text=L10n.T("Импорт .opl не выполнен"); MessageBox.Show(this,L10n.T(ex.Message),L10n.T("Импорт .opl"),MessageBoxButton.OK,MessageBoxImage.Information); }
        finally { packageBusy=false; IsEnabled=true; }
    }
    async void ExportPackage(object sender,RoutedEventArgs e)
    {
        if(packageBusy || audioImportBusy)return;
        var clips=prefs.Clips.Where(c=>marked.Count==0||marked.Contains(c)).Select(CopyClip).ToList();
        if(clips.Count==0) { Status.Text=L10n.T("В библиотеке нет звуков для экспорта"); return; }
        var dialog=new SaveFileDialog { Filter="OpenPad Library|*.opl",DefaultExt=".opl",FileName="OpenPad-library.opl" };
        if(dialog.ShowDialog()!=true)return;
        packageBusy=true; IsEnabled=false; Status.Text=L10n.T("Экспорт .opl…");
        try { await Task.Run(()=>OplPackage.Export(dialog.FileName,clips)); Status.Text=L10n.F("Экспортировано звуков: {0}",clips.Count); }
        catch(Exception ex) { Status.Text=L10n.T("Экспорт .opl не выполнен"); MessageBox.Show(this,L10n.T(ex.Message),L10n.T("Экспорт .opl"),MessageBoxButton.OK,MessageBoxImage.Information); }
        finally { packageBusy=false; IsEnabled=true; }
    }
}
