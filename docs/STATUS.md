# Estado

**Última build estable:** `builds/alpha-0.4.1` (commit babc91c, etiqueta `alpha-0.4.1`, protocolo 4, guardado 4). Abrir con `Jugar.bat`; instrucciones en su `LEEME.txt`.

**Última build de prueba:** `builds/alpha-0.5.0-qa4` (protocolo 7, guardado 5; commit en su `VERSION.txt`). Abrir con `Jugar.bat`; instrucciones en su `LEEME.txt`. Sin QA con entradas reales de Windows: la última es la de `0.5.0-qa2`.

**Hitos:** M0, A0.1, A0.2, A0.4 y evento/offline de A0.5 implementados, con verificación en una sola máquina. A0.3 completo salvo Steam en el menú.

**Rama `evolucion-0.5`, bloques E y C:** el código de E-0 a E-7 y de C-1 a C-6 está integrado, pero integrado no significa comprobado. El desglose por tarea, con lo implementado, lo comprobado y lo pendiente, está en el [ROADMAP](ROADMAP.md).

- **Comprobado con recorridos automáticos y xUnit, en un PC:** cintas, construcción, gestión, zona, tren, viaje, Marte, Halloween, ausencia, dos clientes sobre la misma fábrica con anfitrión y dedicado, y 12 clientes con el 13.º rechazado (T-018 a T-022).
- **Comprobado con entradas reales de Windows:** solo hasta `0.5.0-qa2`, y en parte.
- **No hecho:** revisión independiente de rendimiento (E-8); acabado del resto de máquinas (E-10); causa de BUG-005 (E-11).

**Pendiente con controles reales:** lista concreta en [PENDIENTE_CONTROLES_REALES](PENDIENTE_CONTROLES_REALES.md). Toma el foco y el ratón: solo cuando el titular no esté usando el equipo. No detiene el resto del trabajo.

**El titular prueba:** nada registrado. Comentarios en [FEEDBACK](FEEDBACK.md).

**Bloqueado, requiere intervención del usuario:**

1. Jugar: duración real de la campaña, comprensión de objetivos y sensación general. Es lo que más información aportaría ahora.
2. Dos PC o un amigo: LAN e Internet reales.
3. Segunda cuenta de Steam o probador, y más adelante AppID propio: invitaciones y relays.
4. Decisiones de [STEAM_RELEASE](STEAM_RELEASE.md): nombre, cuenta, pago.

**Riesgos:**

1. Ninguna persona ha jugado todavía: todo lo verificado son bots y recorridos automáticos.
2. BUG-001, reinicio de GPU una vez, sin repetir; este PC los registra desde antes del proyecto.
3. Rendimiento solo medido en un equipo potente.
