# Errores

| ID | Gravedad | Versión | Estado | Descripción | Reproducción |
| --- | --- | --- | --- | --- | --- |
| BUG-001 | Alta | 0.1.0 pre | Abierto, causa sin determinar, no repetido | Pérdida de dispositivo Vulkan: TDR del driver AMD (`LKD_0x141`, `amdkmdag.sys`) y cierre del juego. Se recupera del último autosave (máximo 120 s). | 1 vez en un recorrido a 1080p, t≈50 s. Este PC registró **50 eventos 141/117 en 30 días**, también antes del proyecto. No repetido en las más de 20 ejecuciones gráficas posteriores, pero no se puede descartar. |
| BUG-004 | Crítica | 0.1.0 | **Corregido en 0.2.0** | Caía el hilo del servidor si dos jugadores terminaban el mismo escombro en una ventana de replicación; en anfitrión, congelación sin aviso. | Prueba `Two_players_finishing_the_same_debris...`, rojo → verde |
| BUG-002 | Baja | 0.1.0 | Abierto | El espejo del cliente solo actualiza `Tick` al recibir cambios. No debe usarse como reloj. | Mundo sin actividad |
| BUG-005 | Media | 0.2.0 | Causa de la lectura acotada; **pico real abierto** | RTT de hasta ~1 s con pérdida. Sin pérdida no ocurre: 203 ms con 150 ms configurados. LiteNetLib cuenta un ping perdido como muestra igual a su intervalo (leído en su rama principal, no en la 2.1.5). Con ping cada 500 ms: lectura 1006 → 514 ms. La respuesta real a órdenes, en cambio, apenas mejora: máximo 1560 → 1354 ms con 150 ms y 3 % (una ejecución por caso, diferencia dentro de lo esperable por azar). Limitación nueva: un RTT real por encima de 500 ms no se mide bien. Causa del pico real: sin medir; hipótesis, reenvíos y orden del canal fiable. | `net-test -Latency 150 -Jitter 20 -Loss 3`; ver T-021 |
| BUG-006 | Baja | 0.3.0 | Abierto | Dos trenes parados en el mismo andén se solapan visualmente. Sin efecto económico. | Dos trenes con el mismo destino |
| BUG-007 | Baja | 0.3.0 | Corregido en 0.4.1 | Cambiar de idioma en partida no retraducía la barra de herramientas. | Ajustes en pausa |
| INC-01 | Alta | 0.5.0-qa2 | Corregido en `agente/correcciones` | Mejora y contrato usaban reglas de procedencia distintas: «faltan recursos» con lingotes en un contenedor y sin decir cuáles. | Prueba `QaFixesTests`, rojo → verde; captura `01_mejora_con_contenedor` |
| INC-02 | Alta | 0.5.0-qa2 | Corregido en `agente/correcciones` | Sin brújula ni dirección: la baliza se perdía. | `01_inicio`, `02_objetivo_girado`, `02_ultrawide_hud` |
| INC-03 | Alta | 0.5.0-qa2 | Corregido en `agente/correcciones` | El objetivo no decía que faltaba material ni cómo conseguirlo. | Prueba `QaFixesTests`; `03_objetivo_material` |
| INC-04 | Alta | 0.5.0-qa2 | Corregido en `agente/correcciones` | `{hotbar_1}` sin resolver: el marcador no admitía dígitos. | Prueba que recorre `content/loc/*.json`, rojo → verde; `04_guardada` |
| INC-05 | Media | 0.5.0-qa2 | Corregido en `agente/correcciones` | Rótulos ilegibles sobre el fondo o junto a la herramienta. | `05_texto_sobre_pared`, `17_desmontar` |
| INC-06 | Media | 0.5.0-qa2 | Corregido en `agente/correcciones`; comodidad real **NO VERIFICADA** con personas | El fantasma llenaba la pantalla de cerca. | `06_fantasma_*` |
| INC-07 | Media | 0.5.0-qa2 | **No reproducido, causa NO VERIFICADA**; lógica protegida | Desmontar activo y salto de cámara tras G y F. Ninguna de esas teclas lleva al modo: solo X, el 3, un clic en la barra o la rueda. Hipótesis sin confirmar: mouse físico durante los 13 s sin órdenes del conductor. | `07_tras_G_F_*`, `07_rueda_salta_desmontar`; prueba de la rueda |
| INC-08 | Media | 0.5.0-qa2 | Corregido en `agente/correcciones` | Sin resolución ni modo de ventana. | `08_*`; `PlayDriver info`: 1280x720 y 1920x1082 |
| INC-09 | Media | 0.5.0-qa2 | Corregido en `agente/correcciones` | La barra rápida tapaba «Cerrar» y el panel, el objetivo. | `09_contratos_1600x900`, `09_contratos_1080p`, `09_contratos_ultrawide` |
| INC-10 | Media | 0.5.0-qa2 | Corregido en `agente/correcciones` | Texto falso sobre los conectores; la ruta rodeaba la máquina. | Prueba de entrada directa; `10_ruta_directa` |
| INC-11 | Baja | 0.5.0-qa2 | Corregido en `agente/correcciones` | «Cerrar [Tab]» no cerraba desde Construcción: ahora dice «Cerrar [Q]». | `11_catalogo_cerrar` |
| INC-12 | Baja | 0.5.0-qa2 | Corregido en `agente/correcciones` | Cantidad antigua en la ficha del objeto. | `12_ficha_antes`, `12_ficha_despues` |
| INC-13 | Baja | 0.5.0-qa2 | Corregido en `agente/correcciones` | El catálogo se desplazaba al pasar el cursor. | `13_catalogo_hover`, `13_catalogo_hover2` |
| INC-14 | Baja | 0.5.0-qa2 | Corregido en parte | La «H» suelta dice «[H] Cadena». **Abierto:** los rótulos flotantes de la cadena pueden quedar bajo el panel de objetivos. | `07_recicladora_inicio` |
| INC-15 | Baja | 0.5.0-qa2 | Corregido en `agente/correcciones` | «Sin materiales» sin avisar de lo que había por recoger. | `15_por_recoger` |
| INC-16 | Baja | 0.5.0-qa2 | Corregido en `agente/correcciones` | «Vender todo» sin valor ni elección. **Sin cambiar:** F en la terminal deposita toda la mochila. | `16_terminal`, `16_vendido_lingote` |
| INC-17 | Baja | 0.5.0-qa2 | Corregido en `agente/correcciones` | No se veía a dónde iba lo desmontado ni si servía. | `17_desmontar`, `17_inventario_stock`, `17_material_usado` |
| INC-18 | Baja | 0.5.0-qa2 | Corregido en `agente/correcciones` | Foco en «Guardar» y Esc saliendo de golpe. | `18_pausa_foco`, `18_esc_retrocede` |
| INC-19 | Baja | 0.5.0-qa2 | Corregido en `agente/correcciones` | «Ingresos/min» se disparaba con ventas sueltas y contratos: media de 5 min, solo ventas. | `16_vendido_lingote` (50 ¤ → 10/min) |
| INC-20 | Baja | 0.5.0-qa4 | Abierto | Las piezas grandes muestran tantas flechas de puertos, y tan grandes, que tapan el suelo donde se va a colocar. | Fantasma de la Plataforma de carga: `artifacts/qa/0.5-marte/despues_fantasma.png` |

## Limitaciones de las alfas

Sin tutorial aparte de los objetivos. Sin desvíos ni señales ferroviarias. Sin LOD. Invitar por Steam sin integrar en el menú. Prestigio sin implementar (opcional y posterior).
