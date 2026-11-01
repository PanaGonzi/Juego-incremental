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

Sin probar con `tools/PlayDriver` y entradas reales de Windows: **NO VERIFICADO**.
