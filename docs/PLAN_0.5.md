# Plan de la alfa 0.5: experiencia de fábrica

Encargo: [PROMPT_CLAUDE_CONTINUAR_QA_CINTAS_GRAFICOS.md](../PROMPT_CLAUDE_CONTINUAR_QA_CINTAS_GRAFICOS.md). Rama de integración: `evolucion-0.5`. Estable: `builds/alpha-0.4.1`, etiqueta `alpha-0.4.1`.

## Reparto de archivos

| Bloque | Dueño | Archivos |
| --- | --- | --- |
| Cintas | agente Cintas | `src/Simulation`, `src/Networking`, `src/Persistence`, `tests`, `tools/BotClient`, `content` (salvo textos ajenos), `src/Game/Scripts/BeltView.cs`, en `GameView.cs` solo lo relativo a enlaces |
| Primera persona e inventario | agente Primera persona | nuevos `FirstPersonRig.cs`, `InventoryUi.cs`, `Hotbar.cs`; en `GameView.cs` entrada, herramientas y `Controls`; `World.cs` solo en `Insert`/`Extract`; estado de acción del jugador en protocolo |
| Arte y menús | agente Arte | `Models.cs`, `ItemVisuals.cs`, nuevos `UiTheme.cs`, `Materials.cs`; en `GameView.cs` `BuildEnvironment`, `BuildTerrain`, `BuildScenery`; `MainMenu.cs`; `Ui.cs` |

## Decisiones comunes

- **Cinta = `Link` con geometría.** Sigue uniendo dos máquinas y añade `Cells` (ruta en la cuadrícula) y `Slots` (carga real). Los comandos siguen siendo `Link` y `Unlink`.
- **Espaciado:** 4 huecos por celda de 2 m. Un objeto avanza un hueco cada 3 ticks a velocidad base: 3,33 objetos/s, igual que el antiguo enlace. La mejora `link_rate` acelera el avance.
- **Ruta:** `World` la calcula de forma determinista. La misma función sirve para validar en el servidor y para previsualizar en el cliente. Rodea máquinas, escombros y vías; dos cintas pueden cruzarse en perpendicular con un puente.
- **Puertos:** salidas en la cara frontal según la rotación, entradas en la trasera. Almacenes, terminal y estaciones aceptan por los cuatro lados.
- **Enlaces antiguos sin ruta posible:** se migran a cinta **elevada** recta sobre soportes, que no ocupa celdas. Se pueden desmontar y volver a tender.
- **Retirar una cinta:** su coste y su carga vuelven al depósito del planeta, igual que al retirar una máquina. Las cintas no consumen energía: si una máquina se para, la cinta se atasca.
- **Red:** la simulación es del servidor. Los huecos se replican a 1 Hz solo en cintas que cambian. La vista interpola con su propia copia, nunca en el `World` espejo, para no alterar las firmas `Check`.
- **Dibujo:** `MultiMesh` por tipo de objeto. Sin cuerpo físico ni RPC por objeto.
- **Controles por defecto (reasignables, con migración de ajustes):** `Tab` inventario, `E` interactuar, `Q` catálogo de construcción, números barra rápida, `R` rotar, `X` desmontar.
- **Reglas del proyecto:** ver [CLAUDE.md](../CLAUDE.md). Formato binario y protocolo suben de versión con migración y prueba.

## Puntos de unión entre agentes

- `ItemVisuals.ColorOf(item)`, `MeshOf(item)`, `IconOf(item)`: los usan cintas e inventario; Arte mejora su interior sin cambiar la firma.
- `Link.Cells`, `Link.Slots`, `Link.Elevated`: los escribe Cintas; solo los lee la vista.

## Pruebas visuales

`tools/PlayDriver` juega con entradas normales de Windows. Una sola sesión visual a la vez en el escritorio; ciérrala con `stop` al terminar. Evidencia en `artifacts/qa/<version>/`, con índice.
