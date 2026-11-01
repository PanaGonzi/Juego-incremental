# Cooperativo: fábrica compartida

Build `builds\alpha-dev` sobre a757f90 más los cambios de este bloque, 01-11-2026. **Un solo PC** con conexión local (127.0.0.1): no acredita LAN, Internet ni Steam.

Ruta de interacción: dos clientes Godot exportados y, en dedicado, un servidor exportado. Las acciones se envían con los mismos comandos que la interfaz, **no** con teclado y ratón reales. Mundo preparado (`BeltWorld`): es una prueba de regresión, no de primera experiencia.

Sincronización entre clientes mediante archivos en un directorio común.

| Archivo | Caso |
| --- | --- |
| `host_a.log`, `host_b.log` | Anfitrión e invitado: todas las comprobaciones |
| `dedicated_a.log`, `dedicated_b.log`, `dedicated_server.log` | Dedicado y dos clientes |
| `host_cinta_a.png`, `host_cinta_b.png` | La misma cinta desde cada cliente, con anfitrión |
| `dedicated_cinta_a.png`, `dedicated_cinta_b.png` | La misma cinta desde cada cliente, con dedicado: 83/104 huecos en ambos, lingotes visibles |

Comprobado en ambos modos:

- Colocar la misma pieza a la vez: una sola pieza; créditos y material se cobran una vez.
- Sacar 70 + 70 lingotes de 100: se reparten 100 exactos; devolverlos a la vez conserva el total.
- Pedir la misma cinta a la vez: una sola cinta.
- Objetos dibujados iguales a la carga lógica en cada cliente.
- Desmontar a la vez una cinta cargada: la carga se conserva y la devolución no se duplica.
- B se desconecta y vuelve: misma identidad y mochila, no aparece otro jugador; la cinta sigue con carga.
- Máquinas y trazado de cintas idénticos en A y B en cada etapa (`GEOMETRIA ...: igual` de `coop-test.ps1`).
- 0 divergencias de firma.

Solo con anfitrión: guardar con la orden normal, cerrar y cargar conserva cintas, máquinas y totales.

**NO VERIFICADO:** dos personas con controles reales; guardar y recargar en el dedicado dentro de este escenario; cintas distintas construidas a la vez que se crucen.
