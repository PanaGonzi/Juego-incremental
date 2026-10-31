# Diseño

Bucle: **limpiar → recuperar → extraer → procesar → transportar → vender/contratos → mejorar → siguiente planeta.**

Valores en `content/`: se ajustan con la prueba `New_game_reaches_all_objectives...`, que juega con comandos válidos y mide tiempos.

## Economía actual (Plutón)

| Cadena | Máquina | Tiempo | Precio de venta |
| --- | --- | --- | --- |
| Limpieza → chatarra / hielo | Cortador | 5 cortes/s por jugador | 1 / 1 |
| 2 chatarra → 1 material recuperado | Recicladora | 3 s | 3 |
| Yacimiento → 1 mineral | Extractor | 2,5 s | 1 |
| 2 mineral → 1 lingote | Fundición | 4 s | 5 |
| 2 lingotes → 1 pieza | Ensambladora | 5 s | 16 |
| Yacimiento → 1 hielo | Extractor de hielo | 2 s | 1 |
| 2 hielo → 1 refrigerante | Enfriadora | 4 s | 6 |
| 2 piezas + 2 refrigerante → 1 módulo | Ensambladora criogénica | 10 s | 80 |

La limpieza es finita. Yacimientos sin agotamiento, limitados por rendimiento.

Ingresos recurrentes: terminal de exportación (venta manual, o automática con reserva por objeto). Contratos: pago único.

Evitan bloqueos: núcleo auxiliar de 30 de energía, recicladora y terminal iniciales no desmontables, cortador siempre disponible.

## Alcance previsto

Plutón y Marte completos en la 1.0. Ferrocarril: A0.2. Prestigio y progreso sin conexión: después de estabilizar el bucle. Halloween: sistema de eventos por datos (A0.5, versión cosmética antes).
