using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace Soundpad;

public partial class MainWindow
{
    bool applyingLanguage;
    void ApplyLanguage()
    {
        applyingLanguage=true;
        L10n.Set(prefs.Language); prefs.Language=L10n.Current;
        Language=XmlLanguage.GetLanguage(L10n.Current);
        LanguagePicker.SelectedIndex=L10n.Current switch { "uk"=>0,"en"=>1,_=>2 };
        bool wasUpdating=updating; updating=true;
        if(Monitor.ItemsSource is List<Device> devices)
        {
            var chosen=Monitor.SelectedItem as Device;
            var updated=devices.Select(d=>d.Id==-2?new Device(-2,L10n.T("Не прослушивать")):d).ToList();
            Monitor.ItemsSource=updated; Monitor.SelectedItem=updated.FirstOrDefault(d=>d.Id==chosen?.Id) ?? updated[0];
        }
        updating=wasUpdating;
        MasterLabel.Text=L10n.F("Общая громкость · {0:0}%",Master.Value);
        ClipLabel.Text=L10n.F("Громкость клипа · {0:0}%",ClipGain.Value);
        SelectedLabel.Text=selected?.Name ?? L10n.T("Выбери звук в библиотеке");
        VoiceStatus.Text=L10n.T(engine.VoiceActive?"Голос + звуки → выбранный выход":"Микрофон выключен");
        applyingLanguage=false;
        if(ready) { Render(); Save(); Status.Text=L10n.T("Готов к твоим звукам"); }
    }
    void LanguageChanged(object sender,SelectionChangedEventArgs e)
    {
        if(!ready || applyingLanguage)return;
        prefs.Language=LanguagePicker.SelectedIndex switch {0=>"uk",1=>"en",_=>"ru"}; ApplyLanguage();
    }
}
