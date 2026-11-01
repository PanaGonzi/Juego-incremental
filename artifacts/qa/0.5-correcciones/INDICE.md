# Correcciones de la QA 0.5.0-qa2: evidencia por incidencia

- **Build:** `builds\alpha-dev` de la rama `agente/correcciones`, exportada con PlayDriver a 1600x900, salvo donde se indica. Sesión `correc`, perfil `test-correc`. Registro: `acciones.log`.
- **Dos exportaciones.** Capturas `00` a `02_objetivo_girado`, `01_*`, `03_objetivo_falta`, `04`, `05`, `08`, `09_contratos_1600x900`, `16_*` y `18_pausa_foco`: primeras exportaciones. El resto: la última, con el mismo código salvo tres retoques (posición del rumbo, etiquetas de la brújula y orden de cobro).
- **Partida preparada.** Salvo INC-02, INC-04, INC-08 e INC-18, se partió de un guardado creado con una herramienta temporal fuera del repositorio, en el perfil `test-correc`, para no repetir diez minutos de partida en cada prueba. Desde el menú, todo se hizo con entradas reales. El recorrido completo de los objetivos 1 a 10 **no se repitió** con esta build.

| Incidencia | Paso con entradas reales | Resultado observado | Capturas |
|---|---|---|---|
| INC-01 | Mochila vacía, 40 lingotes en un Contenedor, J y clic en «Comprar» | Comprada: 564 → 414 ¤ (−200 y +50 de recompensa), objetivo 9 → 10. Con recursos insuficientes, el texto indica cantidad y lo que hay | `09_contratos_1600x900` (faltan), `01_mejora_con_contenedor`, `01_comprada`, `03_objetivo_falta` |
| INC-02 | Partida nueva, girar con el ratón, caminar | Brújula con rumbo, marcadores y distancia; flecha en el borde y rombo en pantalla; lleva al montón | `01_inicio`, `02_objetivo_girado`, `02_ultrawide_hud` |
| INC-03 | Objetivo 4 con 7 de material | «Faltan: 5 Material recuperado (7/12)» y cómo conseguirlo, cambiando según el estado: cortar, llevar, esperar, recoger. El aviso desaparece al reunirlo | `03_objetivo_material`, `07_recicladora_inicio`, `07_tras_G_F_pulsar1`, `07_tras_G_F_ultimo`, `03_objetivo_listo` |
| INC-04 | Pulsar 1 con la herramienta en la mano | «[1] Sacar la multiherramienta» y aviso de guardado | `04_guardada` |
| INC-05 | Apuntar a máquinas con herramienta y tableta | Texto legible sobre fondo oscuro propio, encima de la herramienta | `05_texto_sobre_pared`, `17_desmontar` |
| INC-06 | Teclas 6, 7 y 8 mirando al suelo cercano | Se ven el suelo, la cuadrícula y lo que hay detrás. Las flechas se aclaran, pero siguen siendo grandes de muy cerca | `06_fantasma_extractor_cerca`, `06_fantasma_contenedor_cerca`, `06_fantasma_fundicion_medio` |
| INC-07 | G, 200 ms, F, cuatro veces ante la Recicladora; también con 1 | **No se reproduce:** sigue Multiútil y la vista no salta. Rueda desde Cinta: pasa a Vías, sin Desmontar. Al elegir Desmontar aparece un aviso | `07_tras_G_F_1`, `07_tras_G_F_pulsar1`, `07_tras_G_F_ultimo`, `07_rueda_salta_desmontar`, `07_aviso_desmontar` |
| INC-08 | Pausa → Ajustes → Gráficos: elegir 1280x720, luego «Completa sin bordes», luego «Ventana» | `PlayDriver info`: 1280x720; 1920x1082 (el motor añade 2 px a la pantalla completa sin bordes); 1280x720. El modo se conserva al reiniciar | `08_ajustes`, `08_ventana_1280x720`, `08_completa_sin_bordes` |
| INC-09 | J a 1600x900, 1920x1082 y 3440x1392 | «Cerrar» visible y sin solaparse con la barra ni el objetivo | `09_contratos_1600x900`, `09_contratos_1080p`, `09_contratos_ultrawide` |
| INC-10 | L, clic en el Contenedor, apuntar a la Fundición, clic | 8 celdas en línea recta al costado más próximo, sin rodear | `10_ruta_directa`, `10_cinta_construida` |
| INC-11 | Q abre Construcción | El botón dice «Cerrar [Q]» y Q cierra. Tab va a Inventario, como indica su pestaña | `11_catalogo_cerrar`, `13_catalogo_hover` |
| INC-12 | Pasar el cursor por Chatarra y transferirla | Al quedarse sin ese objeto, la ficha se vacía | `12_ficha_antes`, `12_ficha_despues` |
| INC-13 | Pasar el cursor por dos construcciones | Pestañas y columnas no se mueven | `13_catalogo_hover`, `13_catalogo_hover2` |
| INC-14 | Apuntar a una máquina | «[H] Cadena». **Rótulos flotantes bajo el panel: sin corregir, en `docs/BUGS.md`** | `07_recicladora_inicio` |
| INC-15 | Recicladora sin entrada y con producción | «Sin materiales · 11 por recoger (G)» | `15_por_recoger` |
| INC-16 | C en la Terminal, «Vender» en Lingotes | Valor por objeto y total. Vende solo lingotes: 414 → 464 ¤, el resto se queda | `16_terminal`, `16_vendido_lingote` |
| INC-17 | Desmontar un Contenedor, Tab, colocar una Fundición | Dice a dónde va y que se usa solo; el inventario lo muestra. La Fundición se paga con 7 de la mochila y 5 del depósito | `17_desmontar`, `17_desmontado`, `17_inventario_stock`, `17_material_usado` |
| INC-18 | Esc en partida; Esc en Ajustes | Foco en «Reanudar»; Esc vuelve a Pausa | `18_pausa_foco`, `18_esc_retrocede` |
| INC-19 | Vender 50 ¤ | «Ingresos/min: 10», sin picos. Tras los 5 min de ventana: **NO VERIFICADO** | `16_vendido_lingote` |

## Pruebas automáticas

- `dotnet test`: 247 correctas (214 de partida). Con las correcciones de INC-01, INC-04 e INC-07 retiradas a propósito fallaban 8 de `QaFixesTests`.
- `auto-test.ps1`, recorrido normal y escenarios `beltslong` y `build`: CORRECTO. En `build` se corrigió un paso anterior a este trabajo: enviaba `Link` sin ruta, algo que el servidor rechaza desde D-037.
- `coop-test` y `net-test`: **no ejecutados**, sin cambios de protocolo.

## NO VERIFICADO

- Comodidad y claridad con personas: esta es una sesión de agente, no datos de jugadores.
- Causa de INC-07 en 0.5.0-qa2. No se repitió sobre esa build. Ninguna de las teclas registradas lleva a Desmontar; la hipótesis de un ratón físico durante los 13 s sin órdenes no está confirmada.
- Flecha y rombo con escala de interfaz distinta del 100 %, y en cooperativo.
- «Completa exclusiva»: el botón existe y aplica el modo del motor, pero no se pulsó.
- Esc desde la lista de controles: la lógica es la misma que en Ajustes, pero solo se probó en Ajustes.
- Interfaz en inglés: textos comprobados por prueba, sin verla en pantalla.
