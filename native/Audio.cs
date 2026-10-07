using System.Collections.Concurrent;
using System.Runtime.InteropServices;

// Cat sounds: real recordings embedded in the exe (native/sounds, see CREDITS.md), played with SoundPlayer.
// Everything heavy (decoding, volume scaling, starting playback) runs on a background thread, so the cat and
// the ball never stutter when a sound starts. One sound at a time: a new sound is ignored while another one is
// still playing (the angry hiss is the only one allowed to cut in). The volume comes from the settings.
static class Audio
{
    sealed record Clip(short[] Pcm, int Channels, int Rate);

    // ---- everything below is touched by the audio thread only (except the volatile flags) ----------
    static readonly Dictionary<string, Clip> clips = new();
    // Straight to the Windows PlaySound API (SoundPlayer.Stop/Play blocked ~200 ms per call, so rapid sounds fell behind).
    // A new PlaySound call replaces whatever is playing; the buffer must stay pinned until the next call.
    [DllImport("winmm.dll", EntryPoint = "PlaySoundW")] static extern bool PlaySound(IntPtr sound, IntPtr module, uint flags);
    const uint SND_NODEFAULT = 0x2, SND_ASYNC = 0x1, SND_MEMORY = 0x4, SND_LOOP = 0x8, SND_PURGE = 0x40;
    static GCHandle pinned;
    static long busyUntil;                 // TickCount64 at which the current sound ends
    static string curName = "";
    static int curPrio;
    static readonly Random rnd = new();
    static readonly string[] boings = typeof(Audio).Assembly.GetManifestResourceNames().Where(n => n.StartsWith("snd.ball_boing")).ToArray();

    static readonly BlockingCollection<Action> work = new();
    static Thread? worker;
    static readonly object startLock = new();
    static volatile bool purring, wantPurr;

    static double Volume => Math.Clamp(Cfg.V["volume"] / 100.0, 0, 1);

    static void Post(Action a)
    {
        lock (startLock)
        {
            if (worker == null)
            {
                worker = new Thread(() =>
                {
                    foreach (var job in work.GetConsumingEnumerable())
                        try { job(); } catch { }
                }) { IsBackground = true, Name = "audio" };
                worker.Start();
            }
        }
        work.Add(a);
    }

    // Embedded PCM WAV -> samples, normalised so every recording has the same loudness
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
        int peak = 1;
        foreach (var v in pcm) peak = Math.Max(peak, Math.Abs((int)v));
        for (int i = 0; i < pcm.Length; i++) pcm[i] = (short)(pcm[i] * 24000.0 / peak);
        return clips[file] = new Clip(pcm, channels, rate);
    }

    // The same recording back to back n times
    static Clip Repeat(string file, int n)
    {
        string key = file + "*" + n;
        if (clips.TryGetValue(key, out var cached)) return cached;
        var c = Load(file);
        var pcm = new short[c.Pcm.Length * n];
        for (int i = 0; i < n; i++) Array.Copy(c.Pcm, 0, pcm, i * c.Pcm.Length, c.Pcm.Length);
        return clips[key] = new Clip(pcm, c.Channels, c.Rate);
    }

    // hiss > boing > everything else: a sound can cut a lower one, never the same kind (boing excepted: every bounce is heard)
    static int Prio(string name) => name == "hiss" ? 3 : name == "boing" ? 2 : 1;

    static string Pick(params string[] files) => files[rnd.Next(files.Length)];

    public static void Play(string name, double loudness = 1)
    {
        double v = Volume;
        if (v < 0.01) return;
        Post(() => PlayNow(name, v, loudness));
    }

    static void PlayNow(string name, double v, double loudness)
    {
        int prio = Prio(name);
        if (Environment.TickCount64 < busyUntil && !purring && !(prio > curPrio || (name == "boing" && curName == "boing"))) return;   // still playing
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
        purring = false;   // a sound cuts the purr (the cat was woken up)
        Start(clip, v * v * loudness, rate, loop: false);
        curName = name; curPrio = prio;    }

    // Purring loop while the cat sleeps; call every frame with the desired state (it is cheap)
    public static void Purr(bool on)
    {
        if (!on)
        {
            if (!wantPurr && !purring) return;
            wantPurr = false;
            Post(() => { if (purring) { StopSound(); purring = false; busyUntil = 0; } });
            return;
        }
        wantPurr = true;
        if (purring) return;
        double v = Volume;
        if (v < 0.01) return;
        Post(() => PurrNow(v));
    }

    static void PurrNow(double v)
    {
        if (!wantPurr || purring || Environment.TickCount64 < busyUntil) return;   // retried by the next frame
        Start(Load("cat_purrsleepy_loop"), v * v * 0.8, 1, loop: true);
        purring = true;
    }

    // Volume changed while purring: restart at the new level
    public static void Refresh()
    {
        double v = Volume;
        Post(() => { if (purring) { purring = false; busyUntil = 0; PurrNow(v); } });
    }

    // For development: write a sound to a WAV file (TamAPet.exe --dump-sound name file.wav)
    public static void Dump(string name, string path) => File.WriteAllBytes(path, Wav(Load(name), 1, 1));

    static void StopSound()
    {
        PlaySound(IntPtr.Zero, IntPtr.Zero, SND_PURGE);
        if (pinned.IsAllocated) pinned.Free();
    }

    static void Start(Clip clip, double gain, double rate, bool loop)
    {
        var wav = Wav(clip, gain, rate);
        var handle = GCHandle.Alloc(wav, GCHandleType.Pinned);
        PlaySound(handle.AddrOfPinnedObject(), IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT | (loop ? SND_LOOP : 0));
        if (pinned.IsAllocated) pinned.Free();   // the previous sound has been replaced
        pinned = handle;
        double ms = clip.Pcm.Length / (double)clip.Channels / (clip.Rate * rate) * 1000;
        busyUntil = loop ? long.MaxValue : Environment.TickCount64 + (long)ms + 40;
    }

    static byte[] Wav(Clip c, double gain, double rate)
    {
        int dataLen = c.Pcm.Length * 2, sr = (int)(c.Rate * rate);
        var o = new byte[44 + dataLen];
        void Put(int at, byte[] b) => Buffer.BlockCopy(b, 0, o, at, b.Length);
        Put(0, "RIFF"u8.ToArray()); Put(4, BitConverter.GetBytes(36 + dataLen)); Put(8, "WAVEfmt "u8.ToArray());
        Put(16, BitConverter.GetBytes(16)); Put(20, BitConverter.GetBytes((short)1)); Put(22, BitConverter.GetBytes((short)c.Channels));
        Put(24, BitConverter.GetBytes(sr)); Put(28, BitConverter.GetBytes(sr * 2 * c.Channels)); Put(32, BitConverter.GetBytes((short)(2 * c.Channels)));
        Put(34, BitConverter.GetBytes((short)16)); Put(36, "data"u8.ToArray()); Put(40, BitConverter.GetBytes(dataLen));
        var scaled = new short[c.Pcm.Length];
        for (int i = 0; i < scaled.Length; i++) scaled[i] = (short)Math.Clamp((int)(c.Pcm[i] * gain), short.MinValue, short.MaxValue);
        Buffer.BlockCopy(scaled, 0, o, 44, dataLen);
        return o;
    }
}
