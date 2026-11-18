using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Soundpad;

internal sealed class UpdateWindow : Window
{
    readonly UpdateService service;
    readonly CancellationTokenSource cancellation=new();
    readonly TextBlock message=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,20,0,12)};
    readonly ProgressBar progress=new(){Height=4,Minimum=0,Maximum=100,Margin=new Thickness(0,0,0,18)};
    readonly Button action=new(){HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(12,9,12,9)};
    readonly MainWindow owner;
    ReleaseUpdate? release;
    bool downloading;
    internal event Action? CheckCompleted;
    internal UpdateWindow(MainWindow owner,UpdateService? service=null)
    {
        this.owner=owner; this.service=service ?? new(); Owner=owner; Title=L10n.T("Обновления"); Width=490; Height=290; ResizeMode=ResizeMode.NoResize; WindowStartupLocation=WindowStartupLocation.CenterOwner; WindowStyle=WindowStyle.None;
        System.Windows.Shell.WindowChrome.SetWindowChrome(this,new System.Windows.Shell.WindowChrome {CaptionHeight=45,ResizeBorderThickness=new Thickness(0),GlassFrameThickness=new Thickness(0),UseAeroCaptionButtons=false});
        Background=new SolidColorBrush(Color.FromRgb(21,30,38)); Foreground=new SolidColorBrush(Color.FromRgb(231,237,240)); FontFamily=new FontFamily("Segoe UI"); FontSize=14;
        var panel=new StackPanel {Margin=new Thickness(24)};
        var header=new DockPanel(); var close=new Button {Content="×",FontSize=20,Padding=new Thickness(8,0,8,0),ToolTip=L10n.T("Закрыть")}; DockPanel.SetDock(close,Dock.Right); System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(close,true); close.Click+=(_,_)=>Close(); header.Children.Add(close);
        header.Children.Add(new TextBlock {Text="OpenPad · "+VersionInfo.Current+(UpdateService.IsPortable?" · Portable":""),FontSize=20,FontWeight=FontWeights.SemiBold}); panel.Children.Add(header);
        panel.Children.Add(message); panel.Children.Add(progress); panel.Children.Add(action); Content=new Border {BorderThickness=new Thickness(1),BorderBrush=new SolidColorBrush(Color.FromRgb(51,65,75)),Child=panel};
        action.Click+=async(_,_)=> {if(release==null)await Check(); else await Install();};
        Loaded+=async(_,_)=>await Check(); Closing+=OnClosing;
    }
    void OnClosing(object? sender,CancelEventArgs e) { cancellation.Cancel(); }
    async Task Check()
    {
        action.IsEnabled=false; progress.IsIndeterminate=true; message.Text=L10n.T("Проверяем GitHub Releases…");
        try
        {
            release=await service.CheckAsync(VersionInfo.Current,UpdateService.IsPortable,cancellation.Token);
            message.Text=release==null?L10n.T("Установлена последняя версия."):L10n.F("Доступна версия {0}. Библиотека и настройки сохранятся.",release.Version);
            action.Content=L10n.T(release==null?"Проверить ещё раз":"Скачать и установить");
        }
        catch(OperationCanceledException) { message.Text=L10n.T("Проверка прервана. Можно попробовать ещё раз."); action.Content=L10n.T("Проверить ещё раз"); }
        catch(Exception) { message.Text=L10n.T("Не удалось проверить обновления. Проверь подключение к интернету."); action.Content=L10n.T("Проверить ещё раз"); }
        finally {progress.IsIndeterminate=false; action.IsEnabled=true; CheckCompleted?.Invoke();}
    }
    async Task Install()
    {
        if(downloading)return; downloading=true; action.IsEnabled=false;
        string? package=null;
        try
        {
            message.Text=L10n.T("Скачиваем и проверяем SHA-256…");
            package=await service.DownloadAsync(release!,new Progress<double>(value=>progress.Value=value),cancellation.Token);
            if(!owner.CanApplyUpdate()) { message.Text=L10n.T("Сначала дождись завершения импорта или экспорта."); return; }
            if(MessageBox.Show(this,L10n.T("OpenPad закроется для обновления и запустится снова. Продолжить?"),L10n.T("Обновления"),MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
            if(!owner.SaveBeforeUpdate()) { message.Text=L10n.T("Не удалось сохранить библиотеку. Обновление отменено."); return; }
            _ = Process.Start(UpdateService.PrepareHandoff(package,AppContext.BaseDirectory,UpdateService.IsPortable,Environment.ProcessId)) ?? throw new IOException("Updater did not start.");
            package=null; Close(); owner.Close();
        }
        catch(OperationCanceledException) { message.Text=L10n.T("Обновление отменено."); }
        catch(Exception) {message.Text=L10n.T("Не удалось скачать или проверить обновление. Файлы программы не изменены.");}
        finally
        {
            if(package!=null)try {Directory.Delete(Path.GetDirectoryName(package)!,true);}catch(IOException) { }
            downloading=false; action.IsEnabled=true;
        }
    }
}
