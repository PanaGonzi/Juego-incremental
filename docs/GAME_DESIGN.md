# Diseño

Bucle: **limpiar → recuperar → extraer → procesar → transportar → vender/contratos → mejorar → siguiente planeta.**

Todos los números están en `content/`. La prueba `Full_campaign...` juega la partida entera con comandos válidos: si un cambio de equilibrio rompe la progresión, falla.

## Cadenas

| Cadena | Máquina | Tiempo | Precio de venta |
| --- | --- | --- | --- |
| Limpieza → chatarra / hielo | Cortador | 5 cortes/s | 1 / 1 |
| 2 chatarra → 1 material recuperado | Recicladora | 3 s | 3 |
| Yacimiento → 1 mineral | Extractor | 2,5 s (1 s en ricos) | 1 |
| 2 mineral → 1 lingote | Fundición | 4 s | 5 |
| 2 lingotes → 1 pieza | Ensambladora | 5 s | 16 |
| Yacimiento → 1 hielo | Extractor de hielo | 2 s | 1 |
| 2 hielo → 1 refrigerante | Enfriadora | 4 s | 6 |
| 2 piezas + 2 refrigerante → 1 módulo | Ensambladora criogénica | 10 s | 80 |
| Yacimiento marciano → 1 silicio | Extractor de silicio | 2 s | 2 |
| 2 silicio → 1 oblea | Refinería | 5 s | 24 |
| 3 obleas + 1 módulo → 1 núcleo | Fábrica de núcleos | 15 s | 420 |

## Planetas

- **Plutón:** recuperación, ferrocarril y criogenia. Yacimientos ricos fuera del alcance de los enlaces: el tren se justifica por sí solo.
- **Marte:** silicio y calor. Lo que genera calor rinde hasta un 20 % sin refrigeración. Radiador pasivo o criogénico, este con refrigerante de Plutón. Los núcleos necesitan módulos de Plutón.
- **Relación:** lanzadera para jugadores (20 s, mochila) y plataformas de carga para mercancía.

Objetivo final: contrato **Proyecto Noveno Planeta** (6 núcleos y 6 módulos).

## Reglas contra duplicaciones

Contratos de pago único. Desmontar devuelve todo al depósito. La carga existe en un solo sitio, también en tren o en vuelo. Eventos: su material aparece una sola vez por mundo.

## Sin conexión

Máximo 8 h, una vez al reactivar el servidor, con la simulación real a pasos gruesos. Subestima por diseño (D-015). Un reloj atrasado no acredita nada.

## Evento: El Expreso de las Sombras

Datos en `content/events.json`. Se activa por temporada o a mano (panel de jugadores; `--event halloween` en el dedicado). Ramal con calabazas y faroles, contrato de limpieza y entregas por tren, tren espectral permanente del mundo. Desactivarlo no quita recompensas.

## Pendiente de validar con personas

Duración (bot eficiente: 2,9 h; hipótesis 6-10 h), claridad de los objetivos y ritmo de Marte, que en el bot resulta muy rápido una vez preparado.
