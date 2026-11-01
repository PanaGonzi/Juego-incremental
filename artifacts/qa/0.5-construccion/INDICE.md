# QA 0.5 — construcción

Capturas del escenario `--scenario build` de `AutoTest.cs`, exportación `alpha-dev`, 1600×900, perfil `test-constr`.

**Procedencia:** entrada inyectada (`Input.ParseInputEvent`, `AimAt`, llamadas a `Primary`). Es una regresión, no una partida con ratón y teclado reales: **entrada real NO VERIFICADA**, porque `PlayDriver` estaba bloqueado por una ventana «Seguridad de Windows» que no se debe tocar.

| Captura | Qué muestra |
| --- | --- |
| `build_02_catalogo_bloqueada.png` | Filtro de categoría Extracción, máquina bloqueada con vista, receta, requisito y lo que falta |
| `build_04b_puertos_de_costado.png` | Fantasma válido con su coste y flechas de puertos: entrada desde +Z, salida hacia −Z (giro 0, cámara mirando a −X) |
| `build_05_fantasma_invalido.png` | Fantasma rojo sobre una máquina con el motivo «El espacio está ocupado» |
| `build_06_faltan_materiales.png` | Fantasma ámbar sin salir del modo: «Faltan: 10 Lingotes» |
| `build_07_desmontar.png` | Tres fundiciones pegadas: solo se tiñe la apuntada, con lo que se recupera y a dónde va |

`autotest-build.log`: resultado de cada comprobación del escenario, incluida la devolución medida frente a la mostrada.
