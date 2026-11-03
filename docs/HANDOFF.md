# Traspaso

- **Rama de trabajo:** `evolucion-0.5`. Remoto `origin` en GitHub; `main` del remoto se igualó a `evolucion-0.5` el 03-11-2026 a petición del titular. Subir requiere su autorización. La estable empaquetada sigue en `builds/alpha-0.4.1`.
- **Alfas:** etiquetas `alpha-0.1.0` a `alpha-0.4.1`; las de prueba `0.5.0-qa*` no están etiquetadas. Commit y versión en `builds/alpha-*/VERSION.txt`. Ver `git log` para lo posterior.
- **Procesos activos:** ninguno.
- **Comandos:** en [CLAUDE.md](../CLAUDE.md); además `tools/net-matrix.ps1`, `tools/host-test.ps1`, `tools/linux-test.ps1 -LibDir <carpeta con libstdc++ y libgcc>` (servidor en WSL2; en `docker-desktop` el disco puede montarse en solo lectura, por eso usa `/dev/shm`), y `auto-test.ps1 -Scenario rail|signals|travel|mars|halloween|expedition`, `-Measure small|medium|stress`.
- **Bloqueos exactos:** ver [STATUS](STATUS.md). Todos requieren al titular.
- **Pruebas pendientes:** «Sin verificar» en [TEST_REPORTS](TEST_REPORTS.md).
- **Agentes:** ninguno activo. Las ramas `agente/*` ya están integradas en `evolucion-0.5`.
- **Siguiente tarea concreta:** empaquetar `0.5.0-qa9` desde `alpha-dev` con el `GameVersion` nuevo y repetir T-029 sobre el paquete. Después, lo que queda en [PENDIENTE_CONTROLES_REALES](PENDIENTE_CONTROLES_REALES.md) (la expedición y el aviso de material a la venta no se han tocado con entradas reales) y el resto de H-10. Lo demás del roadmap espera a personas, a otro PC o a Steam.
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
