# Estado

**Última build estable:** `builds/alpha-0.4.1` (commit babc91c, etiqueta `alpha-0.4.1`, protocolo 4, guardado 4). Abrir con `Jugar.bat`; instrucciones en su `LEEME.txt`.

**Última build de prueba:** `builds/alpha-0.5.0-qa4` (protocolo 7, guardado 5; commit en su `VERSION.txt`). Abrir con `Jugar.bat`; instrucciones en su `LEEME.txt`. Sin QA con entradas reales de Windows: la última es la de `0.5.0-qa2`.

**Hitos:** M0, A0.1, A0.2, A0.4 y evento/offline de A0.5 implementados, con verificación en una sola máquina. A0.3 completo salvo Steam en el menú. Experiencia de fábrica y construcción (E y C del [ROADMAP](ROADMAP.md)) integradas en `evolucion-0.5`. Cooperativo de fábrica con dos clientes reales y regresión de 12 clientes correctos en un solo PC (T-018, T-019).

**Pendiente con controles reales:** repetir sobre la build final la QA con `PlayDriver`, que toma el foco y el ratón: solo cuando el titular no esté usando el equipo.

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
