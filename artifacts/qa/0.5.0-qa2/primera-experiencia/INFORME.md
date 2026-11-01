# QA de primera experiencia: PLUTON alfa 0.5.0-qa2

- **Versión:** PLUTON alfa 0.5.0-qa2, commit f93207f, fecha 2026-11-01T11:17:49 (`builds\alpha-0.5.0-qa2\VERSION.txt`). El menú muestra protocol 7 y data e8c96bd7.
- **Ruta de interacción:** PlayDriver con SendInput, sesión `qa2`, perfil `test-qa2`, ventana de 1600x900. Sin consola, sin trucos y sin leer código ni documentos.
- **Registro:** `acciones.log`, en esta carpeta.
- **Tipo de evidencia:** una sola sesión de uso real con un agente de QA. No son datos de jugadores. Las valoraciones de claridad son hipótesis.
- **Capturas:** hice unas 125, por encima de las 90 acordadas. Unas 25 se gastaron en reorientarme (ver INC-02). Aquí quedan 63. Los nombres indican el orden.

## Resultado

Partí de una partida nueva y llegué al **objetivo 10/23** («Construye una Ensambladora y produce 8 piezas mecánicas»). Por el camino:

- Cadena extractor → fundición → contenedor funcionando.
- Primera venta, primer contrato y primera mejora comprada.
- Guardar, volver al título y continuar, con el estado conservado.

No hubo bloqueantes ni cierres inesperados. No apareció `FOCO PERDIDO` ni capturas negras.

## Recorrido con tiempos

Los tiempos salen del log y de las horas de los comandos. **Incluyen la latencia del agente (varios segundos por paso) y desvíos de prueba** como ajustes, inventario o varias capturas seguidas. No equivalen al tiempo de una persona.

| Hora | T+ | Hito | Captura |
|---|---|---|---|
| 11:18:24 | 0:00 | Clic en «Nueva partida»; entra directo a la partida, sin carga visible | `02.png` |
| 11:18:46 | 0:22 | **Primera acción útil:** cortar chatarra. Ya tenía el objetivo y el texto de ayuda | `05.png` |
| 11:19:21 | 0:57 | Objetivo 1 hecho (3 montones) | `09.png` |
| 11:20:02 aprox. | 1:38 | Llego a la Recicladora tras unos 40 s buscando la baliza (INC-02) | `17.png` |
| 11:20:22 | 1:58 | Transferencia de 15 de 30 chatarras con clic derecho y deslizador; la cantidad se conserva | `20.png` |
| antes de 11:20:52 | < 2:28 | Objetivo 2 hecho (8 de material) | `21_panelmaq.png` |
| 11:23:01 | 4:37 | Costra de hielo retirada (3 bloques); yacimiento visible | `40.png` |
| 11:23:24 | 5:00 | Extractor colocado, objetivo 3 hecho | `44.png` |
| 11:23:30 a 11:26:46 | 5:06 a 8:22 | Desvío: faltan 5 de material para la Fundición, hay que volver a chatarra y Recicladora (INC-03). Incluye unos 45 s revisando ajustes | `73.png` |
| 11:27:35 | 9:11 | Fundición colocada, objetivo 4 hecho | `75.png` |
| 11:28:30 aprox. | 10:06 | Primera cinta (extractor → fundición) | `85.png` |
| 11:29:10 aprox. | 10:46 | Contenedor y segunda cinta, objetivo 5 hecho | `89.png` |
| 11:29:41 | 11:17 | Objetivo 6 hecho (10 lingotes) | `90.png` |
| 11:31:05 aprox. | **12:41** | **Primer ingreso por venta:** 163 ¤ (20 lingotes a 5, 48 hielos a 1, 15 minerales a 1), más 40 ¤ de recompensa | `100_ingreso.png` |
| 11:31:27 | 13:03 | Contrato «Primer envío de lingotes» entregado (+150 ¤) | `102_contratos2.png` |
| 11:31:45 a 11:34:10 | 13:21 a 15:46 | Atasco con la mejora y desorientación (INC-01, INC-02) | `103.png`, `108.png` |
| 11:33:09 | 14:45 | Guardar y volver al título; «Continuar · 01/11 11:33» | `110_titulo.png` |
| 11:33:15 aprox. | 14:51 | Continuar: créditos, objetivo y mochila conservados | `111_continuar.png` |
| 11:34:10 aprox. | 15:46 | Tecnología de ensamblaje comprada, objetivo 10/23 | `118_tras_mejora.png` |
| 11:34:42 | 16:18 | Contenedor desmontado (+27 ¤) | `123_desmontado.png` |

## Comprobaciones pedidas

| Tema | Resultado | Evidencia |
|---|---|---|
| Orientarse y primer objetivo | Bien: texto claro y arriba. La baliza falla a distancia (INC-02) | `02.png` |
| Reconocer y recolectar recursos | Bien: chatarra e hielo son reconocibles, con rótulo y progreso | `05.png`, `36.png` |
| Brazos, herramienta y respuesta | Bien: la herramienta reacciona al corte y la tableta aparece al construir. Tapa textos (INC-05) | `05.png`, `42.png` |
| Inventario | Bien, con detalles menores (INC-11, INC-12) | `18_panel.png` |
| Barra rápida | Las teclas 1 a 9 y L funcionan. **Fijar objetos a la barra: NO VERIFICADO** | `41_fantasma.png`, `76_cinta.png` |
| Transferir y dividir | Clic derecho con deslizador y arrastre funcionan. Sin pérdidas: 30 → 15 + 15, y 8 lingotes de ida y vuelta. **Mayús+clic y Ctrl+clic: NO VERIFICADO** (el conductor no tiene clic con modificador) | `19.png`, `20.png`, `92_transfer.png` |
| Catálogo | Bien: costes, requisitos y «Tienes todo lo necesario» | `27_catalogo.png`, `28.png` |
| Seleccionar, rotar y colocar máquinas | Bien: el fantasma cambia de verde a rojo con motivo y se ajusta al yacimiento. Falla muy de cerca (INC-06) | `42.png`, `73.png`, `74.png` |
| Tender cinta, conectores, otra ruta, punto intermedio, quitar y cancelar | Funciona: R muestra «ruta 2», M cambia de conector, el clic fija un punto con marcador, Z cancela. Pulsé Backspace pero no revisé esa captura, así que **su efecto queda NO VERIFICADO**. **[N] Ajuste: NO VERIFICADO** | `81.png`, `82.png`, `83.png`, `119_punto.png`, `121_cancel.png` |
| Desmontar | Bien: lista lo que se recupera y las cintas afectadas. Rechaza con motivo las máquinas de la base | `122_desmontar.png`, `INC_F_desmontar.png` |
| Por qué produce, espera o está llena | Bien: el panel muestra estado, ciclo, entrada, salida y producción real o teórica. «Salida llena» y «Cuello de botella» salen con H | `21_panelmaq.png`, `47.png`, `71.png` |
| **Falta de energía** | **NO VERIFICADO:** nunca superé la capacidad (máximo 14 de 30) | — |
| Primer ingreso y mejorar | Funciona, con un atasco en los recursos de la mejora (INC-01) | `100_ingreso.png`, `117_mejora.png` |
| Ajustes | Hay calidad, interfaz, idioma y controles, pero no resolución (INC-08). **Aplicar escala, idioma, reasignación y la pestaña Sonido: NO VERIFICADO** (no los probé) | `57_ajustes.png`, `58_interfaz.png`, `60_mapa_controles.png` |
| Recuperar el control al cerrar menús | Bien tras Esc, Tab y J: la vista vuelve a responder (comprobado girando tras cerrar) | `118_tras_mejora.png` |
| Guardar, salir y continuar | Bien en el mismo proceso. **Tras cerrar y reabrir el juego: NO VERIFICADO**. Tras continuar seguían la Fundición y el Contenedor, con sus lingotes | `110_titulo.png`, `111_continuar.png`, `117_mejora.png`, `122_desmontar.png` |

## Incidencias

### Alta

**INC-01. Una mejora dice «faltan recursos» aunque hay de sobra, y no dice cuáles**
- **Pasos:** tener 564 ¤ y lingotes en el Contenedor, con la mochila vacía. Abrir J y mirar «Tecnología de ensamblaje» (200 ¤ y 10 lingotes).
- **Esperado:** poder comprar, o que se indique qué falta y dónde tiene que estar.
- **Observado:** el contrato de al lado cuenta 15/40 lingotes, pero la mejora sigue en «faltan recursos». Solo se activa con los lingotes en la mochila (41 lingotes → Comprar). El contrato cuenta lo almacenado y la mejora no, y nada lo explica. Es además el objetivo 9, justo después del primer ingreso.
- **Captura:** `103.png`, `117_mejora.png`

**INC-02. No hay brújula, mapa ni indicador de dirección, y la baliza se pierde con facilidad**
- **Pasos:** al completar un objetivo, buscar el siguiente (Recicladora a 27 m). O bien, tras vender, volver a las máquinas propias.
- **Esperado:** alguna indicación de hacia dónde girar cuando el destino está fuera de pantalla.
- **Observado:** «27 m» no dice la dirección. La baliza es un haz vertical que puede quedar detrás de edificios o bajo el panel de objetivos. No la vi en 5 capturas girando por pasos y tardé unos 40 s. Cuando el objetivo no tiene ubicación (como el 9), no hay baliza: me perdí volviendo a mi fábrica unas 8 capturas y llegué a un campo de chatarra equivocado.
- **Captura:** `71.png`, `108.png`

**INC-03. El objetivo 4 no avisa de que falta material ni de cómo conseguirlo**
- **Pasos:** seguir los objetivos 1 a 3 y llegar al 4 («Construye una Fundición cerca del extractor») con 7 de material.
- **Esperado:** que el objetivo o el recorrido tengan en cuenta los 12 de material, o que se indique volver a la Recicladora.
- **Observado:** el aviso solo aparece en el fantasma y en el catálogo. El objetivo sigue marcando el extractor mientras hay que ir en dirección contraria. El desvío duró unos 3 minutos, de 11:23:30 a 11:26:46, con parte dedicada a revisar ajustes.
- **Captura:** `44.png`, `73.png`

**INC-04. Marcador sin resolver visible en pantalla**
- **Pasos:** cancelar la colocación con clic derecho (vuelve a la multiherramienta) y pulsar 1.
- **Esperado:** «[1] Sacar la multiherramienta».
- **Observado:** aparece literalmente `[{hotbar_1}] Sacar la multiherramienta`. Además, pulsar 1 con la herramienta en la mano la guarda, sin ningún otro aviso.
- **Captura:** `INC_placeholder_hotbar.png`

### Media

**INC-05. La herramienta y el fondo tapan los textos de interacción**
- **Pasos:** apuntar a una máquina o recurso con la multiherramienta o la tableta en la mano.
- **Esperado:** rótulos siempre legibles.
- **Observado:** la sierra tapa parte de «[E] Recoger» o «[C] Panel». El texto naranja se pierde sobre máquinas y chatarra del mismo color, y en modo cinta sobre la cuadrícula. Afecta justo a la línea que enseña los controles.
- **Captura:** `17.png`, `09.png`, `85.png`, `88.png`

**INC-06. El fantasma de construcción tapa la pantalla si estás cerca**
- **Pasos:** elegir el Extractor (6) estando al lado del yacimiento.
- **Esperado:** poder ver dónde colocas, por ejemplo con mayor transparencia.
- **Observado:** pantalla casi entera verde, sin ver yacimiento, cuadrícula ni flechas. Hay que retroceder. Con el Contenedor, las flechas llenan también la vista de cerca.
- **Captura:** `41_fantasma.png`, `86.png`

**INC-07. El modo Desmontar se activó solo y la cámara saltó (visto una vez, sin reproducir)**
- **Pasos:** ante la Recicladora, pulsar G y 200 ms después F; luego pulsar 1.
- **Esperado:** recoger y depositar, sin cambiar de herramienta ni mover la vista.
- **Observado:** tras F quedó activo «Desmontar» en lugar de la multiherramienta. Con 1, la vista apareció mirando al cielo y girada. F no volvió a provocarlo apuntando al cielo ni en la Terminal. **Causa NO VERIFICADA:** el salto de cámara podría venir de la herramienta de pruebas. Conviene investigarlo porque un cambio accidental a Desmontar es peligroso.
- **Captura:** `INC_F_desmontar.png`, `65.png`, `66.png`

**INC-08. No hay opción de resolución**
- **Pasos:** Pausa → Ajustes → Gráficos.
- **Esperado:** un selector de resolución.
- **Observado:** solo calidad, sombras, resplandor, pantalla completa y campo de visión.
- **Captura:** `57_ajustes.png`

**INC-09. El botón «Cerrar» de Contratos queda tapado por la barra rápida**
- **Pasos:** pulsar J con la ventana a 1600x900 y escala del 100 %.
- **Esperado:** botón visible y pulsable.
- **Observado:** la barra se pinta encima de «Cerrar» y del final de la lista. El panel también tapa el texto del objetivo, que es justo el que dice qué comprar. Se puede salir con J o Esc.
- **Captura:** `101_contratos.png`, `102_contratos2.png`

**INC-10. El objetivo 5 explica mal cómo conectar**
- **Pasos:** leer el objetivo 5 y mirar los conectores de la Fundición.
- **Esperado:** instrucción coherente con lo que se ve.
- **Observado:** dice «Sale por el frente de la máquina y entra por detrás», pero la Fundición tiene entradas en tres lados y la herramienta elige una por defecto, rodeando la máquina. Las flechas sí se entienden. El panel crece a tres líneas y tapa más cielo, donde está la baliza.
- **Captura:** `74.png`, `75.png`, `81.png`

### Baja

**INC-11. «Cerrar [Tab]» no cierra desde Construcción**
- **Pasos:** abrir Construcción con Q y pulsar Tab.
- **Esperado:** cerrar, como dice el botón.
- **Observado:** cambia a la pestaña Inventario. Hace falta otro Tab.
- **Captura:** `29.png`

**INC-12. Datos antiguos en la ficha del objeto**
- **Pasos:** transferir chatarra y seguir viendo la ficha, o volver a abrir el inventario.
- **Esperado:** cantidad actual o ficha vacía.
- **Observado:** sigue «Chatarra · 30» con 15 en la mochila, e incluso con 0 tras otras acciones.
- **Captura:** `20.png`, `29.png`

**INC-13. El catálogo se desplaza al pasar el cursor**
- **Pasos:** en Construcción, pasar el cursor por una entrada.
- **Esperado:** pestañas y columnas estables.
- **Observado:** el panel lateral se ensancha y las pestañas se comprimen y mueven.
- **Captura:** `27_catalogo.png`, `28.png`

**INC-14. La «H» del estado no se explica**
- **Pasos:** apuntar a una máquina.
- **Esperado:** indicar para qué sirve H.
- **Observado:** el rótulo termina en una «H» suelta que parece un texto cortado. La función (cuello de botella) es útil pero solo la descubrí probando. Sus rótulos flotantes también quedan bajo el panel de objetivos.
- **Captura:** `17.png`, `47.png`, `75.png`

**INC-15. «Sin materiales» en una máquina con producción por recoger**
- **Pasos:** dejar que la Recicladora gaste su entrada y apuntarle.
- **Esperado:** indicar que hay salida para recoger.
- **Observado:** solo «Sin materiales», con 8 de material listos. Lo descubrí al pulsar G (+8).
- **Captura:** no se conservó (era la 69; el registro de la acción está en `acciones.log`).

**INC-16. Vender todo no avisa del valor ni de lo que se vende**
- **Pasos:** F en la Terminal (deposita toda la mochila) y «Vender todo».
- **Esperado:** ver el precio o el total antes de confirmar.
- **Observado:** vendió también hielo y mineral, sin mostrar el valor. Los precios solo están en la ficha del inventario. La reserva por tipo existe pero no se explica.
- **Captura:** `99_terminal.png`, `100_ingreso.png`

**INC-17. No se ve el «Depósito central» de lo desmontado**
- **Pasos:** desmontar el Contenedor, que anuncia que 6 de material y 8 lingotes van al Depósito central.
- **Esperado:** poder consultar ese depósito.
- **Observado:** suben los créditos, pero la mochila no cambia y el panel de inventario no muestra ningún depósito. **Si el material queda disponible para construir o comprar: NO VERIFICADO.**
- **Captura:** `122_desmontar.png`, `124_inventario.png`

**INC-18. Foco y navegación de los menús**
- **Pasos:** pulsar Esc en partida; o Esc dentro de Pausa → Ajustes → Controles.
- **Esperado:** foco en «Reanudar», y Esc retrocede un nivel.
- **Observado:** el foco queda en «Guardar», así que Intro guardaría en lugar de reanudar. Desde la lista de controles, Esc vuelve directo a la partida.
- **Captura:** `56_pausa.png`; `60_mapa_controles.png` muestra el punto de partida. La captura del regreso a la partida no se conservó.

**INC-19. «Ingresos/min» se dispara y cae enseguida**
- **Pasos:** vender una vez y entregar un contrato.
- **Esperado:** una cifra que ayude a valorar la fábrica.
- **Observado:** sube a 316 con ventas puntuales y recompensas, y baja a 0 poco después. No refleja la fábrica, que aún no vende de forma continua.
- **Captura:** `102_contratos2.png`, `111_continuar.png`

## Observaciones positivas (hipótesis, no datos de jugadores)

- Primeros pasos muy guiados: objetivo inmediato, texto de ayuda sobre el objeto y recompensa continua.
- El panel de máquina es muy claro: receta, ciclo, real frente a teórica y consumo.
- El tendido de cinta da información útil antes de construir: celdas, metros, soportes, coste y ruta.
- El aviso de desmontaje es muy completo.

## Notas de método

- Unos clics míos fallaron por usar mal la escala de la captura (cerrar el panel en `24.png`, recoger en `30`). No son fallos del juego ni cuentan como incidencias.
- La pausa parece detener la producción en solitario: la Recicladora solo dio +3 durante unos 43 s, casi todos en menús. Es una **inferencia, sin medición directa**.
