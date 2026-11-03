# Estado

**Última build estable:** `builds/alpha-0.4.1` (commit babc91c, etiqueta `alpha-0.4.1`, protocolo 4, guardado 4). Abrir con `Jugar.bat`; instrucciones en su `LEEME.txt`.

**Última build de prueba:** `builds/alpha-0.5.0-qa9` (protocolo 10, guardado 7; commit 62a09bf). Abrir con `Jugar.bat`; instrucciones en su `LEEME.txt`. Regresión automática sobre el paquete (T-031). La anterior, `alpha-0.5.0-qa8` (protocolo 9, guardado 6), tuvo además entradas reales sobre `alpha-dev` (T-028).

**Novedades de la qa9 respecto a la qa8:** protocolo 10, guardado 7, migra desde el 6 con un guardado real. Incluye la nueva expedición (B-7), el cierre de H-4, H-6 y H-8, el servidor Linux comprobado en WSL2 (B-5) y los arreglos INC-34 a INC-36. Comprobado con xUnit, recorridos automáticos y cooperativo con anfitrión en `alpha-dev` (T-030).

**Hitos:** M0, A0.1, A0.2, A0.4 y evento/offline de A0.5 implementados, con verificación en una sola máquina. A0.3 completo salvo Steam en el menú.

**Rama `evolucion-0.5`, bloques E y C:** el código de E-0 a E-7 y de C-1 a C-6 está integrado, pero integrado no significa comprobado. El desglose por tarea, con lo implementado, lo comprobado y lo pendiente, está en el [ROADMAP](ROADMAP.md).

- **Comprobado con recorridos automáticos y xUnit, en un PC:** cintas, construcción, gestión, zona, tren, viaje, Marte, Halloween, ausencia, dos clientes sobre la misma fábrica con anfitrión y dedicado, y 12 clientes con el 13.º rechazado (T-018 a T-022).
- **Comprobado con entradas reales de Windows:** sesión de un agente sobre `alpha-dev`, casi toda la lista (T-028). Lo que queda, en [PENDIENTE_CONTROLES_REALES](PENDIENTE_CONTROLES_REALES.md).
- **Revisión independiente de rendimiento (E-8):** hecha, en [REVISION_E8](REVISION_E8.md). Cerrados después H-3, H-4, H-6 y H-8; sigue parcial H-10.
- **Mitigado sin resolver del todo:** BUG-005: causa medida y respuesta mejorada, pero con pérdida siguen los picos (T-023).

**B-8, señales y desvíos:** primer sistema implementado y comprobado en un PC con órdenes automáticas (T-024) y, para poner señales y ramales, con entradas reales (T-028). Con personas: **NO VERIFICADO**.

**Pendiente con controles reales:** lista concreta en [PENDIENTE_CONTROLES_REALES](PENDIENTE_CONTROLES_REALES.md). Toma el foco y el ratón: solo cuando el titular no esté usando el equipo. No detiene el resto del trabajo.

**El titular prueba:** nada registrado. Comentarios en [FEEDBACK](FEEDBACK.md).

**Bloqueado, requiere intervención del usuario:**

1. Jugar: duración real de la campaña, comprensión de objetivos y sensación general. Es lo que más información aportaría ahora.
2. Dos PC o un amigo: LAN e Internet reales.
3. Segunda cuenta de Steam o probador, y más adelante AppID propio: invitaciones y relays.
4. Decisiones de [STEAM_RELEASE](STEAM_RELEASE.md): nombre, cuenta, pago.

**Riesgos:**

1. Ninguna persona ha jugado todavía: todo lo verificado son bots, recorridos automáticos y un agente con teclado y ratón.
2. BUG-001, reinicio de GPU una vez, sin repetir; este PC los registra desde antes del proyecto.
3. Rendimiento solo medido en un equipo potente.
