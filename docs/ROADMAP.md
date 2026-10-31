# Roadmap

Estados: pendiente · en curso · bloqueada · implementada (pendiente de prueba) · verificada. Hitos sin fecha prometida.

## M0 — Preparación y riesgo de red: **cerrado** (Steam entre cuentas queda como bloqueo externo)

| ID | P | Tarea | Dep. | Estado | Criterio | Evidencia |
| --- | --- | --- | --- | --- | --- | --- |
| M0-1 | A | Entorno reproducible con versiones fijadas | — | verificada | `tools/setup.ps1` con hashes | T-001 |
| M0-2 | A | Servidor, dos clientes y acción autoritativa | M0-1 | verificada | Procesos reales, sin divergencia | T-002 |
| M0-3 | A | Desconexión controlada y rechazo con motivo | M0-2 | verificada | Pruebas de red | T-003 |
| M0-4 | A | Estudio del transporte Steam | M0-1 | verificada (alcance local) | Binding, relays, canales, lobby | T-004 |
| M0-5 | A | Conexión Steam entre dos cuentas/redes | STEAM-1 | **bloqueada** | Falta segunda cuenta o probador | — |

## A0.1 — Primer bucle cooperativo: **alfa entregada, falta juego humano**

| ID | P | Tarea | Dep. | Estado | Criterio | Evidencia |
| --- | --- | --- | --- | --- | --- | --- |
| A1-1 | A | Economía, recetas, energía, enlaces, contratos, mejoras | M0 | verificada | Pruebas lógicas | T-005 |
| A1-2 | A | Limpieza, construcción, panel, venta en cliente 3D | A1-1 | verificada | Recorrido en build exportada | T-006 |
| A1-3 | A | Guardado, autosave, reanudar, respaldo | A1-1 | verificada | Pruebas y recorrido | T-005, T-006 |
| A1-4 | A | Dos jugadores en el mismo ciclo | A1-2 | verificada (misma máquina) | Host y dedicado con clientes Godot | T-007 |
| A1-5 | A | 10-15 min de juego real | A1-2 | implementada | Bot competente: 9,4 min. **Falta persona** | T-005 |
| A1-6 | M | Dos PCs reales en LAN | A1-4 | pendiente | Requiere al titular | — |

## A0.2 — Tren y prueba de planetas: **siguiente**

| ID | P | Tarea | Dep. | Estado | Criterio |
| --- | --- | --- | --- | --- | --- |
| A2-1 | A | Grafo de vías y colocación por tramos | A0.1 | pendiente | Rectas y curvas con encaje, previsualización y cancelación sin coste |
| A2-2 | A | Dos estaciones, un tren, carga real | A2-1 | pendiente | Tiempos de carga, capacidad, motivo de espera visible |
| A2-3 | A | El tren aumenta la capacidad productiva | A2-2 | pendiente | Yacimientos lejanos solo rentables por tren, medido |
| A2-4 | A | Bots con carga económica realista | — | pendiente | Sin mayoría de rechazos por alcance |
| A2-5 | A | 4, 8 y 12 clientes, red simulada 50/100/150 ms, 1-3 % | A2-4 | pendiente | Sin divergencia ni retraso de tick |
| A2-6 | A | Prueba técnica de dos entornos y lanzadera | A0.1 | pendiente | Ida y vuelta sin corte, con memoria y tirones medidos, y jugadores en planetas distintos |
| A2-7 | M | Filtrar deltas por planeta | A2-6 | pendiente | Tráfico medido |

## Después

| Hito | Contenido |
| --- | --- |
| A0.3 | Producción encadenada, varias rutas, permisos en interfaz, integración Steam si hay acceso, servidor utilizable |
| A0.4 | Marte jugable, viajes continuos, guardado de tránsito |
| A0.5 | Más objetivos, progreso sin conexión, Halloween completo (la versión cosmética puede entrar antes) |
| Beta | Inglés, controles reasignables, 60 min con 12 clientes, escenario medio, arte y audio coherentes |
| RC | Paquetes, instalación limpia, validación online real, materiales de tienda |
