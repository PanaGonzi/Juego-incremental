using Godot;

namespace Pluton.Game;

/// <summary>Modelos generados por código. La geometría está en ModelsBuilder.cs y el catálogo en ModelsCatalog.cs.</summary>
public static partial class Models
{
    /// <summary>Material suelto para primitivas del motor y efectos. Las piezas de Builder usan el material compartido.</summary>
    public static StandardMaterial3D Mat(string name) => Materials.Standard(name);
}

/// <summary>Sonido sintetizado en memoria; no hay archivos de audio externos.</summary>
public static class Sfx
{
    private static readonly Dictionary<string, AudioStreamWav> Streams = new();
    private static Node _host = null!;
    private static Settings _prefs = null!;
    private const int Rate = 22050;

    public static void Setup(Node host, Settings prefs)
    {
        _host = host;
        _prefs = prefs;
        Streams["cut"] = Make(0.09f, t => Noise() * 0.5f * Fade(t, 0.09f) + Mathf.Sin(t * 2200) * 0.2f * Fade(t, 0.09f));
        Streams["break"] = Make(0.35f, t => (Noise() * 0.7f + Mathf.Sin(t * 500 * (1 - t)) * 0.5f) * Fade(t, 0.35f));
        Streams["build"] = Make(0.3f, t => (Mathf.Sin(t * 620 * (1.3f - t)) * 0.7f + Noise() * 0.2f) * Fade(t, 0.3f));
        Streams["remove"] = Make(0.3f, t => Mathf.Sin(t * 900 * (1f - t * 1.8f)) * 0.5f * Fade(t, 0.3f));
        Streams["deposit"] = Make(0.13f, t => Mathf.Sin(t * 2600) * 0.45f * Fade(t, 0.13f));
        Streams["take"] = Make(0.13f, t => Mathf.Sin(t * 1900 + t * t * 5000) * 0.45f * Fade(t, 0.13f));
        Streams["sell"] = Make(0.4f, t => (Mathf.Sin(t * 5200) * 0.3f + Mathf.Sin(t * (t < 0.12f ? 4400 : 6600)) * 0.35f) * Fade(t, 0.4f));
        Streams["error"] = Make(0.16f, t => Mathf.Sign(Mathf.Sin(t * 950)) * 0.2f * Fade(t, 0.16f));
        Streams["objective"] = Make(0.55f, t => (Mathf.Sin(t * 3300) + Mathf.Sin(t * (t < 0.18f ? 3300 : t < 0.36f ? 4150 : 4950))) * 0.25f * Fade(t, 0.55f));
        Streams["ghost"] = MakeLoop(4f, t => (Mathf.Sin(t * Mathf.Tau * 110 + Mathf.Sin(t * Mathf.Tau * 0.5f) * 3f) * 0.16f + Mathf.Sin(t * Mathf.Tau * 165.5f) * 0.09f) * (0.6f + 0.4f * Mathf.Sin(t * Mathf.Tau * 0.25f)));
        // Viento: parciales enteros en 8 s para que el bucle no tenga salto.
        Streams["wind"] = MakeLoop(8f, t =>
        {
            float s = 0;
            for (var i = 1; i <= 9; i++)
                s += Mathf.Sin(t * Mathf.Tau * (30 + i * 11.125f) + i * 1.7f) * (1f / i);
            var swell = 0.55f + 0.45f * Mathf.Sin(t * Mathf.Tau / 8f);
            return s * swell * 0.1f;
        });
        Streams["hum"] = MakeLoop(1f, t => Mathf.Sin(t * Mathf.Tau * 60) * 0.3f + Mathf.Sin(t * Mathf.Tau * 180) * 0.1f + Mathf.Sin(t * Mathf.Tau * 2) * Mathf.Sin(t * Mathf.Tau * 240) * 0.08f);
    }

    private static float Fade(float t, float len) => Mathf.Clamp(1f - t / len, 0, 1);
    private static readonly RandomNumberGenerator Rng = new() { Seed = 42 };
    private static float Noise() => Rng.RandfRange(-1, 1);

    private static AudioStreamWav Make(float seconds, Func<float, float> fn)
    {
        var frames = (int)(seconds * Rate);
        var data = new byte[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            var s = (short)(Mathf.Clamp(fn(i / (float)Rate), -1, 1) * 32000);
            data[i * 2] = (byte)(s & 0xFF);
            data[i * 2 + 1] = (byte)(s >> 8);
        }
        return new AudioStreamWav { Data = data, Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false };
    }

    private static AudioStreamWav MakeLoop(float seconds, Func<float, float> fn)
    {
        var s = Make(seconds, fn);
        s.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        s.LoopEnd = (int)(seconds * Rate);
        return s;
    }

    public static AudioStream Hum => Streams["hum"];
    public static AudioStream Ghost => Streams["ghost"];
    public static AudioStream Wind => Streams["wind"];
    public static float AmbienceDb => Mathf.LinearToDb(Mathf.Max(0.001f, _prefs.MasterVolume * _prefs.SfxVolume * 0.6f));
    public static float MasterLinear => _prefs.MasterVolume;
    public static float MachineDb => Mathf.LinearToDb(Mathf.Max(0.001f, _prefs.MasterVolume * _prefs.MachineVolume));

    public static void Play(string name, float pitchVariation = 0f)
    {
        if (!Streams.TryGetValue(name, out var stream)) return;
        var p = new AudioStreamPlayer
        {
            Stream = stream,
            VolumeDb = Mathf.LinearToDb(Mathf.Max(0.001f, _prefs.MasterVolume * _prefs.SfxVolume)),
            PitchScale = 1f + (pitchVariation > 0 ? Rng.RandfRange(-pitchVariation, pitchVariation) : 0),
        };
        _host.AddChild(p);
        p.Finished += p.QueueFree;
        p.Play();
    }
}
