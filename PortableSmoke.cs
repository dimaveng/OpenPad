using System.IO;
using System.Text.Json;
using NAudio.Wave;

namespace Soundpad;

internal static class PortableSmoke
{
    internal static void Run()
    {
        if(UpdateService.IsPortable && !string.Equals(Path.GetFullPath(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory()).TrimEnd(Path.DirectorySeparatorChar),AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase))throw new Exception("Portable process used an external runtime");
        string root=Path.Combine(Path.GetTempPath(),"OpenPad-Portable-Test-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string original=Path.Combine(root,"external.wav"),first=Path.Combine(root,"first","Data"),moved=Path.Combine(root,"moved папка","Data");
            using(var writer=new WaveFileWriter(original,new WaveFormat(44100,16,1)))writer.Write(new byte[8820],0,8820);
            string stored=PortableStorage.ImportAudio(original,first);
            if(stored!=PortableStorage.ImportAudio(original,first) || Directory.GetFiles(Path.GetDirectoryName(stored)!).Length!=1)throw new Exception("Portable duplicate copy");
            string state=Path.Combine(first,"library.json");
            var prefs=new Preferences {Language="uk",Master=325,CheckUpdates=false,Clips=new(){new Clip {Name="Portable sound",Path=stored,Gain=210,Bass=12,Favorite=true}}};
            SettingsStore.Save(state,prefs,true);
            using(var document=JsonDocument.Parse(File.ReadAllText(state)))if(Path.IsPathRooted(document.RootElement.GetProperty("Clips")[0].GetProperty("Path").GetString()!))throw new Exception("Portable persisted absolute internal path");
            if(prefs.Clips[0].Path!=stored)throw new Exception("Save mutated live paths");
            File.Delete(original); Directory.Move(Path.GetDirectoryName(first)!,Path.GetDirectoryName(moved)!);
            var restored=SettingsStore.Load(Path.Combine(moved,"library.json"),true);
            if(restored.Clips.Count!=1 || !File.Exists(restored.Clips[0].Path) || restored.Master!=325 || restored.CheckUpdates || restored.Language!="uk" || restored.Clips[0].Gain!=210 || !restored.Clips[0].Favorite)throw new Exception("Portable move lost library/settings/audio");
            using(var reader=AudioDecoder.Open(restored.Clips[0].Path)) {var samples=new byte[1024];if(reader.Read(samples,0,samples.Length)==0)throw new Exception("Moved sound cannot play");}
            // OPL-managed files follow the same relative-path persistence rule.
            string opl=Path.Combine(root,"library.opl"); OplPackage.Export(opl,restored.Clips);
            var imported=OplPackage.Import(opl,moved,Array.Empty<Clip>());
            var importedPrefs=new Preferences {Clips=imported.Clips};
            SettingsStore.Save(Path.Combine(moved,"imported.json"),importedPrefs,true);
            var again=SettingsStore.Load(Path.Combine(moved,"imported.json"),true);
            if(!File.Exists(again.Clips[0].Path))throw new Exception("Portable OPL path resolution");
        }
        finally {Directory.Delete(root,true);}
    }
}
