# QA primera experiencia: NO REALIZADA (bloqueo de entorno)

## Versión y ruta de interacción

- Versión: PLUTON alfa 0.5.0-qa1, commit 78f99d2 (con cambios sin confirmar), fecha 2026-11-01T05:09:50.
- Ruta prevista: PlayDriver con SendInput, sesión `qa1`, perfil `test-qa1`, 1600x900.
- Resultado: ninguna entrada llegó al juego y no se obtuvo ninguna captura.

## Resultado

**El juego no se ha probado. Todo el objetivo queda NO VERIFICADO.**
No se ha visto ni una pantalla del juego, así que este informe no contiene ninguna conclusión sobre él.

## Recorrido con tiempos (`acciones.log`)

| Hora | Hecho |
|---|---|
| 05:10:18 | El primer `start` falla: PlayDriver no encuentra .NET (falta `DOTNET_ROOT`). Se define y se reintenta. |
| 05:10:23 | Arranca el juego. |
| 05:10:28 a 05:11:06 | `FOCO PERDIDO` en cada captura y en `move`. `info` indica `primerplano=False responde=True`. |
| 05:11:11 | `stop`, para reintentar con un arranque limpio. |
| 05:11:52 a 05:13:53 | `OCUPADO`: la sesión `cintas` (pid 37496) tiene el escritorio. Se reintenta cada 60 s. |
| 05:14:53 | Segundo arranque, correcto. |
| 05:15:08 y 05:15:21 | `FOCO PERDIDO`. La ventana en primer plano es "Seguridad de Windows" (proceso PickerHost). |
| 05:15:21 a 05:24:29 | Espera de 9 minutos sin enviar entradas. El diálogo sigue en primer plano. |
| 05:24:31 y 05:24:41 | `FOCO PERDIDO` de nuevo. |
| 05:24:52 | `stop`. No queda ningún proceso Pluton abierto. |

Tiempo hasta la primera acción útil y hasta el primer ingreso: **NO VERIFICADO**, no se llegó a jugar.

## Incidencias

### ENT-01: bloqueante, de entorno y no del juego
- **Pasos:** con "Seguridad de Windows" en primer plano, ejecutar `start` y después `shot`.
- **Esperado:** PlayDriver activa la ventana del juego y captura.
- **Observado:** `FOCO PERDIDO` en todos los intentos del segundo arranque, durante más de 9 minutos. El juego responde, pero nunca pasa a primer plano.
- **Captura:** no hay; el conductor no captura sin foco.
- **Sin confirmar:**
  - Quién abrió el diálogo y qué pide.
  - Si el diálogo ya estaba abierto en el primer arranque (05:10). La ventana en primer plano solo se comprobó a partir de las 05:15, así que la causa del primer fallo es desconocida.

### ENT-02: baja, de herramienta
- **Pasos:** ejecutar `PlayDriver.exe` en una consola nueva, sin `DOTNET_ROOT`.
- **Esperado:** el conductor arranca con el comando indicado en el encargo.
- **Observado:** sale con código 131 ("You must install .NET"). Se resuelve definiendo `DOTNET_ROOT="$env:LOCALAPPDATA\PlutonTools\dotnet"`.
- **Captura:** no aplica.

### ENT-03: baja, de herramienta
- **Pasos:** ejecutar `start` sin foco disponible.
- **Esperado:** `start` avisa de que no ha conseguido el foco.
- **Observado:** `start` devuelve la línea normal de `pid=... cliente=1600x900`, y el fallo solo aparece en el comando siguiente.
- **Captura:** no aplica.

## Acciones fuera del conductor

- Consulta de solo lectura a Win32 (`GetForegroundWindow`) para identificar la ventana en primer plano. No envía entradas ni cambia el foco.
- No se cerró ni se tocó el diálogo, y no se forzó el foco por otra vía: es un diálogo de seguridad que no abrió esta prueba, y el encargo limita el control a PlayDriver.

## Para repetir la prueba

1. El titular resuelve o cancela el diálogo "Seguridad de Windows".
2. Comprobar que ninguna otra sesión de PlayDriver ocupa el escritorio.
3. Relanzar esta prueba. El perfil `test-qa1` se abrió dos veces sin jugar; lo ocurrido en esos arranques al guardar no se ha comprobado, así que conviene usar un perfil nuevo si se quiere una primera experiencia limpia.
