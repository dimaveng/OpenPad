using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
[assembly: AssemblyVersion("0.6.0.0")]
[assembly: AssemblyFileVersion("0.6.0.0")]
[assembly: AssemblyInformationalVersion("0.6.0")]
[assembly: AssemblyProduct("OpenPad Installer")]

class Setup : Form
{
    static readonly string UiLanguage=System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
    static string T(string ru,string uk,string en) { return UiLanguage=="uk"?uk:UiLanguage=="ru"?ru:en; }
    TextBox log=new TextBox(); Button install=new Button(); CheckBox cable=new CheckBox(); bool busy;
    [STAThread] static void Main() { Application.EnableVisualStyles(); Application.Run(new Setup()); }
    Setup()
    {
        Text="OpenPad 0.6.0 — "+T("установка","встановлення","setup"); ClientSize=new Size(640,440); StartPosition=FormStartPosition.CenterScreen;
        BackColor=Color.FromArgb(17,20,18); ForeColor=Color.White; Font=new Font("Segoe UI",11);
        Controls.Add(new Label { Text="OpenPad /", Font=new Font("Segoe UI",28,FontStyle.Bold), Location=new Point(24,20),AutoSize=true });
        Controls.Add(new Label { Text=T("Приложение, .NET и звук для Discord.","Застосунок, .NET та звук для Discord.","The app, .NET and audio for Discord."), Location=new Point(26,82),AutoSize=true });
        cable.Text=T("Установить VB-CABLE, если его ещё нет","Встановити VB-CABLE, якщо його ще немає","Install VB-CABLE if it is missing"); cable.Checked=true; cable.SetBounds(26,122,570,32); Controls.Add(cable);
        log.SetBounds(26,170,588,188); log.Multiline=true; log.ReadOnly=true; log.ScrollBars=ScrollBars.Vertical;
        log.BackColor=Color.FromArgb(30,36,30); log.ForeColor=Color.White; log.BorderStyle=BorderStyle.None; Controls.Add(log);
        install.Text=T("Установить OpenPad","Встановити OpenPad","Install OpenPad"); install.SetBounds(26,380,250,40); install.BackColor=Color.FromArgb(212,248,121); install.ForeColor=Color.Black; install.FlatStyle=FlatStyle.Flat; Controls.Add(install);
        install.Click+=async delegate { await Install(); };
        FormClosing+=delegate(object sender,FormClosingEventArgs e) { if(busy)e.Cancel=true; };
    }
    void Append(string s) { if(s==null)return; BeginInvoke(new Action(delegate { log.AppendText(s+Environment.NewLine); })); }
    async Task Install()
    {
        busy=true; install.Enabled=false; cable.Enabled=false;
        string folder=Path.Combine(Path.GetTempPath(),"OpenPad-Setup-"+Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            foreach(string name in new[]{"Install.ps1","OpenPad.zip"})
              using(Stream source=Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
              using(Stream dest=File.Create(Path.Combine(folder,name))) source.CopyTo(dest);
            var info=new ProcessStartInfo("powershell.exe","-NoProfile -ExecutionPolicy Bypass -File \""+Path.Combine(folder,"Install.ps1")+"\""+(cable.Checked?"":" -SkipCable"));
            info.UseShellExecute=false; info.CreateNoWindow=true; info.RedirectStandardOutput=true; info.RedirectStandardError=true;
            var p=new Process(); p.StartInfo=info; p.OutputDataReceived+=delegate(object s,DataReceivedEventArgs e){Append(e.Data);}; p.ErrorDataReceived+=delegate(object s,DataReceivedEventArgs e){Append(e.Data);};
            p.Start(); p.BeginOutputReadLine(); p.BeginErrorReadLine(); await Task.Run(delegate { p.WaitForExit(); });
            install.Text=p.ExitCode==0?T("Готово — можно закрыть","Готово — можна закрити","Done — you may close this window"):T("Повторить установку","Повторити встановлення","Try again"); install.Enabled=p.ExitCode!=0; p.Dispose();
        }
        catch(Exception e) { log.AppendText(e.Message+Environment.NewLine); install.Enabled=true; }
        finally { busy=false; cable.Enabled=true; try { Directory.Delete(folder,true); } catch { } }
    }
}
