using NAudio.Wave;
using NAudio.Vorbis;
using System.IO;
using System.Text;
using Concentus;
using Concentus.Oggfile;

namespace Soundpad;

internal static class AudioDecoder
{
    // The same decoder is used for import validation and playback.
    public static WaveStream Open(string path)
    {
        using var file=File.OpenRead(path);
        var header=new byte[27];
        if(file.Read(header,0,27)==27 && Encoding.ASCII.GetString(header,0,4)=="OggS")
        {
            var segments=new byte[header[26]]; file.ReadExactly(segments);
            var packet=new byte[segments.Sum(x=>(int)x)]; file.ReadExactly(packet);
            if(packet.Length>=19 && Encoding.ASCII.GetString(packet,0,8)=="OpusHead")
                return new OpusWaveStream(path,packet);
            return new VorbisWaveReader(path);
        }
        return new AudioFileReader(path);
    }

    sealed class OpusWaveStream : WaveStream
    {
        readonly FileStream file;
        readonly OpusOggReadStream opus;
        readonly IOpusDecoder decoder;
        readonly WaveFormat format;
        readonly double gain;
        byte[] pending=Array.Empty<byte>(); int pendingOffset,skip;
        long position;
        public override WaveFormat WaveFormat=>format;
        public override long Length {get;}
        public override bool CanSeek=>false;
        public override long Position {get=>position;set=>throw new NotSupportedException();}
        public OpusWaveStream(string path,byte[] head)
        {
            int channels=head[9];
            if(channels is <1 or >2 || head[18]!=0)throw new NotSupportedException("Opus: поддерживаются моно и стерео без многоканальной карты.");
            format=WaveFormat.CreateIeeeFloatWaveFormat(48000,channels);
            skip=BitConverter.ToUInt16(head,10)*channels;
            gain=Math.Pow(10,BitConverter.ToInt16(head,16)/256.0/20);
            decoder=OpusCodecFactory.CreateDecoder(48000,channels);
            file=File.OpenRead(path);
            try
            {
                opus=new OpusOggReadStream(decoder,file);
                if(!string.IsNullOrEmpty(opus.LastError))throw new InvalidDataException(opus.LastError);
                Length=Math.Max(0,opus.GranuleCount*channels-skip)*sizeof(float);
            }
            catch { file.Dispose(); decoder.Dispose(); throw; }
        }
        public override int Read(byte[] buffer,int offset,int count)
        {
            int written=0;
            while(written<count && position<Length)
            {
                if(pendingOffset==pending.Length)
                {
                    var samples=opus.DecodeNextPacket();
                    if(samples==null) { if(!string.IsNullOrEmpty(opus.LastError))throw new InvalidDataException(opus.LastError); break; }
                    int start=Math.Min(skip,samples.Length); skip-=start;
                    var floats=new float[samples.Length-start];
                    for(int i=0;i<floats.Length;i++)floats[i]=(float)(samples[start+i]/32768.0*gain);
                    pending=new byte[floats.Length*4]; Buffer.BlockCopy(floats,0,pending,0,pending.Length); pendingOffset=0;
                    if(pending.Length==0)continue;
                }
                int n=(int)Math.Min(Math.Min(count-written,pending.Length-pendingOffset),Length-position);
                Buffer.BlockCopy(pending,pendingOffset,buffer,offset+written,n);
                pendingOffset+=n; written+=n; position+=n;
            }
            return written;
        }
        protected override void Dispose(bool disposing)
        {
            if(disposing) { opus.Close(); file.Dispose(); decoder.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
