# Decisiones

| ID | Fecha | Decisión | Motivo |
| --- | --- | --- | --- |
| D-001 | 2026-10-31 | Godot 4.7.2 .NET + C# + .NET SDK 10.0.403 (LTS), versiones fijadas | Última estable. .NET 8 termina soporte el 10-nov-2026 y Godot admite versiones posteriores. Compilado y exportado aquí. |
| D-002 | 2026-10-31 | Servidor y clientes de carga como procesos .NET puros, sin Godot | Evita depender de una escena para la autoridad; arranque ligero; 12 procesos reales con coste mínimo. Se aparta de la «exportación de servidor Godot» sugerida porque el servidor no necesita motor. |
| D-003 | 2026-10-31 | No usar la red de alto nivel de Godot ni GodotSteam | GodotSteam no tiene build .NET oficial. Una interfaz `ITransport` propia en C# permite LiteNetLib y Steam sin tocar reglas. Demostrado: la misma sesión funciona por ambos. |
| D-004 | 2026-10-31 | LiteNetLib 2.1.5 (MIT) para LAN/IP en lugar de ENet | C# puro, canales fiable y secuenciado, repositorio activo. Sus opciones de simulación de red **no** funcionan en release: se usa un relé UDP propio. |
| D-005 | 2026-10-31 | Steam con Steamworks.NET 2025.165.0 desde su release, no NuGet | El paquete NuGet (2024.8.0) está desfasado y no incluye `steam_api64`. Release del 29-oct-2026. |
| D-006 | 2026-10-31 | Movimiento: cliente predice, servidor valida velocidad y límites | El servidor no ejecuta física de escena. Coste: no valida colisión con máquinas; entre amigos es aceptable y no afecta a la economía. |
| D-007 | 2026-10-31 | Replicación por entidades sucias con el mismo códec que el guardado: acciones a 5 Hz, simulación a 1 Hz, comprimido una vez | Una sola definición de formato; tráfico medido 3145→810 kbit/s en el escenario medio. |
| D-008 | 2026-10-31 | Al desmontar: 100 % de coste y todo el contenido vuelven al depósito del planeta, aunque supere su capacidad | Regla simple sin pérdidas silenciosas. Ajustable en `refundPermille`. |
| D-009 | 2026-10-31 | Sin energía suficiente la producción se ralentiza proporcionalmente | Cuello de botella visible, sin apagones que bloqueen. |
| D-010 | 2026-10-31 | Pagos: mochila y depósito del planeta; contratos: además terminales, estaciones, plataformas y contenedores | Cumple «créditos compartidos, inventario personal, almacén común» con transacción todo o nada. |
| D-011 | 2026-10-31 | En dedicado, el primero en entrar a un mundo sin dueño pasa a ser propietario | Sencillo para alfas. Revisar con identidad Steam antes de la Beta. |
| D-012 | 2026-10-31 | Nombre comercial pendiente: existen «Planet Nine» en Steam (puzle 2026 y acción anterior) | Búsqueda sin conflicto directo, pero sin revisión de marcas. Decide el titular antes de la tienda. |
| D-013 | 2026-10-31 | Trenes: se reserva el trayecto completo al salir o no se sale; solo retiene vía el tren en marcha | Sin señales ni interbloqueos posibles, a costa de menos capacidad por vía. Suficiente hasta tener desvíos. Dos trenes parados en un andén pueden solaparse visualmente. |
| D-014 | 2026-10-31 | «Sin pantalla de carga»: cabina continua con niebla que oculta el relevo de entornos, 20 s fijos desde datos | Compacto y medible: sin vuelo orbital libre, como permite el encargo. El trayecto no añade espera si el destino tarda: sigue en vuelo hasta confirmar la llegada. |
| D-015 | 2026-10-31 | Progreso sin conexión con la simulación real a pasos gruesos, máximo 6000 pasos | Respeta todas las reglas sin un modelo paralelo. Subestima al completar como mucho una receta por paso, nunca sobreestima. Probado frente al tiempo real. |
| D-016 | 2026-10-31 | Regla de Marte: calor. Lo que genera calor rinde hasta un 20 % sin refrigeración | Decisión distinta a la energía; el radiador criogénico consume refrigerante de Plutón, lo que da motivo para volver. |
| D-017 | 2026-10-31 | Carga interplanetaria por plataformas, con envíos como entidades persistentes | La carga solo existe en un sitio en cada instante, incluso guardando en vuelo. Si falta la plataforma de destino, va al depósito: nunca se pierde. |
| D-018 | 2026-10-31 | Campaña de ~3 h con bot eficiente; la hipótesis de 6-10 h humanas queda sin verificar | No se añade espera artificial para alcanzar una cifra. Se ajustará con jugadores reales. |
