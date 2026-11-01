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

## A0.5.x — Experiencia de fábrica

Encargo: [PROMPT_CLAUDE_CONTINUAR_QA_CINTAS_GRAFICOS.md](../PROMPT_CLAUDE_CONTINUAR_QA_CINTAS_GRAFICOS.md). Plan y reparto: [PLAN_0.5](PLAN_0.5.md). Reconciliado con el código, los commits y las evidencias el 01-11-2026.

Cómo leer las columnas: **Implementado** es que el código existe. **Comprobado** indica con qué: *auto* es un recorrido del cliente exportado con entrada inyectada o comandos de la interfaz; *real* es `PlayDriver` con entradas de Windows, siempre sobre la versión indicada. Nada de esto equivale a una persona jugando. Lo que queda por probar con teclado y ratón está en [PENDIENTE_CONTROLES_REALES](PENDIENTE_CONTROLES_REALES.md).

| ID | P | Tarea | Implementado | Comprobado | Pendiente |
| --- | --- | --- | --- | --- | --- |
| E-0 | A | Conductor de QA con entradas de Windows | sí | real sobre 0.4.1, `artifacts/qa/0.4.1/` | — |
| E-1 | A | Cintas con carga real, puertos, ruta, atasco, corte, migración v4 → v5 | sí | xUnit; auto `belts`, `beltsv4`; dos clientes T-018; real en parte, informe de qa2 | Repetir con entradas reales en la versión actual |
| E-2 | A | Brazos, multiherramienta y animaciones | sí | auto `artifacts/qa/0.5-fp`; real en qa2: la herramienta reacciona | 1080p, 3440×1440 y otros FOV; partículas de hielo y roca; sonido |
| E-3 | A | Controles, barra rápida e inventario con transferencias | sí | real en qa2: 1-9, L, clic derecho con selector, arrastrar | Mayús+clic, Ctrl+clic, fijar a la barra rápida |
| E-4 | A | Zona representativa industrial | sí | `artifacts/qa/0.5-zona`, real y auto | Montículo gris junto al pecio; vapor en movimiento; existencias en los puertos |
| E-5 | A | Menús, panel de máquina, objetivos, ajustes | sí | `artifacts/qa/0.5-arte`; correcciones INC-08 e INC-09 | Calidad baja y alta; escala de interfaz distinta de 100; panel con cooperativo real |
| E-6 | A | QA de primera experiencia con controles normales | sí, hecha sobre qa1 y qa2 | real: hasta el objetivo 10 de 23 | Repetir en la versión actual; fallo de energía; cerrar y reabrir; Retroceso y N |
| E-7 | A | Dos clientes viendo el mismo transporte; 12 clientes | sí | T-018, T-019, un solo PC y comandos de la interfaz | Dos personas o dos PC: NO VERIFICADO |
| E-8 | A | Revisión independiente de calidad y rendimiento con cintas cargadas | no | medidas parciales en PERFORMANCE y T-019 | La revisión independiente no se ha hecho |
| E-9 | M | Divisores y combinadores | sustituida por C-3 | — | — |
| E-10 | M | Acabado de Marte y del resto de máquinas | en parte: terreno y cielo de Marte, plataforma de carga | `artifacts/qa/0.5-marte/` | El resto de máquinas |
| E-11 | M | BUG-005: picos de respuesta | causa medida y mitigada: reenvío 1,3×RTT y lectura cada 5 ms | T-021, T-023, relé en un PC | Picos con pérdidas seguidas; canales separados sin probar; redes reales |

## A0.6 — Construcción, gestión y primeros 30 minutos

Encargo: [PROMPT_CLAUDE_CONSTRUCCION_PRIMEROS_30_MIN.md](../PROMPT_CLAUDE_CONSTRUCCION_PRIMEROS_30_MIN.md). Máximo dos subagentes a la vez, sin delegación en cascada.

| ID | P | Tarea | Implementado | Comprobado | Pendiente |
| --- | --- | --- | --- | --- | --- |
| C-1 | A | Cintas sin límite arbitrario, trazado automático, puntos intermedios, rutas alternativas, confirmación exacta, validación en servidor | sí: b0ff255, 50de932, 3229fa5, a657005, d8353f9 | xUnit; auto `beltslong`; real en qa2: R, M, clic de punto, Z | Pendientes reales: cuestas (el terreno es llano: solo simuladas); Retroceso y N con entradas reales |
| C-2 | A | Colocación consecutiva, previsualización con coste y motivo, desmontaje con lo que se recupera | sí: ed60e1e | auto `artifacts/qa/0.5-construccion`; real en parte, qa2 | Repetir con entradas reales |
| C-3 | A | Divisores y unificadores | sí como máquina: 1d1b828, 137d2b6 | xUnit; auto `manage`; unificador en el cliente, solo pruebas unitarias | Insertarlos en una cinta ya tendida: no existía, ver tarea 3 del encargo vigente |
| C-4 | A | Lectura de un vistazo, capacidad y producción real, seguir cadena | sí | `artifacts/qa/0.5-gestion`, real con H, C y Esc | Botón «Seguir cadena» con ratón real |
| C-5 | A | Progresión de los primeros 30 min | sí | `docs/TIEMPOS_30_MIN.md`, recorridos con guion, no con personas | 36 acarreos manuales todavía; duración con personas |
| C-6 | A | Zona inicial, respuesta sonora y visual, ajustes para reducir movimiento y efectos | sí | `artifacts/qa/0.5-zona` | El sonido no lo ha oído nadie |
| C-7 | A | Controles normales, dos jugadores sobre la misma construcción, rendimiento, regresión de 12 | en parte | T-018, T-019 con comandos de la interfaz | Controles reales; dos personas; antes y después de rendimiento completo |

## Beta

| ID | P | Tarea | Estado |
| --- | --- | --- | --- |
| B-1 | A | Inglés, controles reasignables, ultrawide | verificada (revisión de inglés nativo pendiente) |
| B-2 | A | Ensayo de 60 min, escenario medio, anfitrión + 11 | verificada con clientes automáticos (T-015, T-009) |
| B-3 | A | Tutorial propio con ayudas contextuales | implementada en parte: objetivos con marcador y distancia; validar con personas |
| B-4 | M | Dibujo instanciado de vías y LOD | pendiente; hoy cumple el objetivo sin ello |
| B-5 | M | Exportar y comprobar servidor Linux | pendiente; sin entorno Linux aquí, no se anuncia |
| B-6 | M | Arte: variedad, animación de jugadores, audio ambiente | pendiente |
| B-7 | B | Prestigio opcional | pendiente |
| B-8 | A | Desvíos y señales | pendiente; siguiente en cola tras lo anterior |

## RC

Paquetes, instalación limpia en otro equipo, validación online real, materiales de tienda. Requisitos del titular en [STEAM_RELEASE](STEAM_RELEASE.md).
