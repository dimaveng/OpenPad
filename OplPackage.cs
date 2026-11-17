using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Soundpad;

internal static class OplPackage
{
    internal const long MaxFileBytes=256L*1024*1024, MaxTotalBytes=2L*1024*1024*1024;
    internal const int MaxClips=5000;
    static readonly JsonSerializerOptions Json=new() { WriteIndented=true };
    internal sealed class Manifest
    {
        public string Format {get;set;}="OpenPad Library";
        public int Version {get;set;}=1;
        public List<Item> Clips {get;set;}=new();
    }
    internal sealed class Item
    {
        public string Audio {get;set;}="";
        public string Sha256 {get;set;}="";
        public string Name {get;set;}="";
        public float Gain {get;set;}=100;
        public float Bass {get;set;}
        public bool Effects {get;set;}=true;
        public bool Favorite {get;set;}
    }
    internal sealed class Imported : IDisposable
    {
        public List<Clip> Clips {get;}=new();
        public int Skipped {get;internal set;}
        internal string Folder="";
        bool committed;
        public void Commit()=>committed=true;
        public void Dispose() { if(!committed && Directory.Exists(Folder))Directory.Delete(Folder,true); }
    }
    static string Hash(string path) { using var f=File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(f)); }
    static string Signature(string hash,Clip c)=>JsonSerializer.Serialize(new {Hash=hash.ToUpperInvariant(),c.Name,c.Gain,c.Bass,c.Effects,c.Favorite});
    static void Validate(Item item,int index)
    {
        if(item==null || string.IsNullOrWhiteSpace(item.Name) || item.Name.Length>512 || !float.IsFinite(item.Gain) || item.Gain<0 || item.Gain>500 || !float.IsFinite(item.Bass) || item.Bass<0 || item.Bass>24)
            throw new InvalidDataException(L10n.F("Некорректные настройки клипа {0}.",index+1));
        if(string.IsNullOrEmpty(item.Audio) || item.Sha256==null)throw new InvalidDataException(L10n.T("Отсутствует путь или контрольная сумма."));
        string ext=Path.GetExtension(item.Audio);
        if(ext.Length<2 || ext.Length>10 || ext.Skip(1).Any(c=>!char.IsAsciiLetterOrDigit(c)) || item.Audio!=$"audio/{index:000000}{ext}" || item.Sha256.Length!=64 || item.Sha256.Any(c=>!Uri.IsHexDigit(c)))
            throw new InvalidDataException(L10n.T("Некорректный путь или контрольная сумма в .opl."));
    }
    public static void Export(string destination,IReadOnlyList<Clip> clips)
    {
        if(clips.Count==0 || clips.Count>MaxClips)throw new InvalidDataException(L10n.F("Выбери от 1 до {0} звуков.",MaxClips));
        string full=Path.GetFullPath(destination), temp=full+"."+Guid.NewGuid().ToString("N")+".tmp";
        long total=0;
        try
        {
            using(var zip=ZipFile.Open(temp,ZipArchiveMode.Create))
            {
                var manifest=new Manifest();
                for(int i=0;i<clips.Count;i++)
                {
                    var c=clips[i];
                    if(string.Equals(Path.GetFullPath(c.Path),full,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException(L10n.T("Нельзя заменить исходный звук пакетом .opl."));
                    using var file=File.OpenRead(c.Path);
                    if(file.Length>MaxFileBytes || (total+=file.Length)>MaxTotalBytes)throw new InvalidDataException(L10n.T("Превышен размер пакета (256 МБ на звук, 2 ГБ всего)."));
                    var item=new Item { Audio=$"audio/{i:000000}{Path.GetExtension(c.Path).ToLowerInvariant()}",Name=c.Name,Gain=c.Gain,Bass=c.Bass,Effects=c.Effects,Favorite=c.Favorite,Sha256=Convert.ToHexString(SHA256.HashData(file)) };
                    Validate(item,i); file.Position=0;
                    using(var output=zip.CreateEntry(item.Audio,CompressionLevel.Fastest).Open())file.CopyTo(output);
                    manifest.Clips.Add(item);
                }
                using var metadata=zip.CreateEntry("manifest.json").Open(); JsonSerializer.Serialize(metadata,manifest,Json);
            }
            File.Move(temp,full,true);
        }
        finally { if(File.Exists(temp))File.Delete(temp); }
    }
    public static Imported Import(string path,string dataDirectory,IReadOnlyList<Clip> existing)
    {
        var result=new Imported { Folder=Path.Combine(dataDirectory,"Imports",Guid.NewGuid().ToString("N")) };
        try
        {
            using var zip=ZipFile.OpenRead(path);
            if(zip.Entries.Count>MaxClips+1 || zip.Entries.GroupBy(e=>e.FullName,StringComparer.OrdinalIgnoreCase).Any(g=>g.Count()!=1))throw new InvalidDataException(L10n.T("Слишком много файлов или повторяющиеся пути в .opl."));
            var metadata=zip.GetEntry("manifest.json") ?? throw new InvalidDataException(L10n.T("В .opl отсутствует manifest.json."));
            if(metadata.Length>8*1024*1024)throw new InvalidDataException(L10n.T("Слишком большой список клипов."));
            Manifest manifest;
            using(var source=metadata.Open())manifest=JsonSerializer.Deserialize<Manifest>(source) ?? throw new InvalidDataException(L10n.T("Пустой список клипов."));
            if(manifest.Format!="OpenPad Library" || manifest.Version!=1 || manifest.Clips==null || manifest.Clips.Count==0 || manifest.Clips.Count>MaxClips)throw new InvalidDataException(L10n.T("Неподдерживаемый формат или версия .opl."));
            if(zip.Entries.Count!=manifest.Clips.Count+1)throw new InvalidDataException(L10n.T("Список клипов не соответствует содержимому .opl."));
            long total=0;
            for(int i=0;i<manifest.Clips.Count;i++)
            {
                var item=manifest.Clips[i]; Validate(item,i);
                var entry=zip.GetEntry(item.Audio) ?? throw new InvalidDataException(L10n.T("В пакете отсутствует аудиофайл."));
                if(entry.Length>MaxFileBytes || (total+=entry.Length)>MaxTotalBytes)throw new InvalidDataException(L10n.T("Превышен допустимый размер .opl."));
            }
            var signatures=new HashSet<string>(StringComparer.Ordinal);
            foreach(var c in existing) { try { signatures.Add(Signature(Hash(c.Path),c)); } catch(IOException) { } catch(UnauthorizedAccessException) { } }
            Directory.CreateDirectory(result.Folder);
            for(int i=0;i<manifest.Clips.Count;i++)
            {
                var item=manifest.Clips[i]; var entry=zip.GetEntry(item.Audio)!;
                // Only generated filenames are used; archive paths are never extracted directly.
                string target=Path.Combine(result.Folder,$"{i:000000}{Path.GetExtension(item.Audio)}");
                using(var source=entry.Open()) using(var output=File.Create(target))
                {
                    byte[] buffer=new byte[81920]; long copied=0; int n;
                    while((n=source.Read(buffer))>0) { copied+=n; if(copied>entry.Length || copied>MaxFileBytes)throw new InvalidDataException(L10n.T("Некорректный размер аудиофайла.")); output.Write(buffer,0,n); }
                    if(copied!=entry.Length)throw new InvalidDataException(L10n.T("Аудиофайл обрезан."));
                }
                string hash=Hash(target);
                if(!hash.Equals(item.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException(L10n.T("Повреждён аудиофайл: контрольная сумма не совпадает."));
                using(var reader=AudioDecoder.Open(target))
                {
                    if(reader.WaveFormat.Channels is <1 or >2 || reader.Read(new byte[8192],0,8192)==0)throw new InvalidDataException(L10n.T("Неподдерживаемый или пустой аудиофайл."));
                }
                var clip=new Clip { Path=target,Name=item.Name,Gain=item.Gain,Bass=item.Bass,Effects=item.Effects,Favorite=item.Favorite };
                if(signatures.Add(Signature(hash,clip)))result.Clips.Add(clip);
                else { File.Delete(target); result.Skipped++; }
            }
            if(result.Clips.Count==0) { Directory.Delete(result.Folder,true); result.Folder=""; }
            return result;
        }
        catch { result.Dispose(); throw; }
    }
}
