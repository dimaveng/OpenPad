using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.Dsp;
namespace Soundpad;

public sealed class AudioEngine : IDisposable
{
    public static void SelfTest(string? oggPath = null)
    {
        string path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),Guid.NewGuid()+".wav");
        try
        {
            using(var w=new WaveFileWriter(path,new WaveFormat(44100,16,1))) for(int i=0;i<44100;i++) w.WriteSample((float)(0.05*Math.Sin(i*2*Math.PI*90/44100)));
            double Measure(float gain,float bass)
            {
                var p=new ClipProvider(new Clip { Path=path,Gain=gain,Bass=bass }); var b=new float[48000]; int n=p.Read(b,0,b.Length); double rms=Math.Sqrt(b.Take(n).Sum(x=>(double)x*x)/n); if(n<40000 || p.WaveFormat.Channels!=2 || p.WaveFormat.SampleRate!=48000)throw new Exception("Format conversion failed"); p.Stop(); if(p.Read(b,0,100)!=0)throw new Exception("Stop failed"); return rms;
            }
            MainWindow.SharedGain=1;
            double normal=Measure(100,0), boosted=Measure(500,0), bass=Measure(100,12);
            if(normal<=0 || Math.Abs(boosted/normal-5)>0.01 || bass<normal*1.5)throw new Exception("DSP gain test failed");
            using(var testEngine=new AudioEngine())
            {
                testEngine.mix=NewMixer(); testEngine.monitorMix=NewMixer();
                testEngine.mix.AddMixerInput(new VoiceProvider(new ConstantProvider(0.125f),testEngine));
                testEngine.Play(new Clip { Path=path }); testEngine.Stop();
                var voice=new float[256]; testEngine.mix.Read(voice,0,voice.Length);
                if(voice.Any(x=>Math.Abs(x-0.125f)>0.00001f))throw new Exception("Voice stopped with clips");
                testEngine.VoiceGain=2; testEngine.mix.Read(voice,0,voice.Length);
                if(voice.Any(x=>Math.Abs(x-0.25f)>0.00001f))throw new Exception("Voice gain failed");
                testEngine.monitorMix.Read(voice,0,voice.Length);
                if(voice.Any(x=>x!=0))throw new Exception("Microphone leaked into monitor");
            }
            if(oggPath != null)
            {
                using(var probe=AudioDecoder.Open(oggPath))
                    if(probe.WaveFormat.Channels is < 1 or > 2) throw new Exception("Ogg import failed");
                var ogg=new ClipProvider(new Clip { Path=oggPath,Gain=500,Bass=12 });
                try
                {
                    var buffer=new float[4096]; long total=0; double energy=0;
                    int count;
                    while((count=ogg.Read(buffer,0,buffer.Length))>0)
                    {
                        for(int i=0;i<count;i++) { if(!float.IsFinite(buffer[i]))throw new Exception("Invalid Ogg sample"); energy+=buffer[i]*buffer[i]; }
                        total+=count;
                        if(total>48000*2*300)throw new Exception("Ogg did not reach EOF");
                    }
                    if(total==0 || energy==0 || !ogg.Finished)throw new Exception("Ogg decoding failed");
                }
                finally { ogg.Stop(); }
            }
        }
        finally { System.IO.File.Delete(path); }
    }
    readonly object sync = new();
    sealed class ConstantProvider(float value) : ISampleProvider
    {
        public WaveFormat WaveFormat=>WaveFormat.CreateIeeeFloatWaveFormat(48000,2);
        public int Read(float[] b,int o,int c) { Array.Fill(b,value,o,c); return c; }
    }
    readonly List<IDisposable> resources = new();
    readonly List<ClipProvider> active = new();
    MixingSampleProvider? mix, monitorMix;
    WaveInEvent? input;
    bool monitoring;
    public float Gain = 1;
    public bool Distortion;
    public float Peak;
    public float MicPeak;
    public float VoiceGain=1;
    public bool VoiceActive {get;private set;}
    public void Configure(int output, int monitor, int microphone, bool voice)
    {
        Dispose();
        mix = NewMixer(); monitorMix = NewMixer();
        try
        {
            var main = new WaveOutEvent { DeviceNumber = output, DesiredLatency = 80 };
            resources.Add(main); main.Init(new MasterProvider(mix, this)); main.Play();
            monitoring = monitor >= -1 && monitor != output;
            if (monitoring)
            {
                var headphones = new WaveOutEvent { DeviceNumber = monitor, DesiredLatency = 80 };
                resources.Add(headphones); headphones.Init(new MasterProvider(monitorMix, this)); headphones.Play();
            }
            if(voice && microphone<0) throw new InvalidOperationException("Выбери физический микрофон.");
            if (voice && microphone >= 0)
            {
                input = new WaveInEvent { DeviceNumber = microphone, WaveFormat = new WaveFormat(48000, 16, 1), BufferMilliseconds = 20 };
                resources.Add(input);
                var buffer = new BufferedWaveProvider(input.WaveFormat) { DiscardOnBufferOverflow = true, BufferDuration = TimeSpan.FromSeconds(1) };
                input.DataAvailable += (_, e) => buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
                mix.AddMixerInput(new VoiceProvider(new MonoToStereoSampleProvider(buffer.ToSampleProvider()),this)); input.StartRecording(); VoiceActive=true;
            }
        }
        catch { Dispose(); throw; }
    }
    static MixingSampleProvider NewMixer() => new(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)) { ReadFully = true };
    public void Play(Clip clip)
    {
        lock(sync)
        {
            if (mix == null) throw new InvalidOperationException("Выбери доступное устройство вывода.");
            active.RemoveAll(p => p.Finished);
            var primary = new ClipProvider(clip);
            ClipProvider? preview=null;
            try { if(monitoring)preview=new ClipProvider(clip); }
            catch { primary.Stop(); throw; }
            active.Add(primary); mix.AddMixerInput(primary);
            if (preview!=null) { active.Add(preview); monitorMix!.AddMixerInput(preview); }
        }
    }
    public void Stop()
    {
        lock(sync) { foreach(var p in active) p.Stop(); active.Clear(); }
    }
    public void Dispose()
    {
        foreach(var resource in resources)
        {
            try { if(resource is WaveOutEvent output) output.Stop(); if(resource is WaveInEvent capture) capture.StopRecording(); }
            catch(Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
            try { resource.Dispose(); }
            catch(Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
        }
        resources.Clear(); input = null;
        Stop(); mix = null; monitorMix = null; Peak = 0; MicPeak=0; VoiceActive=false;
    }
    sealed class VoiceProvider(ISampleProvider source,AudioEngine owner) : ISampleProvider
    {
        public WaveFormat WaveFormat=>source.WaveFormat;
        public int Read(float[] buffer,int offset,int count)
        {
            int n=source.Read(buffer,offset,count); float peak=0;
            for(int i=offset;i<offset+n;i++) { buffer[i]*=owner.VoiceGain; peak=Math.Max(peak,Math.Abs(buffer[i])); }
            owner.MicPeak=peak; return n;
        }
    }
    sealed class MasterProvider(ISampleProvider source, AudioEngine owner) : ISampleProvider
    {
        public WaveFormat WaveFormat => source.WaveFormat;
        public int Read(float[] buffer, int offset, int count)
        {
            int n = source.Read(buffer, offset, count); float peak = 0;
            for(int i=offset;i<offset+n;i++)
            {
                // Master gain belongs to clips, so voice stays at its original level.
                float x = buffer[i];
                buffer[i] = owner.Distortion ? (float)Math.Tanh(x * 2) : Math.Clamp(x, -1f, 1f);
                peak = Math.Max(peak, Math.Abs(buffer[i]));
            }
            owner.Peak = peak; return n;
        }
    }
    sealed class ClipProvider : ISampleProvider
    {
        readonly WaveStream reader;
        readonly ISampleProvider source;
        readonly Clip clip;
        readonly BiQuadFilter[] filters = new BiQuadFilter[2];
        float lastBass = float.NaN;
        bool stopped;
        public bool Finished {get;private set;}
        public WaveFormat WaveFormat => source.WaveFormat;
        public ClipProvider(Clip c)
        {
            clip=c; reader=AudioDecoder.Open(c.Path);
            ISampleProvider s=reader.ToSampleProvider();
            if(s.WaveFormat.Channels==1) s=new MonoToStereoSampleProvider(s);
            if(s.WaveFormat.Channels!=2) { reader.Dispose(); throw new NotSupportedException("Нужен моно или стерео файл."); }
            if(s.WaveFormat.SampleRate!=48000) s=new WdlResamplingSampleProvider(s,48000);
            source=s;
        }
        public void Stop() { lock(this) { if(stopped)return; stopped=true; Finished=true; reader.Dispose(); } }
        public int Read(float[] b,int o,int c)
        {
            lock(this)
            {
                if(stopped) return 0;
                int n=source.Read(b,o,c);
                if(n==0) { Stop(); return 0; }
                float bass=clip.Effects ? clip.Bass : 0;
                if(bass!=lastBass) { for(int j=0;j<2;j++) filters[j]=BiQuadFilter.LowShelf(48000,120,1,bass); lastBass=bass; }
                for(int i=0;i<n;i++) b[o+i]=filters[i%2].Transform(b[o+i]) * clip.Gain /100f * MainWindow.SharedGain;
                return n;
            }
        }
    }
}
