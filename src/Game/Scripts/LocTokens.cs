using System.Text.RegularExpressions;

namespace Pluton.Game;

/// <summary>Sintaxis de los marcadores de tecla de los textos. Sin Godot: las pruebas recorren con ella todos los idiomas.</summary>
public static class LocTokens
{
    /// <summary>{accion} se sustituye por la tecla asignada. Empieza por letra: {0} es un argumento numérico, no una acción.</summary>
    public static readonly Regex Action = new(@"\{([a-z_][a-z_0-9]*)\}", RegexOptions.Compiled);
}
