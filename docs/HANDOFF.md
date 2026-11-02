# Traspaso

- **Rama de trabajo:** `evolucion-0.5`. La estable sigue en `main` y `builds/alpha-0.4.1`. Sin remoto: publicar requiere autorización del titular.
- **Alfas:** etiquetas `alpha-0.1.0` a `alpha-0.4.1`; las de prueba `0.5.0-qa*` no están etiquetadas. Commit y versión en `builds/alpha-*/VERSION.txt`. Ver `git log` para lo posterior.
- **Procesos activos:** ninguno.
- **Comandos:** en [CLAUDE.md](../CLAUDE.md); además `tools/net-matrix.ps1`, `tools/host-test.ps1`, y `auto-test.ps1 -Scenario rail|signals|travel|mars|halloween`, `-Measure small|medium|stress`.
- **Bloqueos exactos:** ver [STATUS](STATUS.md). Todos requieren al titular.
- **Pruebas pendientes:** «Sin verificar» en [TEST_REPORTS](TEST_REPORTS.md).
- **Agentes:** ninguno activo. Las ramas `agente/*` ya están integradas en `evolucion-0.5`.
- **Siguiente tarea concreta:** los hallazgos abiertos de [REVISION_E8](REVISION_E8.md), empezando por H-3 (vías, ya en B-4) y H-6. H-4 y H-9 están en la rama, sin paquete: `0.5.0-qa7` no los incluye. Con el equipo libre, además, [PENDIENTE_CONTROLES_REALES](PENDIENTE_CONTROLES_REALES.md).
- **Pruebas sin molestar al titular:** `auto-test`, `coop-test` y `host-test` abren sin sonido y en un monitor secundario (`tools/lib-ventana.ps1`). En `auto-test`, `-NoCapture` evita capturar el ratón. Mientras use el equipo, no ejecutar `PlayDriver`, ni `fp` o el bucle sin `-NoCapture`: capturan el ratón. El titular lo pidió el 01-11-2026.
- **Menús sin molestar:** `tools/desktop-start.ps1` abre `alpha-dev` con accesibilidad, sin foco y sin capturar el ratón; después, `agent-desktop` con `--window-id`. No volver a activar la ventana quitando `WS_EX_NOACTIVATE`: un clic semántico le da el primer plano. En botones de opción, `toggle`, no `click`. Alcance en T-025.
- **Pruebas visuales:** `tools/PlayDriver`. Una sola sesión a la vez; siempre perfil `test*`.
- **Cuidado:**
  - `auto-test`, `coop-test` y `host-test` abren ventanas y usan GPU: comprobar antes que no haya un `Pluton.exe` del titular abierto.
  - Tras pruebas gráficas, vigilar `LiveKernelEvent` y comparar horas (BUG-001).
  - `net-test` compila en `bin/Release`: durante un ensayo largo usar `-BinDir` con binarios copiados.
  - NuGet `Value cannot be null (path1)`: el proceso lanzador no heredó `ProgramData`/`ProgramFiles(x86)`. `export.ps1` ya las rellena; para órdenes `dotnet` sueltas, fijarlas antes (2026-11-01).
  - Editar `.ps1` con BOM UTF-8 o PowerShell 5.1 leerá mal los acentos.
  - Con la fábrica en marcha, comparar hashes solo mediante las firmas `Check`, nunca en un instante arbitrario.
