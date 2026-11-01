# Traspaso

- **Rama:** `main`, sin remoto: publicar requiere autorización del titular.
- **Alfas:** etiquetas `alpha-0.1.0` a `alpha-0.4.1`. Commit y versión en `builds/alpha-*/VERSION.txt`. Ver `git log` para lo posterior.
- **Procesos activos:** ninguno.
- **Comandos:** en [CLAUDE.md](../CLAUDE.md); además `tools/net-matrix.ps1`, `tools/host-test.ps1`, y `auto-test.ps1 -Scenario rail|travel|mars|halloween`, `-Measure small|medium|stress`.
- **Bloqueos exactos:** ver [STATUS](STATUS.md). Todos requieren al titular.
- **Pruebas pendientes:** «Sin verificar» en [TEST_REPORTS](TEST_REPORTS.md).
- **Rama de trabajo:** `evolucion-0.5`, con agentes en `agente/cintas`, `agente/primera-persona` y `agente/arte`; el coordinador integra. La estable sigue en `main` y `builds/alpha-0.4.1`.
- **Siguiente tarea concreta:** integrar las ramas de agentes y someter la build a QA: tareas E-1 a E-8 del [ROADMAP](ROADMAP.md), plan en [PLAN_0.5](PLAN_0.5.md).
- **Pruebas visuales:** `tools/PlayDriver`. Una sola sesión a la vez; siempre perfil `test*`.
- **Cuidado:**
  - `auto-test`, `coop-test` y `host-test` abren ventanas y usan GPU: comprobar antes que no haya un `Pluton.exe` del titular abierto.
  - Tras pruebas gráficas, vigilar `LiveKernelEvent` y comparar horas (BUG-001).
  - `net-test` compila en `bin/Release`: durante un ensayo largo usar `-BinDir` con binarios copiados.
  - Editar `.ps1` con BOM UTF-8 o PowerShell 5.1 leerá mal los acentos.
  - Con la fábrica en marcha, comparar hashes solo mediante las firmas `Check`, nunca en un instante arbitrario.
