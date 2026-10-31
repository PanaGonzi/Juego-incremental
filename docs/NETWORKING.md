# Red

## Modalidades

| Modo | Servidor | Transporte | Estado |
| --- | --- | --- | --- |
| Solo | Hilo local | Memoria (`LoopbackHub`), sin sockets | Verificado |
| Anfitrión | Hilo local, 1 local + hasta 11 remotos | Memoria + LiteNetLib UDP (`CompositeTransport`) | Verificado con 2 clientes en una máquina |
| Dedicado | `Pluton.Server.exe` sin interfaz | LiteNetLib UDP | Verificado en una máquina; Linux NO VERIFICADO |
| Steam (lobby, invitación, relay) | — | Pendiente, ver [STEAM_RELEASE](STEAM_RELEASE.md) | Binding comprobado; conexión entre dos cuentas NO VERIFICADA |

Al salir el anfitrión se guarda el mundo y sus invitados reciben `reject_shutdown`. No hay migración de anfitrión.

## Protocolo (versión en `Protocol.Version`)

Canal 0 fiable y ordenado: saludo, comandos, resultados, instantánea, deltas, chat, firmas.
Canal 1 secuenciado: posiciones de jugadores, 10 Hz, solo de quienes están en el mismo planeta.

```text
Cliente → Hello(protocolo, versión, revisión de datos, clave, nombre, contraseña)
Servidor → Welcome → SnapshotChunk × N → SnapshotDone(tamaño, firma)   [mundo en tick T]
Servidor → Delta...                                                     [cambios tras T, en orden]
Cliente → Command(id, intención) → Result(id, ok, error)
Servidor → Check(tick, firma)  cada 5 s → el cliente compara y cuenta divergencias
```

## Autoridad

- `World.Apply` valida rol, existencia, planeta, alcance, enfriamiento y coste antes de modificar nada.
- Todo se ejecuta en el hilo del servidor: dos acciones sobre el mismo recurso se resuelven por orden de llegada.
- Idempotencia: el servidor recuerda por jugador los últimos 256 IDs de petición y devuelve la misma respuesta. El cliente reenvía lo pendiente al reconectar.
- Límites: paquete del cliente ≤ 2048 bytes, cubo de 80 acciones (recarga 40/s), texto de comando ≤ 64, chat ≤ 200, nombre ≤ 24. Paquete malformado: expulsión solo del emisor.
- Movimiento: velocidad máxima 14 m/s + tolerancia; rechaza NaN y fuera de límites con `Correct`.

## Identidad y rechazo

Clave aleatoria por perfil local (a sustituir o complementar con SteamID). La misma clave recupera jugador, posición e inventario y sustituye a la sesión anterior sin ocupar otra plaza. Plaza 13: `reject_full`. Motivos con mensaje: versión, datos, contraseña, sala llena, expulsión, cierre, servidor inaccesible, conexión perdida.

El cierre de conexión se difiere 500 ms: cortar en el acto descartaba el mensaje de rechazo (error detectado por las pruebas).

## Interés

Las posiciones ya se filtran por planeta. Los deltas de entidades aún se envían a todos: debe filtrarse por planeta antes de A0.4.
