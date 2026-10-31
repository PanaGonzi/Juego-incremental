# Steam

**Estado: no listo para Steam.** Faltan pruebas con personas y por Internet.

## Comprobado (AppID de prueba 480, una máquina y una cuenta; no es el del producto)

Steamworks.NET 2025.165.0 con .NET 10: inicialización, relays disponibles, lobby privado de 12, y **sesión real de servidor + 12 clientes por `SteamTransport`** sobre pares locales. Ver T-004.

## Pendiente técnico

1. Integrar `src/Steam` en el cliente: lobby, invitación y unión desde el menú.
2. Identidad por SteamID en lugar de clave local.
3. **Prueba entre dos cuentas y redes distintas, con relay real.**
4. Dedicado accesible por Steam (Valve gestiona certificados para servidores alojados; IP directa como alternativa).
5. Redistribuir `steam_api64.dll` y añadir su aviso (hoy no se incluye en los paquetes).

## Requiere al titular

Cuenta de Steamworks, datos fiscales y bancarios, pago de Steam Direct (revalidar importe y plazos vigentes), AppID, precio, cuentas de prueba, nombre comercial (D-012), aprobación para hacer pública la tienda.

## Borrador de textos: revisar antes de publicar

Corta: *Reconstruye la industria abandonada de Plutón. Limpia, extrae, automatiza, tiende vías y lleva tu fábrica hasta Marte, solo o con hasta 11 amigos.*

Funciones que existen hoy: fábrica en primera persona, ferrocarril con carga real, dos planetas con viaje continuo, cooperativo de hasta 12 con servidor dedicado, sin combate ni supervivencia. **No anunciar** invitaciones por Steam ni soporte Linux hasta verificarlos.

Requisitos: solo medido en Ryzen 7 5700X, 32 GB, RX 7800 XT. Mínimos sin determinar. Subida del anfitrión con fábrica media y 12 jugadores: ~660 kbit/s medidos.

## Borrador de declaración de contenido generado con IA

El código, los modelos, audio, shaders y textos entregados al jugador se han generado con Claude (Opus 5.5). Revisar el cuestionario vigente de Steamworks, y su texto legal, antes de presentarlo: la decisión y la firma corresponden al titular.

## Capturas

Las capturas de `logs/` son del juego real, de escenas de prueba: se rehará una selección en la Beta.
