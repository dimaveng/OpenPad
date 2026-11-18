using System.Windows;
namespace Soundpad;
public partial class App : Application
{
 protected override void OnStartup(StartupEventArgs e)
 {
  DispatcherUnhandledException+=(_,args)=> { System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory,"error.log"),args.Exception.ToString()); args.Handled=true; Shutdown(1); };
  if(e.Args.Contains("--update-test")) { try { System.Threading.Tasks.Task.Run(UpdateSmoke.Run).GetAwaiter().GetResult(); System.IO.File.WriteAllText("update-test.txt","PASS: release versions, stable channel, asset modes/URLs, checksum integrity, cancellation, download and handoff."); Shutdown(0); } catch(Exception ex) {System.IO.File.WriteAllText("update-test.txt",ex.ToString()); Shutdown(1);} return; }
  if(e.Args.Contains("--stability-test")) { try { StabilitySmoke.Run("stability-test.txt"); Shutdown(0); } catch { Shutdown(1); } return; }
  if(e.Args.Contains("--device-soak-test")) { try { StabilitySmoke.DeviceSoak("device-soak-test.txt"); Shutdown(0); } catch { Shutdown(1); } return; }
  if(e.Args.Length==2 && e.Args[0]=="--ui-test") { UiSmoke.Run(this,e.Args[1]); return; }
  if(e.Args.Length==2 && e.Args[0]=="--language-ui-test") { LanguageSmoke.Run(this,e.Args[1]); return; }
  if(e.Args.Contains("--language-test")) { try { LanguageSmoke.CheckResources(); System.IO.File.WriteAllText("language-test.txt","PASS: complete uk/en/ru dictionaries, format placeholders and detection/fallback."); Shutdown(0); } catch(Exception ex) { System.IO.File.WriteAllText("language-test.txt",ex.ToString()); Shutdown(1); } return; }
  if(e.Args.Contains("--opl-test")) { try { OplSmoke.Run(); System.IO.File.WriteAllText("opl-test.txt","PASS: portable round trip, order and settings, duplicate skip, rollback, failed export preservation, malformed/version/traversal/checksum/extra entry rejection."); Shutdown(0); } catch(Exception ex) { System.IO.File.WriteAllText("opl-test.txt",ex.ToString()); Shutdown(1); } return; }
  if(e.Args.Contains("--self-test")) { try { AudioEngine.SelfTest(e.Args.SkipWhile(a=>a!="--self-test").Skip(1).FirstOrDefault()); System.IO.File.WriteAllText("self-test.txt","PASS: decoding, mono-to-stereo, resampling, 500% gain, bass boost, stop." + (e.Args.Length > 1 ? " Ogg: import, full decode, effects, EOF." : "")); Shutdown(0); } catch(Exception ex) { System.IO.File.WriteAllText("self-test.txt",ex.ToString()); Shutdown(1); } return; }
  base.OnStartup(e); new MainWindow().Show();
 }
}
