using System.Diagnostics;
using System.IO;
using System.Reflection;
using NAudio;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Soundpad;

internal static class StabilitySmoke
{
    public static void Run(string report)
    {
        var log=new List<string>();
        string folder=Path.Combine(Path.GetTempPath(),"OpenPad-Stability-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            string state=Path.Combine(folder,"library.json");
            var prefs=new Preferences { Clips=new(){new Clip {Name="Keep me",Path="original.wav",Gain=325}},Language="uk",Voice=true };
            SettingsStore.Save(state,prefs); File.WriteAllText(state,"{ broken json");
            var recovered=SettingsStore.Load(state);
            if(recovered.Clips.Count!=1 || recovered.Clips[0].Gain!=325 || recovered.Language!="uk")throw new Exception("Backup recovery failed");
            if(!Directory.GetFiles(Path.Combine(folder,"Recovery")).Any(f=>File.ReadAllText(f)=="{ broken json"))throw new Exception("Damaged original was not preserved");
            File.WriteAllText(state,"{\"Clips\":null,\"Master\":999,\"VoiceGain\":-4,\"ViewMode\":\"invalid\"}");
            recovered=SettingsStore.Load(state);
            if(recovered.Clips==null || recovered.Master!=500 || recovered.VoiceGain!=0 || recovered.ViewMode!="tiles")throw new Exception("Settings normalization failed");
            log.Add("PASS: malformed JSON recovery, exact damaged-original preservation, null collection and range normalization.");
            File.WriteAllText(state,"{\"Clips\":[null,{\"Path\":\"\"},{\"Path\":\"valid.wav\",\"Name\":null,\"Gain\":1e99,\"Bass\":-2}],\"Master\":1e309}");
            recovered=SettingsStore.Load(state);
            if(recovered.Clips.Count!=1 || recovered.Clips[0].Name!="valid" || recovered.Clips[0].Gain!=100 || recovered.Master!=100)throw new Exception("Non-finite or invalid clip migration failed");
            log.Add("PASS: null clips, missing names/paths, overflow numbers safely normalized.");
            string wav=Path.Combine(folder,"silence.wav");
            using(var writer=new WaveFileWriter(wav,new WaveFormat(44100,16,1)))writer.Write(new byte[8820],0,8820);
            var mixField=typeof(AudioEngine).GetField("mix",BindingFlags.NonPublic|BindingFlags.Instance)!;
            var monitorField=typeof(AudioEngine).GetField("monitorMix",BindingFlags.NonPublic|BindingFlags.Instance)!;
            using var engine=new AudioEngine();
            var mix=new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(48000,2)){ReadFully=true};
            mixField.SetValue(engine,mix); monitorField.SetValue(engine,new MixingSampleProvider(mix.WaveFormat){ReadFully=true});
            var clip=new Clip {Path=wav,Gain=500,Bass=24}; var samples=new float[4096];
            GC.Collect(); long memoryBefore=GC.GetTotalMemory(true); int handlesBefore=Process.GetCurrentProcess().HandleCount;
            var watch=Stopwatch.StartNew(); long read=0;
            for(int cycle=0;cycle<2000;cycle++)
            {
                engine.Play(clip); if(cycle%3==0)engine.Stop();
                for(int i=0;i<4;i++) { read+=mix.Read(samples,0,samples.Length); if(samples.Any(x=>!float.IsFinite(x)))throw new Exception("Non-finite sample"); }
            }
            engine.Stop(); mix.Read(samples,0,samples.Length);
            long memoryAfter=GC.GetTotalMemory(true); int handlesAfter=Process.GetCurrentProcess().HandleCount;
            if(memoryAfter-memoryBefore>16*1024*1024 || handlesAfter-handlesBefore>24)throw new Exception("Resource growth exceeded the test bound");
            log.Add($"PASS: 2000 play/stop cycles, {read/(48000.0*2):F1} seconds simulated audio, {watch.Elapsed.TotalSeconds:F1}s wall time, managed memory delta {memoryAfter-memoryBefore} bytes, handle delta {handlesAfter-handlesBefore}.");
            // Stress repeated bursts before the output callback drains stopped sources.
            for(int i=0;i<1100;i++) { engine.Play(clip); engine.Stop(); }
            log.Add("PASS: 1100 rapid play/stop operations before audio callback.");
            File.Delete(wav); log.Add("PASS: audio readers released the fixture file.");
            File.WriteAllLines(report,log);
        }
        catch(Exception ex) { log.Add("FAIL: "+ex); File.WriteAllLines(report,log); throw; }
        finally { Directory.Delete(folder,true); }
    }
    public static void DeviceSoak(string report)
    {
        var log=new List<string>();
        var cable=Enumerable.Range(0,WaveOut.DeviceCount).FirstOrDefault(i=>WaveOut.GetCapabilities(i).ProductName.Contains("CABLE Input",StringComparison.OrdinalIgnoreCase),-1);
        if(cable<0) { File.WriteAllText(report,"SKIP: no CABLE Input available; physical output test not run."); return; }
        string path=Path.Combine(Path.GetTempPath(),"OpenPad-silence-"+Guid.NewGuid().ToString("N")+".wav");
        try
        {
            using(var w=new WaveFileWriter(path,new WaveFormat(48000,16,1)))w.Write(new byte[96000],0,96000);
            using var engine=new AudioEngine();
            for(int i=0;i<10;i++) { engine.Configure(cable,-2,-1,false); engine.Play(new Clip {Path=path}); Thread.Sleep(100); engine.Stop(); }
            log.Add("PASS: 10 reopen/play/stop cycles on actual VB-CABLE output.");
            try { engine.Configure(int.MaxValue,-2,-1,false); throw new Exception("Invalid output accepted"); } catch(MmException) { }
            engine.Configure(cable,-2,-1,false);
            log.Add("PASS: invalid device rejected; valid output reopens after failure.");
            int handlesBefore=Process.GetCurrentProcess().HandleCount;
            var watch=Stopwatch.StartNew();
            for(int second=0;second<120;second++)
            {
                engine.Play(new Clip {Path=path,Gain=500,Bass=24});
                if(second%5==0)engine.Stop();
                Thread.Sleep(1000);
                if(engine.LastError!=null)throw new Exception(engine.LastError);
                if(second%30==29)File.WriteAllLines(report,log.Append($"RUNNING: {second+1}/120 seconds"));
            }
            engine.Stop();
            log.Add($"PASS: {watch.Elapsed.TotalSeconds:F1}s real-time silent playback to VB-CABLE, 120 launches, handle delta {Process.GetCurrentProcess().HandleCount-handlesBefore}. No microphone capture or device-default changes.");
            File.WriteAllLines(report,log);
        }
        catch(Exception ex) { File.WriteAllLines(report,log.Append("FAIL: "+ex)); throw; }
        finally { File.Delete(path); }
    }
}

