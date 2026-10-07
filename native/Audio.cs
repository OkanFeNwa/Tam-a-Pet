using System.Collections.Concurrent;
using System.Runtime.InteropServices;

// Cat sounds: real recordings embedded in the exe (native/sounds, see CREDITS.md).
//
// A small software mixer feeds one waveOut stream, so sounds can overlap like in real life: every bounce of the
// ball starts a new boing even if the previous one is still ringing. Cat sounds (meows, hiss, chewing...) are one
// at a time: a new one is ignored while another is still playing (the hiss may cut in on the others). The purr is a
// looping voice that stops when the cat wakes up.
//
// Everything runs on one background thread (decoding, mixing, device), so the cat and the ball never stutter.
// When nothing is playing the thread sleeps and the device is not fed.
static class Audio
{
    sealed record Clip(short[] Pcm, int Rate);   // mono

    sealed class Voice
    {
        public Clip Clip = null!;
        public double Pos, Step;      // position in the clip and advance per output sample (rate conversion + playback speed)
        public float Gain;
        public bool Loop, Dead;
        public string Name = "";
    }

    // ---- winmm waveOut ---------------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential)]
    struct WAVEFORMATEX { public ushort Tag, Channels; public uint SamplesPerSec, AvgBytesPerSec; public ushort BlockAlign, BitsPerSample, Size; }

    [DllImport("winmm.dll")] static extern int waveOutOpen(out IntPtr hwo, uint device, ref WAVEFORMATEX fmt, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] static extern int waveOutPrepareHeader(IntPtr hwo, IntPtr hdr, int size);
    [DllImport("winmm.dll")] static extern int waveOutWrite(IntPtr hwo, IntPtr hdr, int size);
    [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint ms);
    [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint ms);

    const uint WAVE_MAPPER = 0xFFFFFFFF, CALLBACK_EVENT = 0x50000;
    const int HeaderSize = 48, FlagsOffset = 24, WHDR_DONE = 1;
    // 48 kHz is what Windows mixes at by default (no sample-rate conversion in the driver). Buffers of 32 ms x 4 give the
    // stream ~128 ms of slack: slower PCs, busy CPUs and Bluetooth/USB outputs refill late and small buffers made the sound
    // stutter there. A new sound still starts within ~100 ms, which is fine for a pet.
    const int OutRate = 48000, BufSamples = 1536, Buffers = 4, MaxBoings = 12;

    // ---- state: touched by the audio thread only (except the volatile flag) ---------------------
    static readonly Dictionary<string, Clip> clips = new();
    static readonly List<Voice> voices = new();
    static readonly Voice?[] catVoices = new Voice?[Cfg.MaxCats], purrVoices = new Voice?[Cfg.MaxCats];   // per cat
    static readonly bool[] wantPurr = new bool[Cfg.MaxCats];
    static readonly Random rnd = new();
    static readonly string[] boings = typeof(Audio).Assembly.GetManifestResourceNames().Where(n => n.StartsWith("snd.ball_boing")).ToArray();

    static readonly ConcurrentQueue<Action> jobs = new();
    static readonly AutoResetEvent wake = new(false);   // new job, or a device buffer finished
    static Thread? worker;
    static readonly object startLock = new();
    static int underruns, buffersWritten;   // diagnostics (--audio-test)
    static bool streaming;

    // master volume x that pet's volume x the volume of the kind of sound ("voice", "purr" or "fx"), 0..1
    static double Vol(int owner, string kind)
    {
        var p = Cfg.ById(owner);
        double m = p == null ? 1 : p.Volume / 100.0 * (kind == "voice" ? p.VoiceVol : kind == "purr" ? p.PurrVol : p.FxVol) / 100.0;
        return Volume * m;
    }

    static double Volume => Math.Clamp(Cfg.V["volume"] / 100.0, 0, 1);

    static void Post(Action a)
    {
        lock (startLock)
        {
            if (worker == null)
            {
                worker = new Thread(Run) { IsBackground = true, Name = "audio", Priority = ThreadPriority.Highest };   // steady refills
                worker.Start();
            }
        }
        jobs.Enqueue(a);
        wake.Set();
    }

    // The audio thread: run the queued jobs, keep the device fed while something plays, sleep otherwise
    static void Run()
    {
        var fmt = new WAVEFORMATEX { Tag = 1, Channels = 1, SamplesPerSec = OutRate, AvgBytesPerSec = OutRate * 2, BlockAlign = 2, BitsPerSample = 16, Size = 0 };
        IntPtr hwo;
        if (waveOutOpen(out hwo, WAVE_MAPPER, ref fmt, wake.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, CALLBACK_EVENT) != 0)
        {
            while (true) { wake.WaitOne(); while (jobs.TryDequeue(out _)) { } }   // no audio device: stay silent
        }

        var hdr = new IntPtr[Buffers];
        var data = new IntPtr[Buffers];
        var queued = new bool[Buffers];
        for (int i = 0; i < Buffers; i++)
        {
            data[i] = Marshal.AllocHGlobal(BufSamples * 2);
            hdr[i] = Marshal.AllocHGlobal(HeaderSize);
            for (int b = 0; b < HeaderSize; b += 8) Marshal.WriteInt64(hdr[i], b, 0);
            Marshal.WriteIntPtr(hdr[i], 0, data[i]);          // lpData
            Marshal.WriteInt32(hdr[i], 8, BufSamples * 2);    // dwBufferLength
            waveOutPrepareHeader(hwo, hdr[i], HeaderSize);
        }
        var mix = new float[BufSamples];
        var pcm = new short[BufSamples];

        // Decode every recording now (a first-time decode in the middle of playback stalls the stream on slow PCs)
        // and run the mixer once so it is already compiled and optimised when the first sound plays
        foreach (var n in typeof(Audio).Assembly.GetManifestResourceNames().Where(n => n.StartsWith("snd.")))
            try { Load(n.Substring(4)); } catch { }
        Repeat("cat_angry", 2);
        Mix(mix, pcm);
        bool timerRaised = false;

        while (true)
        {
            while (jobs.TryDequeue(out var job))
                try { job(); } catch { }
            voices.RemoveAll(v => v.Dead);

            bool anyQueued = false;
            for (int i = 0; i < Buffers; i++)
            {
                if (queued[i] && (Marshal.ReadInt32(hdr[i], FlagsOffset) & WHDR_DONE) != 0) queued[i] = false;
                anyQueued |= queued[i];
            }
            if (voices.Count > 0 && !anyQueued && streaming) underruns++;   // the device ran dry while sounds were playing: an audible glitch
            streaming = voices.Count > 0 || anyQueued;
            if (streaming != timerRaised) { if (streaming) timeBeginPeriod(1); else timeEndPeriod(1); timerRaised = streaming; }   // 1 ms timer only while sound plays: steadier refills
            for (int i = 0; i < Buffers; i++)
            {
                if (queued[i] || voices.Count == 0) continue;
                Mix(mix, pcm);
                Marshal.Copy(pcm, 0, data[i], BufSamples);
                waveOutWrite(hwo, hdr[i], HeaderSize);
                queued[i] = true; buffersWritten++;
                voices.RemoveAll(v => v.Dead);
            }
            wake.WaitOne(voices.Count > 0 || anyQueued ? 10 : Timeout.Infinite);
        }
    }

    // Sum every voice into one buffer
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]   // no slow first-run (tier-0) code
    static void Mix(float[] mix, short[] outPcm)
    {
        Array.Clear(mix);
        foreach (var v in voices)
        {
            if (v.Dead) continue;
            var src = v.Clip.Pcm;
            int len = src.Length;
            for (int i = 0; i < mix.Length; i++)
            {
                if (v.Pos >= len)
                {
                    if (v.Loop) v.Pos -= len; else { v.Dead = true; break; }
                }
                int idx = (int)v.Pos;
                double frac = v.Pos - idx;
                int nxt = idx + 1 < len ? idx + 1 : (v.Loop ? 0 : idx);
                mix[i] += (float)((src[idx] * (1 - frac) + src[nxt] * frac) * v.Gain);
                v.Pos += v.Step;
            }
        }
        for (int i = 0; i < mix.Length; i++)
        {
            // soft limiter: many overlapping voices at high volume saturate smoothly instead of clipping
            float x = mix[i], a = Math.Abs(x);
            if (a > 20000) x = Math.Sign(x) * (20000 + 12000 * (float)Math.Tanh((a - 20000) / 12000.0));
            outPcm[i] = (short)Math.Clamp(x, short.MinValue, short.MaxValue);
        }
    }

    // ---- clips -----------------------------------------------------------------------------------
    // Embedded PCM WAV -> mono samples, normalised so every recording has the same loudness
    static Clip Load(string file)
    {
        if (clips.TryGetValue(file, out var cached)) return cached;
        using var s = typeof(Audio).Assembly.GetManifestResourceStream("snd." + file)!;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        var b = ms.ToArray();
        int channels = 1, rate = 44100, pos = 12;
        short[] pcm = Array.Empty<short>();
        while (pos + 8 <= b.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(b, pos, 4);
            int size = BitConverter.ToInt32(b, pos + 4);
            if (id == "fmt ") { channels = BitConverter.ToInt16(b, pos + 10); rate = BitConverter.ToInt32(b, pos + 12); }
            else if (id == "data")
            {
                size = Math.Min(size, b.Length - pos - 8);
                pcm = new short[size / 2];
                Buffer.BlockCopy(b, pos + 8, pcm, 0, pcm.Length * 2);
                break;
            }
            pos += 8 + size + (size & 1);
        }
        if (channels > 1)   // down-mix to mono
        {
            var mono = new short[pcm.Length / channels];
            for (int i = 0; i < mono.Length; i++)
            {
                int sum = 0;
                for (int c = 0; c < channels; c++) sum += pcm[i * channels + c];
                mono[i] = (short)(sum / channels);
            }
            pcm = mono;
        }
        int peak = 1;
        foreach (var v in pcm) peak = Math.Max(peak, Math.Abs((int)v));
        for (int i = 0; i < pcm.Length; i++) pcm[i] = (short)(pcm[i] * 24000.0 / peak);
        return clips[file] = new Clip(pcm, rate);
    }

    // The same recording back to back n times
    static Clip Repeat(string file, int n)
    {
        string key = file + "*" + n;
        if (clips.TryGetValue(key, out var cached)) return cached;
        var c = Load(file);
        var pcm = new short[c.Pcm.Length * n];
        for (int i = 0; i < n; i++) Array.Copy(c.Pcm, 0, pcm, i * c.Pcm.Length, c.Pcm.Length);
        return clips[key] = new Clip(pcm, c.Rate);
    }

    static string Pick(params string[] files) => files[rnd.Next(files.Length)];

    // ---- public API ------------------------------------------------------------------------------
    // owner = which cat (its Index); pitch = that cat's voice (a multiplier on the playback speed)
    public static void Play(string name, double loudness = 1, int owner = 0, double pitch = 1)
    {
        double v = Vol(owner, name is "boing" or "munch" ? "fx" : "voice");
        if (v < 0.01) return;
        Post(() => PlayNow(name, v, loudness, owner, pitch));
    }

    static void PlayNow(string name, double v, double loudness, int owner, double pitch)
    {
        Clip clip;
        double rate = 1;   // playback speed: >1 = shorter and higher
        switch (name)
        {
            case "meow": clip = Load(Pick("cat_meow", "cat_meow_cute")); rate = 0.95 + rnd.NextDouble() * 0.1; break;   // now and then
            case "meowfood": clip = Load("cat_meow"); break;                                                            // hungry
            case "mew": clip = Load("cat_meow_cute"); rate = 0.96 + rnd.NextDouble() * 0.1; break;                       // click
            case "chirp": clip = Load("cat_kitten"); break;                                                             // jump
            case "hiss": clip = Repeat("cat_angry", 2); break;                                                          // angry: twice, to cover the whole animation
            case "munch": clip = Load(Pick("munch_3", "munch_4", "munch_5", "munch_6")); break;                         // eating
            case "boing":                                                                                               // ball bounces
                if (boings.Length == 0) return;
                clip = Load(boings[rnd.Next(boings.Length)].Substring(4)); rate = (0.95 + rnd.NextDouble() * 0.2) * (1.12 - 0.12 * loudness); break;   // small bounce: softer and a bit higher
            default: return;
        }

        bool isCat = name != "boing";
        if (isCat)
        {
            if (catVoices[owner] is { Dead: false } playing)
            {
                if (name == "hiss" && playing.Name != "hiss") playing.Dead = true;   // the hiss cuts in on another cat sound
                else return;                                                          // still playing: don't restart it
            }
            StopPurr(owner);   // any sound of that cat ends its purr (it was woken up)
        }

        if (isCat) rate *= pitch;   // each cat has its own voice
        var voice = new Voice { Clip = clip, Step = clip.Rate / (double)OutRate * rate, Gain = (float)(v * v * loudness), Name = name };
        voices.Add(voice);
        if (isCat) catVoices[owner] = voice;
        else
        {
            // bounces overlap freely; just keep their number sane
            int n = 0;
            foreach (var x in voices) if (x.Name == "boing" && !x.Dead) n++;
            foreach (var x in voices) if (n > MaxBoings && x.Name == "boing" && !x.Dead) { x.Dead = true; n--; }
        }
    }

    static void StopPurr(int owner)
    {
        if (purrVoices[owner] != null) { purrVoices[owner]!.Dead = true; purrVoices[owner] = null; }
    }

    static void StartPurr(int owner, double v, double pitch)
    {
        if (purrVoices[owner] is { Dead: false } || v < 0.01) return;
        var c = Load("cat_purrsleepy_loop");
        var voice = new Voice { Clip = c, Step = c.Rate / (double)OutRate * pitch, Gain = (float)(v * v * 0.8), Loop = true, Name = "purr" };
        purrVoices[owner] = voice;
        voices.Add(voice);
    }

    // Purring loop while a cat sleeps or is stroked; call every frame with the desired state (it is cheap)
    public static void Purr(int owner, bool on, double pitch = 1)
    {
        if (on == wantPurr[owner]) return;
        wantPurr[owner] = on;
        double v = Vol(owner, "purr");
        Post(() => { if (on) StartPurr(owner, v, pitch); else StopPurr(owner); });
    }

    // The volume changed: running purrs follow it (or start, if they were muted)
    public static void Refresh()
    {
        var vs = Enumerable.Range(0, Cfg.MaxCats).Select(o => Vol(o, "purr")).ToArray();
        Post(() =>
        {
            for (int o = 0; o < purrVoices.Length; o++)
            {
                double v = vs[o];
                if (purrVoices[o] is { Dead: false } p) p.Gain = (float)(v * v * 0.8);
                else if (wantPurr[o]) StartPurr(o, v, 1);
            }
        });
    }

    // For development: write a sound to a WAV file (TamAPet.exe --dump-sound name file.wav)
    public static void Dump(string name, string path) => File.WriteAllBytes(path, Wav(Load(name).Pcm, Load(name).Rate));

    // For development: play a busy mix of sounds through the real device and report glitches (TamAPet.exe --audio-test file.txt)
    public static void SelfTest(string path)
    {
        Cfg.V["volume"] = 15;
        Purr(0, true);
        for (int i = 0; i < 25; i++) { Play("boing", 0.7 + (i % 3) * 0.15); Thread.Sleep(140); }
        Play("meow"); Thread.Sleep(300); Play("hiss"); Thread.Sleep(2500);
        for (int i = 0; i < 6; i++) { Play("munch"); Thread.Sleep(700); }
        Thread.Sleep(500);
        File.WriteAllText(path, $"buffers written: {buffersWritten}, underruns (device ran dry): {underruns}, buffer = {BufSamples * 1000 / OutRate} ms x {Buffers}");
    }

    // For development: mix three boings started 60 ms and 130 ms apart, to check that they overlap (TamAPet.exe --dump-mix file.wav)
    public static void DumpMix(string path)
    {
        var clip = Load("ball_boing_1");
        var all = new List<short>();
        var mix = new float[BufSamples];
        var pcm = new short[BufSamples];
        double[] startsMs = { 0, 60, 130 };
        int next = 0;
        for (int chunk = 0; chunk < 170; chunk++)
        {
            double t = chunk * BufSamples * 1000.0 / OutRate;
            while (next < startsMs.Length && t >= startsMs[next]) { voices.Add(new Voice { Clip = clip, Step = clip.Rate / (double)OutRate, Gain = 1f, Name = "boing" }); next++; }
            Mix(mix, pcm);
            all.AddRange(pcm);
        }
        File.WriteAllBytes(path, Wav(all.ToArray(), OutRate));
    }

    static byte[] Wav(short[] pcm, int rate)
    {
        int dataLen = pcm.Length * 2;
        var o = new byte[44 + dataLen];
        void Put(int at, byte[] b) => Buffer.BlockCopy(b, 0, o, at, b.Length);
        Put(0, "RIFF"u8.ToArray()); Put(4, BitConverter.GetBytes(36 + dataLen)); Put(8, "WAVEfmt "u8.ToArray());
        Put(16, BitConverter.GetBytes(16)); Put(20, BitConverter.GetBytes((short)1)); Put(22, BitConverter.GetBytes((short)1));
        Put(24, BitConverter.GetBytes(rate)); Put(28, BitConverter.GetBytes(rate * 2)); Put(32, BitConverter.GetBytes((short)2));
        Put(34, BitConverter.GetBytes((short)16)); Put(36, "data"u8.ToArray()); Put(40, BitConverter.GetBytes(dataLen));
        Buffer.BlockCopy(pcm, 0, o, 44, dataLen);
        return o;
    }
}
