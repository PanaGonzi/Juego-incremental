# Roadmap

Estados: pendiente · en curso · bloqueada · implementada (pendiente de prueba) · verificada. Hitos sin fecha prometida. «Verificada» indica el alcance real: salvo aviso, una sola máquina.

## M0 — Preparación y riesgo de red: cerrado

| ID | P | Tarea | Dep. | Estado | Criterio | Evidencia |
| --- | --- | --- | --- | --- | --- | --- |
| M0-1 | A | Entorno reproducible con versiones fijadas | — | verificada | `setup.ps1` con hashes | T-001 |
| M0-2 | A | Servidor, dos clientes y acción autoritativa | M0-1 | verificada | Procesos reales, sin divergencia | T-010 |
| M0-3 | A | Desconexión controlada y rechazo con motivo | M0-2 | verificada | Pruebas de red | T-003 |
| M0-4 | A | Estudio del transporte Steam | M0-1 | verificada (local) | Sesión real por Steam Sockets | T-004 |
| M0-5 | A | Conexión Steam entre dos cuentas/redes | — | **bloqueada: requiere intervención del usuario** | Segunda cuenta o probador | — |

## A0.1 — Primer bucle cooperativo: entregada (0.1.0, sustituida por corrección crítica)

| ID | P | Tarea | Estado | Evidencia |
| --- | --- | --- | --- | --- |
| A1-1…4 | A | Economía, cliente 3D, guardado, dos jugadores | verificada (misma máquina) | T-005, T-007, T-008 |
| A1-5 | A | 10-15 min de juego real | implementada: bot 9,4 min. **Falta persona** | T-005 |
| A1-6 | M | Dos PC reales | **bloqueada: requiere intervención del usuario** | — |

## A0.2 — Tren y prueba de planetas: cerrado en alcance local

| ID | P | Tarea | Estado | Evidencia |
| --- | --- | --- | --- | --- |
| A2-1…3 | A | Vías, estaciones, tren que aumenta la capacidad | verificada | T-005, T-008; tren ≥ 1,5× los cercanos |
| A2-4…5 | A | Bots realistas; 4, 8 y 12 clientes; 50/100/150 ms con pérdida | verificada | T-010, T-011 |
| A2-6 | A | Dos entornos, jugadores en planetas distintos | verificada | T-014, T-011 |
| A2-7 | M | Filtrar deltas por planeta | pendiente: tráfico ya aceptable (68 kbit/s por cliente, medio) | PERFORMANCE |

## A0.3 — Alfa cooperativa ampliada

| ID | P | Tarea | Estado | Evidencia |
| --- | --- | --- | --- | --- |
| A3-1 | A | Producción encadenada | verificada | T-006 |
| A3-2 | A | Varias rutas y trenes sin colisión ni interbloqueo | verificada | T-005 |
| A3-3 | A | Permisos y expulsión en interfaz | implementada; reglas verificadas, panel sin recorrido visual | T-005 |
| A3-4 | A | Entrada tardía, reconexión, servidor exportado | verificada | T-003, T-013 |
| A3-5 | A | `SteamTransport` | implementada, verificada en pares locales | T-004 |
| A3-6 | A | Lobby, invitaciones y unión por Steam en menú | **bloqueada: requiere intervención del usuario** (AppID propio, segunda cuenta) | — |

## A0.4 — Expansión interplanetaria

| ID | P | Tarea | Estado | Evidencia |
| --- | --- | --- | --- | --- |
| A4-1 | A | Marte jugable, recursos y regla térmica propios | verificada | T-005, recorrido `mars` |
| A4-2 | A | Viaje continuo y guardado en tránsito y con carga en vuelo | verificada | T-005, T-014 |
| A4-3 | A | Viajes independientes de varios jugadores | verificada con bots | T-011, T-012 |

## A0.5 — Contenido y evento

| ID | P | Tarea | Estado | Evidencia |
| --- | --- | --- | --- | --- |
| A5-1 | A | Objetivos alcanzables sin depuración | verificada con bot | T-006 |
| A5-2 | A | Progreso sin conexión del mundo | verificada en lógica; aviso al entrar sin recorrido visual | T-005 |
| A5-3 | A | Halloween activable, recompensas permanentes | verificada | T-005, recorrido `halloween` |
| A5-4 | M | Equilibrio y duración con personas | **bloqueada: requiere juego humano** | — |

## Beta

| ID | P | Tarea | Estado |
| --- | --- | --- | --- |
| B-1 | A | Inglés, controles reasignables, ultrawide | verificada (revisión de inglés nativo pendiente) |
| B-2 | A | Ensayo de 60 min, escenario medio, anfitrión + 11 | verificada (ver T-012 y su repetición) |
| B-3 | A | Tutorial propio con ayudas contextuales | pendiente |
| B-4 | M | Dibujo instanciado de vías y LOD | pendiente; hoy cumple el objetivo sin ello |
| B-5 | M | Exportar y comprobar servidor Linux | pendiente; sin entorno Linux aquí, no se anuncia |
| B-6 | M | Arte: variedad, animación de jugadores, audio ambiente | pendiente |
| B-7 | B | Prestigio opcional | pendiente |
| B-8 | A | Desvíos y señales | pendiente |

## RC

Paquetes, instalación limpia en otro equipo, validación online real, materiales de tienda. Requisitos del titular en [STEAM_RELEASE](STEAM_RELEASE.md).
