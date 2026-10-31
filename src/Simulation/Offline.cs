namespace Pluton.Simulation;

public sealed record OfflineReport(long SecondsApplied, long SecondsAway, long Steps, long CreditsEarned);

/// <summary>
/// Progreso del mundo mientras el servidor estuvo detenido. Se ejecuta la simulación real con pasos gruesos:
/// respeta recetas, energía, enlaces, reservas, capacidad y rutas, y no puede crear recursos imposibles.
/// Aproximación documentada: con pasos grandes cada máquina completa como máximo una receta por paso,
/// así que el resultado queda por debajo de lo que habría producido en tiempo real, nunca por encima.
/// </summary>
public static class OfflineProgress
{
    private const int MaxSteps = 6000;

    public static OfflineReport? Apply(World world, long nowUnix)
    {
        var saved = world.SavedUnix;
        // Mundo sin marca de tiempo, reloj atrasado o sin ausencia: no hay nada que acreditar.
        if (saved <= 0 || nowUnix <= saved) return null;
        var away = nowUnix - saved;
        var cap = world.Content.Balance.OfflineCapHours * 3600L;
        var seconds = Math.Min(away, cap);
        var ticks = seconds * world.Content.Balance.TicksPerSecond;
        var scale = (int)Math.Max(1, (ticks + MaxSteps - 1) / MaxSteps);
        var steps = ticks / scale;
        var credits = world.Credits;

        world.TimeScale = scale;
        for (long i = 0; i < steps; i++) world.Step();
        world.TimeScale = 1;

        // Marca aplicada: reiniciar de nuevo sin más ausencia no vuelve a acreditar el mismo periodo.
        world.SavedUnix = nowUnix;
        world.ClearReplicationState();
        return new OfflineReport(seconds, away, steps, world.Credits - credits);
    }
}
