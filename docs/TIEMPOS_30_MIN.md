# Tiempos de los primeros 30 minutos

Prueba: `FirstThirtyMinutes_reaches_automation_and_upgrade_without_long_waits` en `tests/Pluton.Tests/FirstThirtyMinutesTests.cs`. Sin escena, sobre `World`, solo con comandos válidos.

```powershell
dotnet test tests/Pluton.Tests --filter "FullyQualifiedName~FirstThirtyMinutes_reaches" --logger "console;verbosity=detailed"
```

**Estos tiempos detectan bloqueos y esperas. No demuestran que el juego sea divertido ni cuánto tardará una persona: NO VERIFICADO con jugadores.**

## Perfil de ritmo (constantes de la prueba)

| Concepto | Valor | Origen |
| --- | --- | --- |
| Andar | 7 m/s, con un 20 % de rodeo | velocidad real de `GameView`; el rodeo es hipótesis |
| Alcance de corte | 4,4 m | `WorkReach` de `GameView` |
| Corte | enfriamiento real de `cutterCooldownTicks`, más 1 s de apuntar por escombro | datos y hipótesis |
| Leer un objetivo nuevo | 5 s | hipótesis |
| Decidir con menú o panel | 3 s | hipótesis |
| Colocar una máquina | 3 s | hipótesis |
| Tender una cinta | 6 s | hipótesis |
| Depositar o recoger | 1,5 s | hipótesis |

El guion ya conoce el camino: no se equivoca, no explora y no atraviesa colisiones reales. Una primera partida será más lenta, y no se ha medido cuánto.

**Espera sin decisión:** tramo en que el guion no tiene ninguna acción que adelante su objetivo. Antes de contarla prueba las decisiones pendientes (otro extractor, cinta de reciclado). Cortar chatarra sigue siendo posible en esos tramos, pero no se cuenta como decisión cuando no adelanta el objetivo.

## Hitos

| Hito | Antes | Después |
| --- | --- | --- |
| Primera acción | 00:06 | 00:06 |
| Primera máquina | 01:13 | 01:13 |
| Primera cinta | 02:05 | 02:05 |
| Primera cadena automática (lingote en la terminal por cinta) | 02:57 | 02:55 |
| Primer ingreso | 03:39 | 03:24 |
| Primera mejora significativa (Ensamblaje) | 04:26 | 04:05 |
| Segunda cadena (8 piezas) | 10:01 | 09:01 |
| Mejora de ritmo (Extracción) | 10:51 | 09:38 |
| Licencia ferroviaria | 13:41 | 10:51 |
| Primer tren | 25:19 | 22:06 |
| Primera entrega por tren | 25:30 | 22:18 |

«Antes» es `evolucion-0.5` (30cc3ee) con este mismo guion. La cadena y la mejora ya llegaban muy por debajo de 30 minutos; no se ha añadido espera para acercarse a esa cifra (D-018). Los 30 minutos alcanzan hasta el tren.

## Esperas más largas

| Antes | | Después | |
| --- | --- | --- | --- |
| 155,3 s | lingotes de la licencia ferroviaria | 45,5 s | lingotes de la Ensambladora |
| 90,3 s | piezas del objetivo | 45,2 s | lingotes de la licencia |
| 63,8 s | lingotes o piezas del tren | 42,7 s | piezas del objetivo |
| 52,3 s | lingotes de la Ensambladora | 41,0 s | lingotes o piezas del tren |
| 38,6 s | lingotes de Ensamblaje | 33,1 s | lingotes de Ensamblaje |

| Total | Antes | Después |
| --- | --- | --- |
| Duración hasta la entrega por tren | 25:30 | 22:18 |
| Espera acumulada | 685 s en 30 tramos | 496 s en 31 tramos |
| Espera máxima | 155 s | 45,5 s (la prueba exige ≤ 60 s) |
| Andar | 444 s | 444 s |
| Cortar | 126 s | 126 s |
| Acarreos a mano | 36 | 36 |

## Qué no mejora

- **Los acarreos siguen en 36 en este guion, aunque 8 ya no hacen falta.** Desde D-045 se paga también desde la terminal, así que el guion recoge a mano lo que no necesita. Probado el 02-11-2026 sin esos 8: quedan 28, de chatarra y reciclado anteriores a la cinta. Pero la partida pasa de 22:18 a 25:22 y una espera sube a 102,7 s, por encima del límite de 60. Causa exacta sin aislar. El guion se dejó como estaba: quitar acarreos no acorta por sí solo el recorrido.
- **La espera acumulada sigue siendo alta (37 % del recorrido)**, repartida en tramos cortos.
- **Vender chatarra a mano rinde más que la primera línea:** unos 12 ¤ por montón frente a 2 ¤/s de la línea. Es finito y exige ir a la terminal, así que no se ha tocado. Conviene observarlo con jugadores.

## Sin callejones

`Spending_badly_at_any_early_stage_never_blocks_progress` recorre 5 etapas tempranas, con y sin chatarra restante: gasta en contenedores inútiles, deja créditos y materiales a cero, y comprueba que se puede volver a producir y comprar la siguiente tecnología.

Regla añadida: si un planeta baja de `scrapRenewal.minPiles` (4) montones, aparece uno cada `intervalTicks` (30 s) en su primer campo. Antes la chatarra era finita y, agotada, no había forma de conseguir material recuperado. Desmontar sigue devolviendo el 100 %.
