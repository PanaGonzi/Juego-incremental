# Cintas 0.5: evidencia visual

Capturas de **regresión por programa** (`--autotest --scenario belts|beltsv4`), no de partida con entradas reales: el jugador lo mueve AutoTest. Build `alpha-dev` de la rama `agente/cintas`, Forward+, 1600x900.

| Archivo | Qué muestra |
| --- | --- |
| `belt_01_previsualizacion.png` | Ruta prevista en verde con celdas y coste; puertos marcados en la máquina apuntada |
| `belt_04_cruce.png` | Puente plano sobre un tramo recto ajeno, con rampas y soportes; inspección en pantalla |
| `belt_05_carga_densa.png` | Cinta larga casi saturada con objetos reales visibles |
| `belt_06_atasco.png` | Destino lleno: cinta llena, marcas en rojo y causa en la inspección |
| `beltv4_02_elevada.png` | Guardado real de 0.4.1 migrado: cinta elevada apuntada |
| `autotest-belts.log`, `autotest-beltsv4.log` | Resultado de ambos recorridos: CORRECTO |



## Cinta larga y trazado (C-1)

Recorrido `--scenario beltslong`: teclas y clics enviados **dentro de Godot** (`Key`, `Primary`, `AimAt`), no por Windows. Resultado: CORRECTO (`autotest-beltslong.log`).

| Archivo | Qué muestra |
| --- | --- |
| `long_01_punto_fijado.png` | Punto intermedio fijado, con su poste en el mundo |
| `long_02_previsualizacion.png` | Ruta con el punto fijado hasta el destino lejano |
| `long_03_otra_ruta.png` | R: otra ruta válida (ruta 2), con longitud, soportes y coste |
| `long_04_antes_de_construir.png` y `long_05_despues_de_construir.png` | Mismo encuadre: previsualización tras retirar el punto y resultado. La prueba compara además celda a celda |
| `long_06_llegan_objetos.png` | Objetos a lo largo de la cinta (70 celdas), ya llegando al destino |
| `long_07_desnivel_simulado.png` | Solo el enlace de altura de `BeltView`, con una función de prueba: el suelo sigue plano, así que la cinta y los conectores flotan. **No prueba desniveles reales** |

Con el conductor y entradas reales de Windows: **NO VERIFICADO**. El juego no pudo tomar el foco porque un cuadro «Seguridad de Windows» (`PickerHost`) ocupaba el primer plano. No se tocó ese cuadro. Partida preparada para repetirlo: `--scenario beltstage` crea `belts-real` en el perfil `test-cintas`.
