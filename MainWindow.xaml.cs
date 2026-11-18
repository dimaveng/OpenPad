using System.IO;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using NAudio.Wave;

namespace Soundpad;
public sealed class Clip
{
 public string Path {get;set;}="";
 public string Name {get;set;}="";
 public float Gain {get;set;}=100;
 public float Bass {get;set;}
 public bool Effects {get;set;}=true;
 public bool Favorite {get;set;}
}
public sealed class Preferences
{
 public List<Clip> Clips {get;set;}=new();
 public double Master {get;set;}=100;
 public bool Distortion {get;set;}
 public string? Output {get;set;}
 public string? Monitor {get;set;}
 public string? Microphone {get;set;}
 public bool Voice {get;set;}
 public double VoiceGain {get;set;}=100;
 public bool CheckUpdates {get;set;}=true;
 public string ViewMode {get;set;}="tiles";
 public string? Language {get;set;}
}
public record Device(int Id,string Name) { public override string ToString()=>Name; }
public partial class MainWindow : Window
{
 public static volatile float SharedGain=1;
 readonly AudioEngine engine=new();
 readonly string dataDir=Environment.GetEnvironmentVariable("SOUNDPAD_DATA") ?? System.IO.Path.Combine(AppContext.BaseDirectory,"Data");
 Preferences prefs=new(); Clip? selected; bool ready, updating, favorites, audioImportBusy;
 IntPtr handle;
 readonly DispatcherTimer timer=new() { Interval=TimeSpan.FromMilliseconds(60) };
 [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint key);
 [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd,int id);
 public MainWindow()
 {
  InitializeComponent(); Title="OpenPad "+VersionInfo.Current; VersionLabel.Text=VersionInfo.Current; Directory.CreateDirectory(dataDir);
  prefs=SettingsStore.Load(StatePath,UpdateService.IsPortable);
  AutoUpdates.IsChecked=prefs.CheckUpdates;
  ApplyLanguage(); Master.Value=prefs.Master; VoiceGain.Value=prefs.VoiceGain; Distort.IsChecked=prefs.Distortion; SharedGain=(float)Master.Value/100;
  Preset.SelectedIndex=0; FillDevices(); ready=true; Render(); Configure(); SettingsChanged(this,new()); if(prefs.Clips.FirstOrDefault() is Clip initial)Select(initial);
  if(Environment.GetEnvironmentVariable("SOUNDPAD_CAPTURE") is string capture)
   Loaded+=(_,_)=>Dispatcher.BeginInvoke(new Action(()=> { var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32); bitmap.Render(this); var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap)); using(var file=File.Create(capture))encoder.Save(file); Close(); }),DispatcherPriority.ApplicationIdle);
  SourceInitialized+=(_,_)=> { handle=new WindowInteropHelper(this).Handle; HwndSource.FromHwnd(handle).AddHook(Hook); int failed=0; for(int i=0;i<10;i++) if(!RegisterHotKey(handle,100+i,0x4003,(uint)(0x30+i))) failed++; if(failed>0) Status.Text=L10n.F("Не удалось занять горячие клавиши: {0}. Возможно, они используются другой программой.",failed); };
  timer.Tick+=(_,_)=> { Level.Value=engine.Peak; MicLevel.Value=engine.MicPeak; VoiceStatus.Text=L10n.T(engine.VoiceActive?"Голос + звуки → выбранный выход":"Микрофон выключен"); var audioError=Interlocked.Exchange(ref engine.LastError,null); if(audioError!=null)Status.Text=L10n.T("Не удалось воспроизвести: ")+L10n.T(audioError); }; timer.Start();
  Closing+=(_,e)=> { if(packageBusy || audioImportBusy) { e.Cancel=true; return; } Save(); timer.Stop(); engine.Dispose(); for(int i=0;i<10;i++) UnregisterHotKey(handle,100+i); };
  if(Environment.GetEnvironmentVariable("SOUNDPAD_DATA")==null && Environment.GetEnvironmentVariable("SOUNDPAD_CAPTURE")==null)
   Loaded+=async(_,_)=> { if(!prefs.CheckUpdates)return; try {var release=await new UpdateService().CheckAsync(VersionInfo.Current,UpdateService.IsPortable); if(IsLoaded && release!=null) { UpdatesButton.Content=L10n.F("Обновить до {0}",release.Version); Status.Text=L10n.F("Доступна версия {0}. Библиотека и настройки сохранятся.",release.Version); }} catch { /* Offline startup must remain quiet. Manual check reports errors. */ } };
 }
 internal bool CanApplyUpdate()=>!packageBusy && !audioImportBusy;
 internal bool SaveBeforeUpdate()=>Save();
 void OpenUpdates(object sender,RoutedEventArgs e) {new UpdateWindow(this).ShowDialog();}
 void UpdatePreferenceChanged(object sender,RoutedEventArgs e) {if(!ready)return; prefs.CheckUpdates=AutoUpdates.IsChecked==true; Save();}
 string StatePath=>System.IO.Path.Combine(dataDir,"library.json");
 void MinimizeWindow(object sender,RoutedEventArgs e)=>SystemCommands.MinimizeWindow(this);
 void MaximizeWindow(object sender,RoutedEventArgs e) { if(WindowState==WindowState.Maximized)SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this); }
 void CloseWindow(object sender,RoutedEventArgs e)=>Close();
 void ShowTiles(object sender,RoutedEventArgs e) { prefs.ViewMode="tiles"; Save(); Render(); }
 void ShowList(object sender,RoutedEventArgs e) { prefs.ViewMode="list"; Save(); Render(); }
 IntPtr Hook(IntPtr hwnd,int msg,IntPtr wp,IntPtr lp,ref bool handled)
 {
  if(msg==0x0312) { int n=wp.ToInt32()-100; if(n==0) StopAll(this,new()); else if(n<=prefs.Clips.Count) Play(prefs.Clips[n-1]); handled=true; } return IntPtr.Zero;
 }
 bool Save()
 {
  if(!ready)return false;
  prefs.Master=Master.Value; prefs.VoiceGain=VoiceGain.Value; prefs.Distortion=Distort.IsChecked==true;
  prefs.Output=(Output.SelectedItem as Device)?.Name; prefs.Monitor=(Monitor.SelectedItem as Device)?.Name; prefs.Microphone=(Microphone.SelectedItem as Device)?.Name; prefs.Voice=MicEnabled.IsChecked==true;
  try { SettingsStore.Save(StatePath,prefs,UpdateService.IsPortable); return true; } catch(Exception ex) { Status.Text=L10n.T("Не удалось сохранить: ")+L10n.T(ex.Message); return false; }
 }
 void FillDevices()
 {
  updating=true;
  var outputs=Enumerable.Range(0,WaveOut.DeviceCount).Select(i=>new Device(i,WaveOut.GetCapabilities(i).ProductName)).ToList();
  Output.ItemsSource=outputs; Output.SelectedItem=outputs.FirstOrDefault(d=>d.Name==prefs.Output) ?? outputs.FirstOrDefault(d=>d.Name.Contains("CABLE",StringComparison.OrdinalIgnoreCase)) ?? outputs.FirstOrDefault();
  var monitors=new List<Device>{new(-2,L10n.T("Не прослушивать"))}; monitors.AddRange(outputs); Monitor.ItemsSource=monitors; Monitor.SelectedItem=monitors.FirstOrDefault(d=>d.Name==prefs.Monitor) ?? monitors[0];
  var inputs=Enumerable.Range(0,WaveIn.DeviceCount).Select(i=>new Device(i,WaveIn.GetCapabilities(i).ProductName)).Where(d=>!d.Name.Contains("CABLE",StringComparison.OrdinalIgnoreCase)).ToList(); Microphone.ItemsSource=inputs; Microphone.SelectedItem=inputs.FirstOrDefault(d=>d.Name==prefs.Microphone) ?? inputs.FirstOrDefault(); MicEnabled.IsChecked=prefs.Voice;
  updating=false;
 }
 void Configure()
 {
  if(!ready || updating)return;
  try { if(Output.SelectedItem is not Device d) { Status.Text=L10n.T("Нет устройств вывода. Подключи наушники и обнови список."); return; } engine.Configure(d.Id,(Monitor.SelectedItem as Device)?.Id ?? -2,(Microphone.SelectedItem as Device)?.Id ?? -1,MicEnabled.IsChecked==true); engine.Distortion=Distort.IsChecked==true; Status.Text=L10n.T("Выход: ")+d.Name; } catch(Exception e) { Status.Text=L10n.T("Не удалось открыть аудиоустройство: ")+L10n.T(e.Message); }
  Save();
  VoiceStatus.Text=engine.VoiceActive ? L10n.T("Голос + звуки → выбранный выход") : L10n.T("Микрофон выключен");
 }
 void SetupDiscord(object sender,RoutedEventArgs e)
 {
  Save(); FillDevices();
  var cable=Output.Items.Cast<Device>().FirstOrDefault(d=>d.Name.Contains("CABLE Input",StringComparison.OrdinalIgnoreCase));
  if(cable==null) { Status.Text=L10n.T("VB-CABLE не найден. Установи драйвер и нажми «Обновить устройства»."); return; }
  if(Microphone.SelectedItem==null) { Status.Text=L10n.T("Подключи физический микрофон и обнови устройства."); return; }
  updating=true; Output.SelectedItem=cable; MicEnabled.IsChecked=true; updating=false; Configure();
 }
 void RouteChanged(object sender,RoutedEventArgs e)=>Configure();
 void RefreshDevices(object sender,RoutedEventArgs e) { Save(); FillDevices(); Configure(); }
 void SettingsChanged(object sender,RoutedEventArgs e)
 {
  if(!ready)return; SharedGain=(float)Master.Value/100; MasterLabel.Text=L10n.F("Общая громкость · {0:0}%",Master.Value); engine.VoiceGain=(float)VoiceGain.Value/100; engine.Distortion=Distort.IsChecked==true; Save();
 }
 void ClipChanged(object sender,RoutedEventArgs e)
 {
  if(!ready || updating)return;
  ClipLabel.Text=L10n.F("Громкость клипа · {0:0}%",ClipGain.Value); BassLabel.Text=$"Bass Boost · {Bass.Value:0} dB";
  updating=true; Preset.SelectedIndex=Array.IndexOf(new double[]{0,6,12,20},Bass.Value); updating=false;
  if(selected!=null) { selected.Gain=(float)ClipGain.Value; selected.Bass=(float)Bass.Value; selected.Effects=Effects.IsChecked==true; Save(); }
 }
 void PresetChanged(object sender,SelectionChangedEventArgs e) { if(!ready||updating)return; Bass.Value=new double[]{0,6,12,20}[Math.Max(0,Preset.SelectedIndex)]; }
 void Select(Clip c)
 {
  selected=c; updating=true; SelectedLabel.Text=c.Name; ClipGain.Value=c.Gain; Bass.Value=c.Bass; Effects.IsChecked=c.Effects; Preset.SelectedIndex=Array.IndexOf(new float[]{0,6,12,20},c.Bass); SelectedFormat.Text=System.IO.Path.GetExtension(c.Path).TrimStart('.').ToUpperInvariant(); updating=false; ClipChanged(this,new()); Render();
 }
 void Play(Clip c)
 {
  Select(c);
  try { engine.Play(c); Status.Text=L10n.T("Играет: ")+c.Name; } catch(Exception ex) { Status.Text=L10n.T("Не удалось воспроизвести: ")+L10n.T(ex.Message); }
 }
 void StopAll(object sender,RoutedEventArgs e) { engine.Stop(); Status.Text=L10n.T("Все звуки остановлены"); }
 void Import(object sender,RoutedEventArgs e)
 {
  var dialog=new OpenFileDialog { Multiselect=true, Filter=L10n.T("Аудио и OpenPad|*.opl;*.wav;*.mp3;*.ogg;*.opus;*.aiff;*.wma;*.m4a;*.aac|Ogg Vorbis / Opus|*.ogg;*.opus|Все файлы|*.*") }; if(dialog.ShowDialog()==true) AddFiles(dialog.FileNames);
 }
 void FilesDropped(object sender,DragEventArgs e) { if(e.Data.GetData(DataFormats.FileDrop) is string[] files) AddFiles(files); }
 async void AddFiles(IEnumerable<string> files)
 {
  if(packageBusy || audioImportBusy)return;
  var paths=files.ToList();
  foreach(var path in paths.Where(p=>System.IO.Path.GetExtension(p).Equals(".opl",StringComparison.OrdinalIgnoreCase)))await ImportPackageAsync(path);
  var audio=paths.Where(p=>!System.IO.Path.GetExtension(p).Equals(".opl",StringComparison.OrdinalIgnoreCase)).ToList();
  if(audio.Count==0)return;
  var errors=new List<string>(); int added=0,failed=0; foreach(var file in audio)
  {
   if(prefs.Clips.Any(c=>string.Equals(c.Path,file,StringComparison.OrdinalIgnoreCase)))continue;
   try
   {
    using(var probe=AudioDecoder.Open(file))if(probe.WaveFormat.Channels>2)throw new NotSupportedException();
    string stored=file;
    if(UpdateService.IsPortable) {audioImportBusy=true; try {stored=await System.Threading.Tasks.Task.Run(()=>PortableStorage.ImportAudio(file,dataDir));} finally {audioImportBusy=false;}}
    if(prefs.Clips.Any(c=>string.Equals(c.Path,stored,StringComparison.OrdinalIgnoreCase)))continue;
    prefs.Clips.Add(new Clip { Path=stored,Name=System.IO.Path.GetFileNameWithoutExtension(file) }); added++;
   } catch(Exception ex) { failed++; errors.Add(System.IO.Path.GetFileName(file)+": "+L10n.T(ex.Message)); }
  }
  if(errors.Count>0)MessageBox.Show(this,string.Join(Environment.NewLine,errors),L10n.T("Не удалось добавить файлы"),MessageBoxButton.OK,MessageBoxImage.Information); Save(); Render(); Status.Text=L10n.F("Добавлено: {0}",added)+(failed>0?L10n.F(" · Не удалось прочитать: {0}",failed):"");
 }
 void ShowAll(object sender,RoutedEventArgs e) { favorites=false; Render(); }
 void ShowFavorites(object sender,RoutedEventArgs e) { favorites=true; Render(); }
 void SearchChanged(object sender,TextChangedEventArgs e) { if(ready)Render(); }
 void Render()
 {
  Tiles.Children.Clear(); Rows.Children.Clear(); CountLabel.Text=prefs.Clips.Count.ToString();
  bool list=prefs.ViewMode=="list";
  Tiles.Visibility=list?Visibility.Collapsed:Visibility.Visible; Rows.Visibility=list?Visibility.Visible:Visibility.Collapsed;
  TileMode.Background=list?Brushes.Transparent:new SolidColorBrush(Color.FromRgb(36,51,61)); TileMode.Foreground=list?new SolidColorBrush(Color.FromRgb(132,153,167)):(Brush)FindResource("Accent");
  ListMode.Background=list?new SolidColorBrush(Color.FromRgb(36,51,61)):Brushes.Transparent; ListMode.Foreground=list?(Brush)FindResource("Accent"):new SolidColorBrush(Color.FromRgb(132,153,167));
  AllButton.Background=!favorites?new SolidColorBrush(Color.FromRgb(36,51,59)):Brushes.Transparent; FavoritesButton.Background=favorites?new SolidColorBrush(Color.FromRgb(36,51,59)):Brushes.Transparent; ClipGain.IsEnabled=Bass.IsEnabled=Preset.IsEnabled=Effects.IsEnabled=selected!=null; UpdateSelectionBar();
  foreach(var c in VisibleClips())
  {
   var panel=new StackPanel();
   int index=prefs.Clips.IndexOf(c)+1;
   var top=new Grid { Margin=new Thickness(0,0,0,24) };
   top.Children.Add(new TextBlock { Text=selectionMode?(marked.Contains(c)?"☑":"☐"):"▶",Foreground=(Brush)FindResource("Accent"),FontSize=20 });
   top.Children.Add(new TextBlock { Text=index.ToString("00"),Foreground=(Brush)FindResource("Accent"),FontSize=17,HorizontalAlignment=HorizontalAlignment.Right }); panel.Children.Add(top);
   panel.Children.Add(new TextBlock { Text=c.Name,FontSize=14,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis,ToolTip=c.Path });
   panel.Children.Add(new TextBlock { Text=(c.Favorite?"★  ":"")+System.IO.Path.GetExtension(c.Path).TrimStart('.').ToUpperInvariant(),Foreground=new SolidColorBrush(Color.FromRgb(132,153,167)),FontSize=11,Margin=new Thickness(0,8,0,0) });
   FrameworkElement content=panel;
   if(list)
   {
    var row=new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(32) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(46) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(98) });
    row.Children.Add(new TextBlock { Text=selectionMode?(marked.Contains(c)?"☑":"☐"):"▶",Foreground=marked.Contains(c)||ReferenceEquals(selected,c)?(Brush)FindResource("Accent"):new SolidColorBrush(Color.FromRgb(110,132,145)),VerticalAlignment=VerticalAlignment.Center });
    var name=new TextBlock { Text=(c.Favorite?"★  ":"")+c.Name,TextTrimming=TextTrimming.CharacterEllipsis,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,ToolTip=c.Name }; Grid.SetColumn(name,1); row.Children.Add(name);
    var format=new TextBlock { Text=System.IO.Path.GetExtension(c.Path).TrimStart('.').ToUpperInvariant(),FontSize=11,Foreground=new SolidColorBrush(Color.FromRgb(132,153,167)),VerticalAlignment=VerticalAlignment.Center }; Grid.SetColumn(format,2); row.Children.Add(format);
    var hotkey=new TextBlock { Text=index<=9?$"Ctrl+Alt+{index}":"—",FontSize=11,Foreground=new SolidColorBrush(Color.FromRgb(132,153,167)),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center }; Grid.SetColumn(hotkey,3); row.Children.Add(hotkey); content=row;
   }
   var button=new Button { Content=content,Width=list?double.NaN:192,Height=list?60:142,Margin=list?new Thickness(0,0,8,4):new Thickness(0,0,12,12),Padding=new Thickness(16),HorizontalContentAlignment=HorizontalAlignment.Stretch,ToolTip=selectionMode?L10n.T("Выбрать / снять выбор"):L10n.T("Нажми для запуска. Ctrl+клик — выбрать. Правой кнопкой — настройки клипа.") }; button.Click+=(_,_)=>ActivateClip(c);
   button.Background=marked.Contains(c)||ReferenceEquals(selected,c)?new SolidColorBrush(Color.FromRgb(32,46,52)):list?Brushes.Transparent:new SolidColorBrush(Color.FromRgb(23,35,44));
   System.Windows.Automation.AutomationProperties.SetName(button,(selectionMode?L10n.T("Выбрать "):L10n.T("Воспроизвести "))+c.Name);
   var menu=new ContextMenu();
   var edit=new MenuItem { Header=L10n.T("Настроить эффекты") }; edit.Click+=(_,_)=>Select(c); menu.Items.Add(edit);
   var fav=new MenuItem { Header=c.Favorite?L10n.T("Убрать из избранного"):L10n.T("В избранное") }; fav.Click+=(_,_)=> { c.Favorite=!c.Favorite; Save(); Render(); }; menu.Items.Add(fav);
   var remove=new MenuItem { Header=L10n.T("Убрать из библиотеки") }; remove.Click+=(_,_)=>RemoveClips(new HashSet<Clip>{c}); menu.Items.Add(remove); button.ContextMenu=menu; if(list)Rows.Children.Add(button); else Tiles.Children.Add(button);
  }
  EmptyLabel.Visibility=Tiles.Children.Count+Rows.Children.Count==0?Visibility.Visible:Visibility.Collapsed;
 }
 void CreateDemo(object sender,RoutedEventArgs e)
 {
  string path=System.IO.Path.Combine(dataDir,"OpenPad-demo.wav");
  using(var writer=new WaveFileWriter(path,new WaveFormat(48000,16,1))) { for(int i=0;i<48000;i++) { double t=i/48000.0; writer.WriteSample((float)(0.25*Math.Sin(2*Math.PI*90*t)*Math.Sin(Math.PI*t))); } }
  AddFiles(new[]{path});
 }
}
