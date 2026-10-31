# Errores

| ID | Gravedad | Versión | Estado | Descripción | Reproducción |
| --- | --- | --- | --- | --- | --- |
| BUG-001 | Alta | 0.1.0 pre | Abierto, causa sin determinar, no repetido | Pérdida de dispositivo Vulkan: TDR del driver AMD (`LKD_0x141`, `amdkmdag.sys`) y cierre del juego. Se recupera del último autosave (máximo 120 s). | 1 vez en un recorrido a 1080p, t≈50 s. Este PC registró **50 eventos 141/117 en 30 días**, también antes del proyecto. No repetido en las más de 20 ejecuciones gráficas posteriores, pero no se puede descartar. |
| BUG-004 | Crítica | 0.1.0 | **Corregido en 0.2.0** | Caía el hilo del servidor si dos jugadores terminaban el mismo escombro en una ventana de replicación; en anfitrión, congelación sin aviso. | Prueba `Two_players_finishing_the_same_debris...`, rojo → verde |
| BUG-002 | Baja | 0.1.0 | Abierto | El espejo del cliente solo actualiza `Tick` al recibir cambios. No debe usarse como reloj. | Mundo sin actividad |
| BUG-005 | Media | 0.2.0 | En investigación | RTT máximo observado de hasta ~1 s con 150 ms configurados. | `net-matrix`, fila 150 ms |
| BUG-006 | Baja | 0.3.0 | Abierto | Dos trenes parados en el mismo andén se solapan visualmente. Sin efecto económico. | Dos trenes con el mismo destino |
| BUG-007 | Baja | 0.3.0 | Abierto | Cambiar de idioma en partida no retraduce la barra de herramientas hasta recargar. | Ajustes en pausa |

## Limitaciones de las alfas

Sin tutorial aparte de los objetivos. Sin desvíos ni señales ferroviarias. Sin LOD. Invitar por Steam sin integrar en el menú. Prestigio sin implementar (opcional y posterior).
