# Errores

| ID | Gravedad | Versión | Estado | Descripción | Reproducción |
| --- | --- | --- | --- | --- | --- |
| BUG-001 | Alta | pre-0.1.0 | Abierto, causa sin determinar | Pérdida de dispositivo Vulkan: TDR del driver AMD (`LKD_0x141`, `amdkmdag.sys`) y cierre del juego. El mundo se recupera del último autosave (pérdida máxima 120 s). | 1 de 6 recorridos automáticos a 1080p, en t≈50 s, sin acción especial. Este PC registró **50 eventos 141/117 en 30 días**, también antes del proyecto, así que puede ser inestabilidad previa de driver o hardware; no está descartado que el juego contribuya. |
| BUG-002 | Baja | pre-0.1.0 | Abierto | El espejo del cliente solo actualiza `Tick` al recibir cambios. Sin efecto en juego, pero no debe usarse como reloj. | Mundo sin actividad |

## Limitaciones conocidas de la alfa

Los controles no son reasignables todavía. Sin tutorial aparte de los objetivos. Sin inglés. Pendiente sonido de ambiente.
