using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Security.Cryptography;
using NAudio.Wave;

namespace Soundpad;

internal static class OplSmoke
{
    public static void Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"OpenPad-OPL-Test-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string wav=Path.Combine(root,"source.wav"), package=Path.Combine(root,"collection.opl"), data=Path.Combine(root,"Data");
            using(var w=new WaveFileWriter(wav,new WaveFormat(44100,16,1)))for(int i=0;i<4410;i++)w.WriteSample((float)(0.1*Math.Sin(i*0.1)));
            var clips=new List<Clip> { new() {Path=wav,Name="Перший / first",Gain=420,Bass=12,Favorite=true},new() {Path=wav,Name="Второй",Gain=75,Bass=6,Effects=false} };
            OplPackage.Export(package,clips);
            string originalHash=Hash(package);
            try { OplPackage.Export(package,new[]{new Clip {Path=wav+"missing",Name="missing"}}); throw new Exception("Missing export accepted"); } catch(FileNotFoundException) { }
            if(Hash(package)!=originalHash)throw new Exception("Failed export replaced original package");
            using(var zip=ZipFile.OpenRead(package))
            {
                using var reader=new StreamReader(zip.GetEntry("manifest.json")!.Open());
                if(reader.ReadToEnd().Contains(root,StringComparison.OrdinalIgnoreCase))throw new Exception("Local paths leaked");
            }
            File.Delete(wav); // Package must work without the original files.
            List<Clip> imported;
            using(var batch=OplPackage.Import(package,data,Array.Empty<Clip>()))
            {
                imported=batch.Clips;
                if(imported.Count!=2 || imported[0].Name!=clips[0].Name || imported[0].Gain!=420 || imported[0].Bass!=12 || !imported[0].Favorite || imported[1].Effects)throw new Exception("Metadata round trip failed");
                if(imported.Any(c=>!File.Exists(c.Path)))throw new Exception("Audio not extracted");
                batch.Commit();
            }
            using(var duplicate=OplPackage.Import(package,data,imported))
                if(duplicate.Clips.Count!=0 || duplicate.Skipped!=2)throw new Exception("Duplicate import failed");
            string rollback;
            using(var batch=OplPackage.Import(package,data,Array.Empty<Clip>()))rollback=batch.Clips[0].Path;
            if(File.Exists(rollback))throw new Exception("Uncommitted import not cleaned up");
            void Reject(string name,Action<ZipArchive> mutate)
            {
                string bad=Path.Combine(root,name+".opl"); File.Copy(package,bad);
                using(var zip=ZipFile.Open(bad,ZipArchiveMode.Update))mutate(zip);
                int folders=Directory.GetDirectories(Path.Combine(data,"Imports")).Length;
                try { using var batch=OplPackage.Import(bad,data,Array.Empty<Clip>()); throw new Exception("Invalid package accepted: "+name); }
                catch(InvalidDataException) { }
                if(Directory.GetDirectories(Path.Combine(data,"Imports")).Length!=folders)throw new Exception("Failed import left partial files");
            }
            void EditManifest(ZipArchive zip,Action<OplPackage.Manifest> edit)
            {
                var entry=zip.GetEntry("manifest.json")!; OplPackage.Manifest m;
                using(var s=entry.Open())m=JsonSerializer.Deserialize<OplPackage.Manifest>(s)!;
                entry.Delete(); edit(m); using var stream=zip.CreateEntry("manifest.json").Open(); JsonSerializer.Serialize(stream,m);
            }
            Reject("future",zip=>EditManifest(zip,m=>m.Version=999));
            Reject("traversal",zip=>EditManifest(zip,m=>m.Clips[0].Audio="../outside.wav"));
            Reject("checksum",zip=>EditManifest(zip,m=>m.Clips[1].Sha256=new string('0',64)));
            Reject("missing",zip=>zip.GetEntry("audio/000001.wav")!.Delete());
            Reject("duplicate-entry",zip=>zip.CreateEntry("manifest.json"));
            Reject("extra-file",zip=>zip.CreateEntry("unexpected.txt"));
            Reject("bad-gain",zip=>EditManifest(zip,m=>m.Clips[1].Gain=501));
            if(!File.Exists(imported[0].Path))throw new Exception("Existing audio removed by failed import");
        }
        finally { Directory.Delete(root,true); }
    }
    static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
