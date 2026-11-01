# Zona inicial de Plutón: evidencia

Capturas reales de `builds\alpha-dev` en esta rama. Las de `antes_` son de `evolucion-0.5` (74bb46c). Conducidas con PlayDriver y entradas normales del sistema, perfiles `test-zona*`, salvo las indicadas.

| Antes | Después | Qué comparar |
| --- | --- | --- |
| `antes_cinta_cerca.png` | `despues_cinta_cerca.png` | Cinta real con carga, a menos de 2 m: banda, bastidor, objetos. Mismo recorrido de teclas; el encuadre es aproximado |
| `antes_cinta_largo.png` | `despues_cinta_largo.png` | Tacos de la banda, flecha desconchada y carga no clonada |
| `antes_deposito_y_cinta.png` | `despues_deposito_y_cinta.png` | Salida del depósito y remate de la cinta |
| `antes_menuview_scrap.png` | `despues_menuview_scrap.png` | Encuadre fijo. Montón sin disco claro, viga con arista viva |
| `antes_menuview_smelter.png` | `despues_menuview_smelter.png` | Encuadre fijo. Pintura de máquina |
| `antes_menuview_items.png` | `despues_menuview_items.png` | Encuadre fijo. Objetos y material de cerca |
| `antes_vista_inicial.png` | `despues_vista_inicial.png` | Aparición: la torre se ve al frente |
| `antes_fabrica_en_marcha.png` | `despues_fabrica_en_marcha.png` | Recorrido automático (`--measure medium`), escenario medio: cinta, luz de trabajo, humo tenue. No son entradas reales |

Solo después:

- `despues_hito_este.png`, `despues_hito_sur.png`, `despues_hito_oeste.png`: grúa, pecio y cúpula tras 11 s de carrera desde la aparición.
- `despues_hacia_este_aparicion.png`, `despues_hacia_sur_aparicion.png`, `despues_hacia_oeste_aparicion.png`: lo mismo desde la aparición.
- `despues_panoramica_grua.png`: grúa de perfil con su pluma, desde el recorrido automático. Es posterior a girarla: la captura `despues_hito_este.png` también.
- `despues_menuview_paint.png`, `despues_menuview_train.png`, `despues_menuview_station.png`, `despues_menuview_astronaut.png`: nuevos encuadres fijos de pintura, tren con carga, estación y modelo externo con la herramienta.
- `despues_actividad_recicladora.png`: máquina sin producir en ese momento (energía 0/25): **no** demuestra vapor; sirve de contraste con la fábrica en marcha.
- `despues_ajuste_reducir_efectos.png`: «Reducir efectos» activado con clic real; `settings.json` pasó de `false` a `true`.

Datos: `autotest-zona.log` (escenario `zone`: hitos, niveles, respuesta a cada acción y ajuste), `medicion-antes.txt`, `medicion-despues.txt`.

## Observaciones al revisar

- Los hitos se distinguen por silueta desde la base. La grúa es la menos llamativa de frente a la zona de aparición: queda a contraluz y dependen de ella sus luces.
- El vapor es tenue a propósito y en una captura fija apenas se aprecia; en movimiento: **NO VERIFICADO**.
- La mancha blanca sobre la chapa de la ensambladora y el brillo del hielo junto al pecio son reflejo del sol: el primero ya está en la captura de antes.
- Las pistas se distinguen con poca fuerza, mejor al caminar hacia el hito (`despues_hito_oeste.png`).
