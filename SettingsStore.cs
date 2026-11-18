using System.IO;
using System.Text.Json;

namespace Soundpad;

internal static class SettingsStore
{
    static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    public static Preferences Load(string path,bool portable=false)
    {
        if(!File.Exists(path))return new();
        try
        {
            var prefs=JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path)) ?? throw new InvalidDataException("Empty settings");
            if(Normalize(prefs))Preserve(path);
            if(portable)ResolvePaths(prefs,path);
            return prefs;
        }
        catch(Exception ex) when(ex is JsonException or InvalidDataException)
        {
            Preserve(path);
            try
            {
                var backup=JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path+".bak")) ?? new();
                Normalize(backup); if(portable)ResolvePaths(backup,path); return backup;
            }
            catch(Exception backupError) when(backupError is IOException or JsonException or InvalidDataException) { return new(); }
        }
    }
    static void Preserve(string path)
    {
        string folder=Path.Combine(Path.GetDirectoryName(path)!,"Recovery"); Directory.CreateDirectory(folder);
        File.Copy(path,Path.Combine(folder,$"library-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json"));
    }
    static bool Normalize(Preferences prefs)
    {
        string before=JsonSerializer.Serialize(prefs,new JsonSerializerOptions {NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals});
        prefs.Clips ??=new();
        prefs.Clips=prefs.Clips.Where(c=>c!=null && !string.IsNullOrWhiteSpace(c.Path)).ToList();
        foreach(var clip in prefs.Clips)
        {
            if(string.IsNullOrWhiteSpace(clip.Name))clip.Name=Path.GetFileNameWithoutExtension(clip.Path);
            clip.Gain=float.IsFinite(clip.Gain)?Math.Clamp(clip.Gain,0,500):100;
            clip.Bass=float.IsFinite(clip.Bass)?Math.Clamp(clip.Bass,0,24):0;
        }
        prefs.Master=double.IsFinite(prefs.Master)?Math.Clamp(prefs.Master,0,500):100;
        prefs.VoiceGain=double.IsFinite(prefs.VoiceGain)?Math.Clamp(prefs.VoiceGain,0,200):100;
        if(prefs.ViewMode is not "tiles" and not "list")prefs.ViewMode="tiles";
        return before!=JsonSerializer.Serialize(prefs);
    }
    static void ResolvePaths(Preferences prefs,string path)
    {
        string folder=Path.GetDirectoryName(Path.GetFullPath(path))!;
        foreach(var clip in prefs.Clips)if(!Path.IsPathFullyQualified(clip.Path))clip.Path=Path.GetFullPath(Path.Combine(folder,clip.Path));
    }
    public static void Save(string path,Preferences prefs,bool portable=false)
    {
        var node=JsonSerializer.SerializeToNode(prefs)!;
        if(portable)
        {
            string folder=Path.GetDirectoryName(Path.GetFullPath(path))!;
            foreach(var clip in node["Clips"]!.AsArray())
            {
                string full=Path.GetFullPath(clip!["Path"]!.GetValue<string>());
                if(full.StartsWith(folder+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))clip["Path"]=Path.GetRelativePath(folder,full);
            }
        }
        string json=node.ToJsonString(Json),temp=path+".tmp";
        File.WriteAllText(temp,json); File.Move(temp,path,true);
        // Backup failure must not report the committed library save as failed.
        try { File.WriteAllText(path+".bak.tmp",json); File.Move(path+".bak.tmp",path+".bak",true); }
        catch(IOException) { } catch(UnauthorizedAccessException) { }
    }
}
