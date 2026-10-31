# Traspaso

- **Rama:** `main`, sin remoto: publicar requiere autorización del titular.
- **Alfas:** etiquetas `alpha-0.1.0` a `alpha-0.4.0`. Commit y versión en `builds/alpha-*/VERSION.txt`. Ver `git log` para lo posterior.
- **Procesos activos que dejar en marcha:** ninguno al cerrar; un ensayo largo escribe en `logs/net/*-soak60-040`.
- **Comandos:** en [CLAUDE.md](../CLAUDE.md); además `tools/net-matrix.ps1`, `tools/host-test.ps1`, y `auto-test.ps1 -Scenario rail|travel|mars|halloween`, `-Measure small|medium|stress`.
- **Bloqueos exactos:** ver [STATUS](STATUS.md). Todos requieren al titular.
- **Pruebas pendientes:** «Sin verificar» en [TEST_REPORTS](TEST_REPORTS.md).
- **Siguiente tarea concreta:** B-3, tutorial con ayudas contextuales; después B-8, desvíos y señales.
- **Cuidado:**
  - `auto-test`, `coop-test` y `host-test` abren ventanas y usan GPU: comprobar antes que no haya un `Pluton.exe` del titular abierto.
  - Tras pruebas gráficas, vigilar `LiveKernelEvent` y comparar horas (BUG-001).
  - `net-test` compila en `bin/Release`: durante un ensayo largo usar `-BinDir` con binarios copiados.
  - Editar `.ps1` con BOM UTF-8 o PowerShell 5.1 leerá mal los acentos.
  - Con la fábrica en marcha, comparar hashes solo mediante las firmas `Check`, nunca en un instante arbitrario.
