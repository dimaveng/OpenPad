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
 public string ViewMode {get;set;}="tiles";
}
public record Device(int Id,string Name) { public override string ToString()=>Name; }
public partial class MainWindow : Window
{
 public static volatile float SharedGain=1;
 readonly AudioEngine engine=new();
 readonly string dataDir=Environment.GetEnvironmentVariable("SOUNDPAD_DATA") ?? System.IO.Path.Combine(AppContext.BaseDirectory,"Data");
 Preferences prefs=new(); Clip? selected; bool ready, updating, favorites;
 IntPtr handle;
 readonly DispatcherTimer timer=new() { Interval=TimeSpan.FromMilliseconds(60) };
 [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint key);
 [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd,int id);
 public MainWindow()
 {
  InitializeComponent(); Title="OpenPad "+VersionInfo.Current; VersionLabel.Text="OpenPad / "+VersionInfo.Current; Directory.CreateDirectory(dataDir);
  try { if(File.Exists(StatePath)) prefs=JsonSerializer.Deserialize<Preferences>(File.ReadAllText(StatePath)) ?? new(); } catch { Status.Text="Не удалось прочитать настройки. Создана новая библиотека."; }
  Master.Value=prefs.Master; VoiceGain.Value=prefs.VoiceGain; Distort.IsChecked=prefs.Distortion; SharedGain=(float)Master.Value/100;
  Preset.SelectedIndex=0; FillDevices(); ready=true; Render(); Configure(); SettingsChanged(this,new());
  if(Environment.GetEnvironmentVariable("SOUNDPAD_CAPTURE") is string capture)
   Loaded+=(_,_)=>Dispatcher.BeginInvoke(new Action(()=> { var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32); bitmap.Render(this); var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap)); using(var file=File.Create(capture))encoder.Save(file); Close(); }),DispatcherPriority.ApplicationIdle);
  SourceInitialized+=(_,_)=> { handle=new WindowInteropHelper(this).Handle; HwndSource.FromHwnd(handle).AddHook(Hook); int failed=0; for(int i=0;i<10;i++) if(!RegisterHotKey(handle,100+i,0x4003,(uint)(0x30+i))) failed++; if(failed>0) Status.Text=$"Не удалось занять горячие клавиши: {failed}. Возможно, они используются другой программой."; };
  timer.Tick+=(_,_)=> { Level.Value=engine.Peak; MicLevel.Value=engine.MicPeak; }; timer.Start();
  Closing+=(_,_)=> { Save(); timer.Stop(); engine.Dispose(); for(int i=0;i<10;i++) UnregisterHotKey(handle,100+i); };
 }
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
 void Save()
 {
  if(!ready)return;
  prefs.Master=Master.Value; prefs.VoiceGain=VoiceGain.Value; prefs.Distortion=Distort.IsChecked==true;
  prefs.Output=(Output.SelectedItem as Device)?.Name; prefs.Monitor=(Monitor.SelectedItem as Device)?.Name; prefs.Microphone=(Microphone.SelectedItem as Device)?.Name; prefs.Voice=MicEnabled.IsChecked==true;
  try { var temp=StatePath+".tmp"; File.WriteAllText(temp,JsonSerializer.Serialize(prefs,new JsonSerializerOptions { WriteIndented=true })); File.Move(temp,StatePath,true); } catch(Exception ex) { Status.Text="Не удалось сохранить: "+ex.Message; }
 }
 void FillDevices()
 {
  updating=true;
  var outputs=Enumerable.Range(0,WaveOut.DeviceCount).Select(i=>new Device(i,WaveOut.GetCapabilities(i).ProductName)).ToList();
  Output.ItemsSource=outputs; Output.SelectedItem=outputs.FirstOrDefault(d=>d.Name==prefs.Output) ?? outputs.FirstOrDefault(d=>d.Name.Contains("CABLE",StringComparison.OrdinalIgnoreCase)) ?? outputs.FirstOrDefault();
  var monitors=new List<Device>{new(-2,"Не прослушивать")}; monitors.AddRange(outputs); Monitor.ItemsSource=monitors; Monitor.SelectedItem=monitors.FirstOrDefault(d=>d.Name==prefs.Monitor) ?? monitors[0];
  var inputs=Enumerable.Range(0,WaveIn.DeviceCount).Select(i=>new Device(i,WaveIn.GetCapabilities(i).ProductName)).Where(d=>!d.Name.Contains("CABLE",StringComparison.OrdinalIgnoreCase)).ToList(); Microphone.ItemsSource=inputs; Microphone.SelectedItem=inputs.FirstOrDefault(d=>d.Name==prefs.Microphone) ?? inputs.FirstOrDefault(); MicEnabled.IsChecked=prefs.Voice;
  updating=false;
 }
 void Configure()
 {
  if(!ready || updating)return;
  try { if(Output.SelectedItem is not Device d) { Status.Text="Нет устройств вывода. Подключи наушники и обнови список."; return; } engine.Configure(d.Id,(Monitor.SelectedItem as Device)?.Id ?? -2,(Microphone.SelectedItem as Device)?.Id ?? -1,MicEnabled.IsChecked==true); engine.Distortion=Distort.IsChecked==true; Status.Text="Выход: "+d.Name; } catch(Exception e) { Status.Text="Не удалось открыть аудиоустройство: "+e.Message; }
  Save();
  VoiceStatus.Text=engine.VoiceActive ? "Голос + звуки → выбранный выход" : "Микрофон выключен";
 }
 void SetupDiscord(object sender,RoutedEventArgs e)
 {
  Save(); FillDevices();
  var cable=Output.Items.Cast<Device>().FirstOrDefault(d=>d.Name.Contains("CABLE Input",StringComparison.OrdinalIgnoreCase));
  if(cable==null) { Status.Text="VB-CABLE не найден. Установи драйвер и нажми «Обновить устройства»."; return; }
  if(Microphone.SelectedItem==null) { Status.Text="Подключи физический микрофон и обнови устройства."; return; }
  updating=true; Output.SelectedItem=cable; MicEnabled.IsChecked=true; updating=false; Configure();
 }
 void RouteChanged(object sender,RoutedEventArgs e)=>Configure();
 void RefreshDevices(object sender,RoutedEventArgs e) { Save(); FillDevices(); Configure(); }
 void SettingsChanged(object sender,RoutedEventArgs e)
 {
  if(!ready)return; SharedGain=(float)Master.Value/100; MasterLabel.Text=$"Общая громкость · {Master.Value:0}%"; engine.VoiceGain=(float)VoiceGain.Value/100; engine.Distortion=Distort.IsChecked==true; Save();
 }
 void ClipChanged(object sender,RoutedEventArgs e)
 {
  if(!ready || updating)return;
  ClipLabel.Text=$"Громкость клипа · {ClipGain.Value:0}%"; BassLabel.Text=$"Bass Boost · {Bass.Value:0} dB";
  if(selected!=null) { selected.Gain=(float)ClipGain.Value; selected.Bass=(float)Bass.Value; selected.Effects=Effects.IsChecked==true; Save(); }
 }
 void PresetChanged(object sender,SelectionChangedEventArgs e) { if(!ready||updating)return; Bass.Value=new double[]{0,6,12,20}[Math.Max(0,Preset.SelectedIndex)]; }
 void Select(Clip c)
 {
  selected=c; updating=true; SelectedLabel.Text=c.Name; ClipGain.Value=c.Gain; Bass.Value=c.Bass; Effects.IsChecked=c.Effects; updating=false; ClipChanged(this,new());
 }
 void Play(Clip c)
 {
  Select(c);
  try { engine.Play(c); Status.Text="Играет: "+c.Name; } catch(Exception ex) { Status.Text="Не удалось воспроизвести: "+ex.Message; }
 }
 void StopAll(object sender,RoutedEventArgs e) { engine.Stop(); Status.Text="Все звуки остановлены"; }
 void Import(object sender,RoutedEventArgs e)
 {
  var dialog=new OpenFileDialog { Multiselect=true, Filter="Аудиофайлы|*.wav;*.mp3;*.ogg;*.opus;*.aiff;*.wma;*.m4a;*.aac|Ogg Vorbis / Opus|*.ogg;*.opus|Все файлы|*.*" }; if(dialog.ShowDialog()==true) AddFiles(dialog.FileNames);
 }
 void FilesDropped(object sender,DragEventArgs e) { if(e.Data.GetData(DataFormats.FileDrop) is string[] files) AddFiles(files); }
 void AddFiles(IEnumerable<string> files)
 {
  var errors=new List<string>(); int added=0,failed=0; foreach(var file in files)
  {
   if(prefs.Clips.Any(c=>string.Equals(c.Path,file,StringComparison.OrdinalIgnoreCase)))continue;
   try { using var probe=AudioDecoder.Open(file); if(probe.WaveFormat.Channels>2)throw new NotSupportedException(); prefs.Clips.Add(new Clip { Path=file,Name=System.IO.Path.GetFileNameWithoutExtension(file) }); added++; } catch(Exception ex) { failed++; errors.Add(System.IO.Path.GetFileName(file)+": "+ex.Message); }
  }
  if(errors.Count>0)MessageBox.Show(this,string.Join(Environment.NewLine,errors),"Не удалось добавить файлы",MessageBoxButton.OK,MessageBoxImage.Information); Save(); Render(); Status.Text=$"Добавлено: {added}"+(failed>0?$" · Не удалось прочитать: {failed}":"");
 }
 void ShowAll(object sender,RoutedEventArgs e) { favorites=false; Render(); }
 void ShowFavorites(object sender,RoutedEventArgs e) { favorites=true; Render(); }
 void SearchChanged(object sender,TextChangedEventArgs e) { if(ready)Render(); }
 void Render()
 {
  Tiles.Children.Clear(); Rows.Children.Clear(); CountLabel.Text=prefs.Clips.Count.ToString();
  bool list=prefs.ViewMode=="list";
  Tiles.Visibility=list?Visibility.Collapsed:Visibility.Visible; Rows.Visibility=list?Visibility.Visible:Visibility.Collapsed;
  TileMode.Background=list?new SolidColorBrush(Color.FromRgb(38,41,39)):(Brush)FindResource("Accent"); TileMode.Foreground=list?Brushes.White:Brushes.Black;
  ListMode.Background=list?(Brush)FindResource("Accent"):new SolidColorBrush(Color.FromRgb(38,41,39)); ListMode.Foreground=list?Brushes.Black:Brushes.White;
  foreach(var c in prefs.Clips.Where(c=>(!favorites||c.Favorite)&&c.Name.Contains(Search.Text,StringComparison.OrdinalIgnoreCase)))
  {
   var panel=new StackPanel();
   int index=prefs.Clips.IndexOf(c)+1;
   var top=new Grid { Margin=new Thickness(0,0,0,24) };
   top.Children.Add(new TextBlock { Text="▶",Foreground=(Brush)FindResource("Accent"),FontSize=20 });
   top.Children.Add(new TextBlock { Text=index.ToString("00"),Foreground=(Brush)FindResource("Accent"),FontSize=17,HorizontalAlignment=HorizontalAlignment.Right }); panel.Children.Add(top);
   panel.Children.Add(new TextBlock { Text=c.Name,FontSize=16,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis,ToolTip=c.Path });
   panel.Children.Add(new TextBlock { Text=(c.Favorite?"★  ":"")+System.IO.Path.GetExtension(c.Path).TrimStart('.').ToUpperInvariant(),Foreground=Brushes.DarkGray,FontSize=11,Margin=new Thickness(0,8,0,0) });
   FrameworkElement content=panel;
   if(list)
   {
    var row=new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(32) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(46) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(98) });
    row.Children.Add(new TextBlock { Text="▶",Foreground=(Brush)FindResource("Accent"),VerticalAlignment=VerticalAlignment.Center });
    var name=new TextBlock { Text=(c.Favorite?"★  ":"")+c.Name,TextTrimming=TextTrimming.CharacterEllipsis,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,ToolTip=c.Name }; Grid.SetColumn(name,1); row.Children.Add(name);
    var format=new TextBlock { Text=System.IO.Path.GetExtension(c.Path).TrimStart('.').ToUpperInvariant(),FontSize=11,Foreground=Brushes.DarkGray,VerticalAlignment=VerticalAlignment.Center }; Grid.SetColumn(format,2); row.Children.Add(format);
    var hotkey=new TextBlock { Text=index<=9?$"Ctrl+Alt+{index}":"—",FontSize=11,Foreground=Brushes.DarkGray,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center }; Grid.SetColumn(hotkey,3); row.Children.Add(hotkey); content=row;
   }
   var button=new Button { Content=content,Width=list?double.NaN:208,Height=list?58:158,Margin=list?new Thickness(0,0,10,8):new Thickness(0,0,12,12),Padding=new Thickness(16),HorizontalContentAlignment=HorizontalAlignment.Stretch,ToolTip="Нажми для запуска. Правой кнопкой — настройки клипа." }; button.Click+=(_,_)=>Play(c);
   System.Windows.Automation.AutomationProperties.SetName(button,"Воспроизвести "+c.Name);
   var menu=new ContextMenu();
   var edit=new MenuItem { Header="Настроить эффекты" }; edit.Click+=(_,_)=>Select(c); menu.Items.Add(edit);
   var fav=new MenuItem { Header=c.Favorite?"Убрать из избранного":"В избранное" }; fav.Click+=(_,_)=> { c.Favorite=!c.Favorite; Save(); Render(); }; menu.Items.Add(fav);
   var remove=new MenuItem { Header="Убрать из библиотеки" }; remove.Click+=(_,_)=> { prefs.Clips.Remove(c); if(selected==c)selected=null; Save(); Render(); }; menu.Items.Add(remove); button.ContextMenu=menu; if(list)Rows.Children.Add(button); else Tiles.Children.Add(button);
  }
  EmptyLabel.Visibility=Tiles.Children.Count+Rows.Children.Count==0?Visibility.Visible:Visibility.Collapsed;
 }
 void CreateDemo(object sender,RoutedEventArgs e)
 {
  string path=System.IO.Path.Combine(dataDir,"Демо — низкий импульс.wav");
  using(var writer=new WaveFileWriter(path,new WaveFormat(48000,16,1))) { for(int i=0;i<48000;i++) { double t=i/48000.0; writer.WriteSample((float)(0.25*Math.Sin(2*Math.PI*90*t)*Math.Sin(Math.PI*t))); } }
  AddFiles(new[]{path});
 }
}
