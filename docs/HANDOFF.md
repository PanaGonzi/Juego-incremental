# Traspaso

- **Rama:** `main`, sin remoto (publicar requiere autorización del titular).
- **Commit de la alfa:** 5cb2311, etiqueta `alpha-0.1.0`. Ver `git log` para lo posterior.
- **Procesos activos que dejar en marcha:** ninguno.
- **Comandos:** en [CLAUDE.md](../CLAUDE.md).
- **Bloqueo exacto:** Steam entre cuentas (M0-5). Necesita segunda cuenta o probador y, para el producto, AppID propio.
- **Pruebas pendientes:** ver «Sin verificar» en [TEST_REPORTS.md](TEST_REPORTS.md).
- **Siguiente tarea concreta:** A2-1, grafo de vías en `src/Simulation` con pruebas antes de tocar Godot. Sube `WorldCodec.FormatVersion` a 2 con migración de los guardados de la 0.1.0.
- **Cuidado:** `auto-test` y `coop-test` abren ventanas y usan GPU; no lanzarlas si el titular está jugando. Vigilar `LiveKernelEvent` tras cada una (BUG-001).
