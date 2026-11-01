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
        Streams["break"] = Make(0.35f, t => (Noise() * 0.52f + Mathf.Sin(t * 500 * (1 - t)) * 0.36f) * Fade(t, 0.35f));
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

        // Respuesta a acciones: golpes cortos con cuerpo grave, para distinguirlos de los avisos de interfaz.
        Streams["place"] = Make(0.42f, t => (Mathf.Sin(t * Mathf.Tau * (92f - t * 70f)) * 0.62f * Mathf.Exp(-t * 11f) + Mathf.Sin(t * Mathf.Tau * 830f) * 0.13f * Mathf.Exp(-t * 26f) + Noise() * 0.2f * Mathf.Exp(-t * 34f)) * Attack(t));
        Streams["belt_link"] = Make(0.34f, t =>
        {
            var second = t - 0.12f;
            var click = Mathf.Sin(t * Mathf.Tau * 640f) * 0.34f * Mathf.Exp(-t * 46f) + Noise() * 0.14f * Mathf.Exp(-t * 60f);
            var latch = second < 0 ? 0f : Mathf.Sin(second * Mathf.Tau * 960f) * 0.3f * Mathf.Exp(-second * 18f) + Mathf.Sin(second * Mathf.Tau * 180f) * 0.34f * Mathf.Exp(-second * 16f);
            return (click + latch) * Attack(t);
        });
        Streams["dismantle"] = Make(0.5f, t =>
        {
            var clatter = 0f;
            foreach (var (at, tone) in new[] { (0f, 410f), (0.09f, 610f), (0.2f, 330f), (0.31f, 520f) })
                if (t >= at) clatter += Mathf.Sin((t - at) * Mathf.Tau * tone) * 0.22f * Mathf.Exp(-(t - at) * 24f);
            return (clatter + Noise() * 0.16f * Mathf.Exp(-t * 9f)) * Attack(t);
        });
        Streams["upgrade"] = Make(0.85f, t =>
        {
            var s = 0f;
            foreach (var (at, tone) in new[] { (0f, 330f), (0.11f, 415f), (0.22f, 494f), (0.36f, 659f) })
                if (t >= at) s += (Mathf.Sin((t - at) * Mathf.Tau * tone) + 0.3f * Mathf.Sin((t - at) * Mathf.Tau * tone * 2f)) * 0.17f * Mathf.Exp(-(t - at) * 4.2f);
            return s * Attack(t);
        });

        // Bucles de máquina: parciales enteros en la duración exacta para que el bucle no tenga salto.
        Streams["furnace"] = MakeLoop(2f, t =>
        {
            var sway = 0.7f + 0.3f * Mathf.Sin(t * Mathf.Tau * 0.5f);
            return (Mathf.Sin(t * Mathf.Tau * 41f) * 0.17f + Mathf.Sin(t * Mathf.Tau * 58.5f + 1.1f) * 0.15f + Mathf.Sin(t * Mathf.Tau * 83f + 2.3f) * 0.08f + Mathf.Sin(t * Mathf.Tau * 137.5f) * 0.03f) * sway * 1.35f;
        });
        Streams["press"] = MakeLoop(1f, t =>
        {
            var phase = Mathf.PosMod(t * 2f, 1f) / 2f;
            var thump = Mathf.Sin(phase * Mathf.Tau * 72f) * Mathf.Exp(-phase * 15f) * 0.42f;
            return thump + Mathf.Sin(t * Mathf.Tau * 118f) * 0.06f + Mathf.Sin(t * Mathf.Tau * 236f) * 0.03f;
        });
        Streams["drill"] = MakeLoop(1f, t =>
        {
            var phase = Mathf.PosMod(t * 8f, 1f) / 8f;
            return Mathf.Sin(phase * Mathf.Tau * 190f) * Mathf.Exp(-phase * 44f) * 0.24f + Mathf.Sin(t * Mathf.Tau * 96f) * 0.13f + Mathf.Sin(t * Mathf.Tau * 288f) * 0.05f;
        });
        Streams["belt"] = MakeLoop(1f, t =>
        {
            var phase = Mathf.PosMod(t * 14f, 1f) / 14f;
            var roller = Mathf.Sin(phase * Mathf.Tau * 1180f) * Mathf.Exp(-phase * 150f) * 0.12f;
            float bed = 0;
            for (var i = 0; i < 7; i++) bed += Mathf.Sin(t * Mathf.Tau * (170f + i * 67f) + i * 2.4f) * 0.017f;
            return roller + bed + Mathf.Sin(t * Mathf.Tau * 49f) * 0.05f;
        });
        Streams["factory"] = MakeLoop(4f, t =>
        {
            var breath = 0.75f + 0.25f * Mathf.Sin(t * Mathf.Tau * 0.25f);
            return (Mathf.Sin(t * Mathf.Tau * 46.25f) * 0.2f + Mathf.Sin(t * Mathf.Tau * 69.5f + 0.8f) * 0.13f + Mathf.Sin(t * Mathf.Tau * 92.5f + 2f) * 0.07f + Mathf.Sin(t * Mathf.Tau * 139f + 3f) * 0.04f) * breath;
        });

        // Limitador en la salida: la suma de voces nunca recorta, aunque varias acciones coincidan.
        if (AudioServer.GetBusEffectCount(0) == 0) AudioServer.AddBusEffect(0, new AudioEffectHardLimiter { CeilingDb = -1f, PreGainDb = 0f });
    }

    /// <summary>Entrada de 4 ms: evita el chasquido de empezar en un valor alto.</summary>
    private static float Attack(float t) => Mathf.Clamp(t * 250f, 0, 1);

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
    public static float MachineLinear => _prefs.MasterVolume * _prefs.MachineVolume;
    public static AudioStream Belt => Streams["belt"];
    public static AudioStream Factory => Streams["factory"];

    /// <summary>Bucle de trabajo de una máquina: cada familia suena distinta y se reconoce sin mirarla.</summary>
    public static AudioStream LoopOf(string def) => Streams[def switch
    {
        "smelter" or "wafer_refinery" => "furnace",
        "recycler" or "assembler" or "cryo_assembler" or "nav_fab" => "press",
        "ore_extractor" or "ice_extractor" or "silicon_extractor" => "drill",
        _ => "hum",
    }];

    /// <summary>Medición de cada sonido generado: pico y eficaz en dBFS y salto del bucle. Para comprobar con herramienta, no de oído.</summary>
    public static IEnumerable<(string name, float peakDb, float rmsDb, float loopJump)> Levels()
    {
        foreach (var (name, stream) in Streams)
        {
            var data = stream.Data;
            var frames = data.Length / 2;
            float peak = 0;
            double sum = 0;
            float Sample(int i) => (short)(data[i * 2] | data[i * 2 + 1] << 8) / 32768f;
            for (var i = 0; i < frames; i++)
            {
                var v = Mathf.Abs(Sample(i));
                peak = Mathf.Max(peak, v);
                sum += v * v;
            }
            var jump = stream.LoopMode == AudioStreamWav.LoopModeEnum.Disabled ? 0f : Mathf.Abs(Sample(frames - 1) - Sample(0));
            yield return (name, Mathf.LinearToDb(Mathf.Max(peak, 1e-5f)), Mathf.LinearToDb(Mathf.Max((float)Math.Sqrt(sum / Math.Max(frames, 1)), 1e-5f)), jump);
        }
    }

    /// <summary>Sonido de una acción en el mundo: se oye desde donde ocurre, también si la hace otro jugador.</summary>
    public static void PlayAt(string name, Vector3 at, float pitchVariation = 0.05f)
    {
        if (!Streams.TryGetValue(name, out var stream) || !Allow(name)) return;
        var p = new AudioStreamPlayer3D
        {
            Stream = stream,
            VolumeDb = Mathf.LinearToDb(Mathf.Max(0.001f, _prefs.MasterVolume * _prefs.SfxVolume)),
            PitchScale = 1f + Rng.RandfRange(-pitchVariation, pitchVariation),
            UnitSize = 9,
            MaxDistance = 70,
            Position = at,
        };
        _host.AddChild(p);
        _voices++;
        p.Finished += () => { _voices--; p.QueueFree(); };
        p.Play();
    }

    private static int _voices;
    private static readonly Dictionary<string, ulong> LastPlayed = new();

    /// <summary>Mezcla limitada: pocas voces a la vez y sin repetir el mismo sonido en menos de 40 ms.</summary>
    private static bool Allow(string name)
    {
        var now = Time.GetTicksMsec();
        if (_voices >= 14 || LastPlayed.TryGetValue(name, out var last) && now - last < 40) return false;
        LastPlayed[name] = now;
        return true;
    }

    public static void Play(string name, float pitchVariation = 0f)
    {
        if (!Streams.TryGetValue(name, out var stream) || !Allow(name)) return;
        var p = new AudioStreamPlayer
        {
            Stream = stream,
            VolumeDb = Mathf.LinearToDb(Mathf.Max(0.001f, _prefs.MasterVolume * _prefs.SfxVolume)),
            PitchScale = 1f + (pitchVariation > 0 ? Rng.RandfRange(-pitchVariation, pitchVariation) : 0),
        };
        _host.AddChild(p);
        _voices++;
        p.Finished += () => { _voices--; p.QueueFree(); };
        p.Play();
    }
}
