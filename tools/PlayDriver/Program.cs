using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;

// Conductor de QA: arranca el juego exportado con un perfil de prueba, captura solo su ventana y le envía
// entradas normales de Windows (SendInput). Antes de cada acción comprueba que la ventana activa es la del juego;
// si el foco se pierde, suelta todo y falla: cambiar de ventana a mano detiene la prueba.
// Uso y comandos: tools\PlayDriver\README.md

return Driver.Run(args);

internal static class Driver
{
    private const int ExitUsage = 2, ExitNoGame = 3, ExitFocus = 4, ExitBusy = 5;
    private static readonly string StateDir = Path.Combine(Path.GetTempPath(), "PlutonPlayDriver");
    private static string _session = "qa";
    private static string StatePath => Path.Combine(StateDir, _session + ".json");
    private static readonly HashSet<ushort> Held = new();
    private static readonly HashSet<uint> HeldButtons = new();
    private static string? _logPath;

    private sealed record State(int Pid, string Log, string Exe);

    public static int Run(string[] argv)
    {
        var args = argv.ToList();
        var si = args.IndexOf("--session");
        if (si >= 0 && si + 1 < args.Count) { _session = args[si + 1]; args.RemoveRange(si, 2); }
        if (args.Count == 0) { Console.Error.WriteLine("Sin comando. Ver tools/PlayDriver/README.md"); return ExitUsage; }
        Directory.CreateDirectory(StateDir);

        // Un solo conductor por escritorio: el teclado y el ratón son únicos.
        using var mutex = new Mutex(false, "Global\\PlutonPlayDriver");
        var owned = false;
        try { owned = mutex.WaitOne(TimeSpan.FromSeconds(120)); }
        catch (AbandonedMutexException) { owned = true; }
        if (!owned) { Console.Error.WriteLine("Otro conductor está usando el escritorio"); return ExitBusy; }
        try
        {
            return Execute(args);
        }
        catch (FocusLost e)
        {
            ReleaseAll();
            Console.Error.WriteLine("FOCO PERDIDO: " + e.Message);
            Log("FOCO PERDIDO " + e.Message);
            return ExitFocus;
        }
        catch (NoGame e)
        {
            Console.Error.WriteLine("SIN JUEGO: " + e.Message);
            return ExitNoGame;
        }
        finally
        {
            ReleaseAll();
            mutex.ReleaseMutex();
        }
    }

    private static int Execute(List<string> a)
    {
        var cmd = a[0].ToLowerInvariant();
        var rest = a.Skip(1).ToList();
        if (cmd == "start") return Start(rest);
        var state = LoadState();
        _logPath = state.Log;
        var game = FindGame(state);
        Log(string.Join(' ', a));
        switch (cmd)
        {
            case "stop":
                game.proc.CloseMainWindow();
                if (!game.proc.WaitForExit(15000)) game.proc.Kill();
                File.Delete(StatePath);
                Console.WriteLine("cerrado");
                return 0;
            case "info": return Info(game);
            case "shot":
                return Shot(game.hwnd, rest);
            case "wait":
                Thread.Sleep(int.Parse(rest[0]));
                return 0;
            case "key":
            {
                // key W 800 · key Shift+W 1500 · key Tab (toque breve)
                Focus(game.hwnd);
                var keys = rest[0].Split('+').Select(Vk).ToArray();
                var ms = rest.Count > 1 ? int.Parse(rest[1]) : 60;
                foreach (var k in keys) KeyDown(k);
                Hold(game.hwnd, ms);
                foreach (var k in keys.Reverse()) KeyUp(k);
                return 0;
            }
            case "keydown": Focus(game.hwnd); KeyDown(Vk(rest[0])); Held.Clear(); return 0;
            case "keyup": Focus(game.hwnd); KeyUp(Vk(rest[0])); return 0;
            case "type":
                Focus(game.hwnd);
                TypeText(string.Join(' ', rest), game.hwnd);
                return 0;
            case "move":
            {
                // Coordenadas de cliente de la ventana.
                Focus(game.hwnd);
                MoveTo(game.hwnd, int.Parse(rest[0]), int.Parse(rest[1]));
                return 0;
            }
            case "click":
            {
                // click X Y [left|right] [ms]
                Focus(game.hwnd);
                MoveTo(game.hwnd, int.Parse(rest[0]), int.Parse(rest[1]));
                Thread.Sleep(60);
                var b = rest.Count > 2 ? rest[2] : "left";
                Button(b, true);
                Hold(game.hwnd, rest.Count > 3 ? int.Parse(rest[3]) : 50);
                Button(b, false);
                return 0;
            }
            case "press":
            {
                // Clic en el sitio actual, para primera persona con ratón capturado: press [left|right] [ms]
                Focus(game.hwnd);
                var b = rest.Count > 0 ? rest[0] : "left";
                Button(b, true);
                Hold(game.hwnd, rest.Count > 1 ? int.Parse(rest[1]) : 50);
                Button(b, false);
                return 0;
            }
            case "look":
            {
                // Movimiento relativo real: look DX DY [ms], repartido para que el juego lo reciba como ratón normal.
                Focus(game.hwnd);
                Look(game.hwnd, int.Parse(rest[0]), int.Parse(rest[1]), rest.Count > 2 ? int.Parse(rest[2]) : 200);
                return 0;
            }
            case "drag":
            {
                Focus(game.hwnd);
                MoveTo(game.hwnd, int.Parse(rest[0]), int.Parse(rest[1]));
                Thread.Sleep(80);
                Button("left", true);
                Thread.Sleep(80);
                MoveTo(game.hwnd, (int.Parse(rest[0]) + int.Parse(rest[2])) / 2, (int.Parse(rest[1]) + int.Parse(rest[3])) / 2);
                Thread.Sleep(80);
                MoveTo(game.hwnd, int.Parse(rest[2]), int.Parse(rest[3]));
                Thread.Sleep(120);
                Button("left", false);
                return 0;
            }
            case "scroll":
                Focus(game.hwnd);
                Scroll(int.Parse(rest[0]));
                return 0;
            default:
                Console.Error.WriteLine("Comando desconocido: " + cmd);
                return ExitUsage;
        }
    }

    // ---------- Sesión ----------

    private static int Start(List<string> a)
    {
        string Opt(string name, string fallback)
        {
            var i = a.IndexOf("--" + name);
            return i >= 0 && i + 1 < a.Count ? a[i + 1] : fallback;
        }
        var build = Opt("build", "");
        var profile = Opt("profile", "test-qa");
        if (!profile.StartsWith("test", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("El perfil debe empezar por «test»: nunca se usan los guardados del titular");
            return ExitUsage;
        }
        var exe = Path.GetFullPath(Path.Combine(build, "client", "Pluton.exe"));
        if (!File.Exists(exe)) { Console.Error.WriteLine("No existe " + exe); return ExitUsage; }
        if (File.Exists(StatePath))
        {
            var old = JsonSerializer.Deserialize<State>(File.ReadAllText(StatePath))!;
            if (Alive(old.Pid)) { Console.Error.WriteLine($"La sesión {_session} ya tiene el juego abierto (pid {old.Pid})"); return ExitBusy; }
        }
        // Solo una sesión visual a la vez en todo el escritorio: dos juegos se pelearían por el foco.
        foreach (var other in Directory.GetFiles(StateDir, "*.json"))
        {
            State? s;
            try { s = JsonSerializer.Deserialize<State>(File.ReadAllText(other)); }
            catch (Exception e) when (e is JsonException or IOException) { continue; }
            if (s != null && Alive(s.Pid)) { Console.Error.WriteLine($"OCUPADO: la sesión {Path.GetFileNameWithoutExtension(other)} está activa (pid {s.Pid}); reintenta más tarde"); return ExitBusy; }
        }
        var w = int.Parse(Opt("width", "1600"));
        var h = int.Parse(Opt("height", "900"));
        var screens = System.Windows.Forms.Screen.AllScreens;
        var index = int.Parse(Opt("monitor", "-1"));
        // Por omisión, un monitor secundario donde quepa: no estorba en el principal.
        var screen = index >= 0 && index < screens.Length ? screens[index]
            : screens.Where(s => !s.Primary && s.Bounds.Width >= w && s.Bounds.Height >= h).FirstOrDefault()
              ?? System.Windows.Forms.Screen.PrimaryScreen!;
        var px = screen.Bounds.X + Math.Max(0, (screen.Bounds.Width - w) / 2);
        var py = screen.Bounds.Y + Math.Max(0, (screen.Bounds.Height - h) / 2);
        var log = Opt("log", Path.Combine(StateDir, _session + "-acciones.log"));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(log))!);

        // Sin heredar la salida: quien lanza el conductor no debe quedarse esperando a que el juego se cierre.
        var psi = new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! };
        // Godot mide --position desde la esquina del escritorio virtual, no desde el monitor principal.
        var vx = screens.Min(s => s.Bounds.X);
        var vy = screens.Min(s => s.Bounds.Y);
        foreach (var x in new[] { "--resolution", $"{w}x{h}", "--position", $"{px - vx},{py - vy}", "--", "--profile", profile }) psi.ArgumentList.Add(x);
        var extra = a.IndexOf("--");
        if (extra >= 0) foreach (var x in a.Skip(extra + 1)) psi.ArgumentList.Add(x);
        var proc = Process.Start(psi)!;
        File.WriteAllText(StatePath, JsonSerializer.Serialize(new State(proc.Id, Path.GetFullPath(log), exe)));
        _logPath = Path.GetFullPath(log);
        Log($"start {exe} perfil={profile} {w}x{h} pantalla={screen.DeviceName}");

        var limit = Stopwatch.StartNew();
        while (limit.ElapsedMilliseconds < 60000)
        {
            proc.Refresh();
            if (proc.HasExited) { Console.Error.WriteLine("El juego terminó al arrancar"); return ExitNoGame; }
            if (proc.MainWindowHandle != IntPtr.Zero && Native.IsWindowVisible(proc.MainWindowHandle))
            {
                Console.WriteLine($"pid={proc.Id} ventana={proc.MainWindowHandle} pantalla={screen.DeviceName} cliente={w}x{h}");
                return 0;
            }
            Thread.Sleep(200);
        }
        Console.Error.WriteLine("La ventana no apareció en 60 s");
        return ExitNoGame;
    }

    private static bool Alive(int pid)
    {
        try { return !Process.GetProcessById(pid).HasExited; }
        catch (ArgumentException) { return false; }
    }

    private static State LoadState()
    {
        if (!File.Exists(StatePath)) throw new NoGame("no hay sesión; usa start");
        return JsonSerializer.Deserialize<State>(File.ReadAllText(StatePath))!;
    }

    private static (Process proc, IntPtr hwnd) FindGame(State s)
    {
        Process proc;
        try { proc = Process.GetProcessById(s.Pid); }
        catch (ArgumentException) { throw new NoGame("el proceso ya no existe"); }
        if (proc.HasExited) throw new NoGame("el juego terminó");
        if (!string.Equals(Path.GetFullPath(proc.MainModule?.FileName ?? ""), s.Exe, StringComparison.OrdinalIgnoreCase))
            throw new NoGame("el pid ya no es el juego");
        var hwnd = proc.MainWindowHandle;
        if (hwnd == IntPtr.Zero) throw new NoGame("sin ventana principal");
        return (proc, hwnd);
    }

    private static int Info((Process proc, IntPtr hwnd) g)
    {
        Native.GetClientRect(g.hwnd, out var r);
        var o = new Native.POINT();
        Native.ClientToScreen(g.hwnd, ref o);
        Console.WriteLine($"pid={g.proc.Id} cliente={r.Right}x{r.Bottom} origen={o.X},{o.Y} dpi={Native.GetDpiForWindow(g.hwnd)} primerplano={Native.GetForegroundWindow() == g.hwnd} responde={g.proc.Responding}");
        return 0;
    }

    private static void Log(string line)
    {
        if (_logPath == null) return;
        File.AppendAllText(_logPath, $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}");
    }

    // ---------- Captura ----------

    private static int Shot(IntPtr hwnd, List<string> a)
    {
        // shot RUTA.png [ANCHO_MAX] [X Y ANCHO ALTO de recorte en cliente]
        // Se copia de la pantalla solo la región de cliente, y solo si la ventana del juego está delante.
        Focus(hwnd);
        Thread.Sleep(50);
        Native.GetClientRect(hwnd, out var r);
        var o = new Native.POINT();
        Native.ClientToScreen(hwnd, ref o);
        var crop = a.Count >= 6
            ? new Rectangle(int.Parse(a[2]), int.Parse(a[3]), int.Parse(a[4]), int.Parse(a[5]))
            : new Rectangle(0, 0, r.Right, r.Bottom);
        using var bmp = new Bitmap(crop.Width, crop.Height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(o.X + crop.X, o.Y + crop.Y, 0, 0, crop.Size);
        if (Native.GetForegroundWindow() != hwnd) throw new FocusLost("durante la captura");

        var maxW = a.Count >= 2 ? int.Parse(a[1]) : 0;
        var path = Path.GetFullPath(a[0]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Detecta capturas negras: son un bloqueo que hay que señalar, no una prueba superada.
        var dark = MostlyBlack(bmp);
        if (maxW > 0 && bmp.Width > maxW)
        {
            var h = bmp.Height * maxW / bmp.Width;
            using var small = new Bitmap(maxW, h, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(small))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(bmp, 0, 0, maxW, h);
            }
            small.Save(path, ImageFormat.Png);
        }
        else bmp.Save(path, ImageFormat.Png);
        Console.WriteLine($"{path} {crop.Width}x{crop.Height}{(dark ? " AVISO: casi negra" : "")}");
        return 0;
    }

    private static bool MostlyBlack(Bitmap b)
    {
        var dark = 0;
        var total = 0;
        for (var y = 8; y < b.Height; y += Math.Max(1, b.Height / 24))
        for (var x = 8; x < b.Width; x += Math.Max(1, b.Width / 32))
        {
            var c = b.GetPixel(x, y);
            total++;
            if (c.R + c.G + c.B < 12) dark++;
        }
        return dark > total * 95 / 100;
    }

    // ---------- Foco ----------

    private static void Focus(IntPtr hwnd)
    {
        if (Native.GetForegroundWindow() == hwnd) return;
        Native.ShowWindow(hwnd, 9);
        // Windows solo cede el primer plano a quien acaba de recibir entrada: un toque de Alt lo habilita.
        Send(KeyInput(0x12, false));
        Send(KeyInput(0x12, true));
        Native.SetForegroundWindow(hwnd);
        var t = Stopwatch.StartNew();
        while (t.ElapsedMilliseconds < 2000)
        {
            if (Native.GetForegroundWindow() == hwnd)
            {
                Thread.Sleep(150);
                return;
            }
            Thread.Sleep(30);
        }
        throw new FocusLost("no se pudo activar la ventana del juego");
    }

    private static void Hold(IntPtr hwnd, int ms)
    {
        var t = Stopwatch.StartNew();
        while (t.ElapsedMilliseconds < ms)
        {
            Thread.Sleep(Math.Min(20, Math.Max(1, ms - (int)t.ElapsedMilliseconds)));
            if (Native.GetForegroundWindow() != hwnd) throw new FocusLost("otra ventana tomó el primer plano");
        }
    }

    // ---------- Entrada ----------

    private static void ReleaseAll()
    {
        foreach (var k in Held.ToList()) KeyUp(k);
        foreach (var b in HeldButtons.ToList()) Send(MouseInput(b, 0, 0, 0));
        HeldButtons.Clear();
    }

    private static void KeyDown(ushort vk) { Send(KeyInput(vk, false)); Held.Add(vk); }
    private static void KeyUp(ushort vk) { Send(KeyInput(vk, true)); Held.Remove(vk); }

    private static void Button(string name, bool down)
    {
        var (d, u) = name switch
        {
            "right" => (Native.MOUSEEVENTF_RIGHTDOWN, Native.MOUSEEVENTF_RIGHTUP),
            "middle" => (Native.MOUSEEVENTF_MIDDLEDOWN, Native.MOUSEEVENTF_MIDDLEUP),
            _ => (Native.MOUSEEVENTF_LEFTDOWN, Native.MOUSEEVENTF_LEFTUP),
        };
        Send(MouseInput(down ? d : u, 0, 0, 0));
        if (down) HeldButtons.Add(u);
        else HeldButtons.Remove(u);
    }

    private static void MoveTo(IntPtr hwnd, int x, int y)
    {
        Native.GetClientRect(hwnd, out var r);
        if (x < 0 || y < 0 || x >= r.Right || y >= r.Bottom) throw new ArgumentException($"({x},{y}) fuera del cliente {r.Right}x{r.Bottom}");
        var o = new Native.POINT { X = x, Y = y };
        Native.ClientToScreen(hwnd, ref o);
        Native.SetCursorPos(o.X, o.Y);
        // Un movimiento real tras colocar el cursor para que el juego reciba hover.
        Send(MouseInput(Native.MOUSEEVENTF_MOVE, 1, 0, 0));
        Send(MouseInput(Native.MOUSEEVENTF_MOVE, -1, 0, 0));
    }

    private static void Look(IntPtr hwnd, int dx, int dy, int ms)
    {
        var steps = Math.Max(1, ms / 10);
        int doneX = 0, doneY = 0;
        for (var i = 1; i <= steps; i++)
        {
            var tx = dx * i / steps;
            var ty = dy * i / steps;
            Send(MouseInput(Native.MOUSEEVENTF_MOVE, tx - doneX, ty - doneY, 0));
            doneX = tx;
            doneY = ty;
            Thread.Sleep(10);
            if (Native.GetForegroundWindow() != hwnd) throw new FocusLost("otra ventana tomó el primer plano");
        }
    }

    private static void Scroll(int ticks) => Send(MouseInput(Native.MOUSEEVENTF_WHEEL, 0, 0, ticks * 120));

    private static void TypeText(string text, IntPtr hwnd)
    {
        foreach (var ch in text)
        {
            var i = new Native.INPUT { type = 1 };
            i.U.ki = new Native.KEYBDINPUT { wScan = ch, dwFlags = Native.KEYEVENTF_UNICODE };
            Send(i);
            i.U.ki.dwFlags = Native.KEYEVENTF_UNICODE | Native.KEYEVENTF_KEYUP;
            Send(i);
            Hold(hwnd, 15);
        }
    }

    private static ushort Vk(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Length == 1 && char.IsLetterOrDigit(n[0])) return char.ToUpperInvariant(n[0]);
        if (n.Length >= 2 && n[0] == 'f' && int.TryParse(n[1..], out var fn) && fn is >= 1 and <= 12) return (ushort)(0x6F + fn);
        return n switch
        {
            "esc" or "escape" => 0x1B,
            "tab" => 0x09,
            "enter" or "return" => 0x0D,
            "space" => 0x20,
            "shift" => 0x10,
            "ctrl" or "control" => 0x11,
            "alt" => 0x12,
            "backspace" => 0x08,
            "delete" => 0x2E,
            "up" => 0x26,
            "down" => 0x28,
            "left" => 0x25,
            "right" => 0x27,
            _ => throw new ArgumentException("Tecla desconocida: " + name),
        };
    }

    private static Native.INPUT KeyInput(ushort vk, bool up)
    {
        var i = new Native.INPUT { type = 1 };
        // Se envía el código de exploración además del virtual: Godot usa teclas físicas en el mapa de acciones.
        i.U.ki = new Native.KEYBDINPUT { wVk = vk, wScan = (ushort)Native.MapVirtualKey(vk, 0), dwFlags = up ? Native.KEYEVENTF_KEYUP : 0 };
        if (vk is 0x25 or 0x26 or 0x27 or 0x28 or 0x2E) i.U.ki.dwFlags |= Native.KEYEVENTF_EXTENDEDKEY;
        return i;
    }

    private static Native.INPUT MouseInput(uint flags, int dx, int dy, int data)
    {
        var i = new Native.INPUT { type = 0 };
        i.U.mi = new Native.MOUSEINPUT { dx = dx, dy = dy, mouseData = data, dwFlags = flags };
        return i;
    }

    private static void Send(Native.INPUT input)
    {
        if (Native.SendInput(1, new[] { input }, Marshal.SizeOf<Native.INPUT>()) != 1)
            throw new FocusLost("SendInput rechazado (¿pantalla bloqueada o ventana con privilegios?)");
    }

    private sealed class FocusLost(string m) : Exception(m);
    private sealed class NoGame(string m) : Exception(m);
}

internal static class Native
{
    public const uint MOUSEEVENTF_MOVE = 1, MOUSEEVENTF_LEFTDOWN = 2, MOUSEEVENTF_LEFTUP = 4, MOUSEEVENTF_RIGHTDOWN = 8, MOUSEEVENTF_RIGHTUP = 16,
        MOUSEEVENTF_MIDDLEDOWN = 32, MOUSEEVENTF_MIDDLEUP = 64, MOUSEEVENTF_WHEEL = 0x800;
    public const uint KEYEVENTF_EXTENDEDKEY = 1, KEYEVENTF_KEYUP = 2, KEYEVENTF_UNICODE = 4;

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx, dy; public int mouseData; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)] public struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public InputUnion U; }

    [DllImport("user32.dll", SetLastError = true)] public static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint type);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
}
