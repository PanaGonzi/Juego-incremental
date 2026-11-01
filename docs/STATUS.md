# Estado

**Última build estable:** `builds/alpha-0.4.1` (commit babc91c, etiqueta `alpha-0.4.1`, protocolo 4, guardado 4). Abrir con `Jugar.bat`; instrucciones en su `LEEME.txt`.

**Hitos:** M0, A0.1, A0.2, A0.4 y evento/offline de A0.5 implementados, con verificación en una sola máquina. A0.3 completo salvo Steam en el menú. Siguiente trabajo: Beta (ver [ROADMAP](ROADMAP.md)).

**El titular prueba:** nada registrado. Comentarios en [FEEDBACK](FEEDBACK.md).

**Bloqueado, requiere intervención del usuario:**

0. Ventana «Seguridad de Windows» (proceso `PickerHost`, abierta el 01-11-2026 a las 05:07, origen desconocido) con el primer plano: impide a `PlayDriver` jugar con entradas reales. No se ha cerrado ni se ha forzado el foco. Al resolverla, repetir la QA de primera experiencia (E-6) con un perfil nuevo.
1. Jugar: duración real de la campaña, comprensión de objetivos y sensación general. Es lo que más información aportaría ahora.
2. Dos PC o un amigo: LAN e Internet reales.
3. Segunda cuenta de Steam o probador, y más adelante AppID propio: invitaciones y relays.
4. Decisiones de [STEAM_RELEASE](STEAM_RELEASE.md): nombre, cuenta, pago.

**Riesgos:**

1. Ninguna persona ha jugado todavía: todo lo verificado son bots y recorridos automáticos.
2. BUG-001, reinicio de GPU una vez, sin repetir; este PC los registra desde antes del proyecto.
3. Rendimiento solo medido en un equipo potente.
