# Decisiones

| ID | Fecha | Decisión | Motivo |
| --- | --- | --- | --- |
| D-001 | 2026-10-31 | Godot 4.7.2 .NET + C# + .NET SDK 10.0.403 (LTS), versiones fijadas | Última estable. .NET 8 termina soporte el 10-nov-2026 y Godot admite versiones posteriores. Compilado y exportado aquí. |
| D-002 | 2026-10-31 | Servidor y clientes de carga como procesos .NET puros, sin Godot | Evita depender de una escena para la autoridad; arranque ligero; 12 procesos reales con coste mínimo. Se aparta de la «exportación de servidor Godot» sugerida porque el servidor no necesita motor. |
| D-003 | 2026-10-31 | No usar la red de alto nivel de Godot ni GodotSteam | GodotSteam no tiene build .NET oficial. Una interfaz `ITransport` propia en C# permite LiteNetLib y Steam sin tocar reglas. |
| D-004 | 2026-10-31 | LiteNetLib 2.1.5 (MIT) para LAN/IP en lugar de ENet | C# puro, canales fiable y secuenciado, simulación integrada de latencia y pérdida para las pruebas de red, repositorio activo. |
| D-005 | 2026-10-31 | Steam con Steamworks.NET 2025.165.0 desde su release, no NuGet | El paquete NuGet (2024.8.0) está desfasado y no incluye `steam_api64`. Release del 29-oct-2026. Binding verificado (ver TEST_REPORTS T-004). |
| D-006 | 2026-10-31 | Movimiento: cliente predice, servidor valida velocidad y límites | El servidor no ejecuta física de escena. Coste: no valida colisión con máquinas; en un cooperativo entre amigos es aceptable y no afecta a la economía. |
| D-007 | 2026-10-31 | Replicación por entidades sucias a 5 Hz con el mismo códec que el guardado | Una sola definición de formato; instantánea y delta coherentes tras vaciar cambios en el tick. |
| D-008 | 2026-10-31 | Al desmontar: 100 % de coste y todo el contenido vuelven al depósito del planeta, aunque supere su capacidad | Regla simple sin pérdidas silenciosas. Ajustable en `refundPermille`. |
| D-009 | 2026-10-31 | Sin energía suficiente la producción se ralentiza proporcionalmente | Cuello de botella visible, sin apagones que bloqueen. Núcleo auxiliar no desmontable evita el bloqueo inicial. |
| D-010 | 2026-10-31 | Pagos de construcción y mejoras: mochila del jugador y depósito del planeta; contratos: además terminales y contenedores | Cumple «créditos compartidos, inventario personal, almacén común» con transacción todo o nada. |
| D-011 | 2026-10-31 | En dedicado, el primero en entrar a un mundo sin dueño pasa a ser propietario | Sencillo para alfas. Revisar con identidad Steam antes de la Beta. |
| D-012 | 2026-10-31 | Nombre comercial pendiente: existen «Planet Nine» en Steam (puzle 2026 y acción anterior) | Búsqueda sin conflicto directo con «PLUTON — Noveno Planeta», pero sin revisión de marcas. Decidir antes de la tienda. |
