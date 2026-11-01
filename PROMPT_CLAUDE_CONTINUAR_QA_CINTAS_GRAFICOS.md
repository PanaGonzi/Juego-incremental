# Continuación de PLUTON: experiencia de fábrica, primera persona y pruebas por agentes

Continúa el desarrollo del proyecto existente. Este encargo actualiza las prioridades visuales y de pruebas del encargo principal. Mantén el cooperativo online para 12 jugadores, Plutón y Marte, los viajes continuos, la progresión, las alfas y el rendimiento como requisitos del producto.

Tu último informe indica alfa 0.4.1, commit `babc91c`, protocolo 4 y guardado 4. Son referencias del informe, no una orden de volver atrás: comprueba el estado actual del repositorio y conserva cualquier avance posterior. Lee `CLAUDE.md`, `docs/HANDOFF.md`, `docs/STATUS.md`, `docs/ROADMAP.md`, los errores y los informes de pruebas. Si un archivo o dato no coincide, registra la situación real.

No quiero que el desarrollo se detenga esperando que yo pruebe cada alfa, decida el diseño o te diga si puedes continuar. Te delego las decisiones de implementación, diseño y acabado dentro de estas prioridades. Puedes entregarme alfas para que las pruebe cuando quiera; mi respuesta no es una condición para continuar con trabajo útil.

## 1. Qué debes cambiar ahora

Mis prioridades explícitas son:

1. Agentes que prueben la experiencia jugable de la versión exportada, observen las pantallas, encuentren problemas, pidan correcciones y vuelvan a comprobarlas.
2. Cintas transportadoras con los objetos recolectados y fabricados visibles encima. Quiero ver claramente por dónde va cada recurso y si la instalación funciona.
3. Primera persona con brazos, manos y herramientas físicas visibles, con animación y respuesta convincentes.
4. Inventario accesible, barra rápida y menús con más calidad visual, información útil y acabado profesional.
5. Gráficos bastante más bonitos y realistas: ciencia ficción industrial semirrealista, con materiales y modelos creíbles. No hace falta hiperrealismo.

La dirección visual del primer encargo era provisional. Este cambio reemplaza el objetivo de un acabado geométrico muy simple. Aprovecha el código y los sistemas existentes; conserva el presupuesto de rendimiento y la coherencia de las partidas.

### Qué me gusta de Satisfactory

Satisfactory es la referencia principal de cómo quiero sentir la gestión: estar físicamente en la fábrica, manipular herramientas, colocar y conectar máquinas, ver el material desplazarse y resolver cuellos de botella. Quiero que esas acciones resulten cómodas, claras y satisfactorias. Desarrolla una identidad propia para Plutón; utiliza recursos, diseños, sonidos e interfaz originales o con licencia válida.

La gestión debe mostrar recetas, entradas y salidas por minuto, energía, capacidad de transporte y motivo de parada. Añade progresivamente divisores y combinadores de cintas, orientación clara de puertos y una construcción con ajuste, rotación y cancelación fiables. Las cifras deben proceder de la simulación; diferencia capacidad teórica de producción real. El ferrocarril mantiene su papel en las distancias largas.

No amplíes ahora el alcance a todos los sistemas de Satisfactory. Prioriza las acciones que el jugador repite durante los primeros minutos y conserva la progresión incremental ya construida.

## 2. Brazos, herramientas e interacción en primera persona

Quiero ver los brazos y las manos del personaje y comenzar con una herramienta física creíble. Una línea luminosa desde el centro de la cámara no satisface este requisito.

Diseña una **multiherramienta industrial de recuperación** original, sujeta con manos y guantes: empuñadura, cuerpo mecánico y cabezal visible para retirar chatarra o depósitos superficiales. Elige un diseño coherente y ejecútalo sin pedirme escoger entre variantes. La mejora inicial debe transformar la interacción que ya existe, sin exigir rehacer todo el terreno o crear destrucción voxel.

Implementa:

- Brazos y manos modelados y animados, conectados visualmente con la herramienta. Los dedos deben sujetarla y las muñecas conservar posiciones creíbles.
- Animaciones diferenciadas de reposo, equipar/guardar, caminar y trabajar. El balanceo debe ser moderado y ajustable para evitar mareo.
- Un cabezal que trabaje cerca del objetivo, respuesta de la herramienta y sonido coherente. Usa partículas breves de polvo, virutas o fragmentos según el material, dentro del presupuesto gráfico.
- Una indicación clara del objeto seleccionado, acción posible, progreso cuando proceda y materiales obtenidos.
- Alcance de interacción definido, interrupción al soltar el control o alejarse y consumo/obtención exactamente una vez por acción válida del servidor.
- Gestos de recoger o interactuar donde aporten claridad, sin obligar a esperar una animación larga por cada unidad de mineral.
- Construcción con herramienta visible y previsualización de la pieza, coste, orientación, ajuste y errores comprensibles.

Puedes seguir usando raycasts internos para seleccionar objetivos. El problema es la presentación y la sensación de uso: no representes toda la acción mediante un rayo genérico que sustituya la herramienta.

Comprueba que los brazos no atraviesan la cámara o la herramienta, no tapan el área útil y se ven correctamente con diferentes FOV y formatos de pantalla. Resuelve de forma controlada el recorte cerca de paredes. Al abrir inventario o menús, se libera el cursor y se suspenden las acciones correspondientes; al cerrar, el control vuelve sin clics o movimientos accidentales.

En cooperativo, los demás jugadores deben ver un personaje y una herramienta coherentes con la acción. El modelo de primera persona puede ser distinto del modelo externo; no dupliques los brazos delante de la propia cámara ni transmitas cada hueso en cada frame. Sincroniza el estado necesario para representar la acción, conservando la autoridad del servidor.

La entrega de esta fase debe incluir una herramienta y un conjunto de animaciones completos y pulidos. Amplía después a variantes y mejoras, evitando sustituir la primera herramienta por un gran catálogo de modelos sin terminar.

### Inventario y accesos inmediatos

Adopta una distribución inicial convencional y reasignable: `Tab` para inventario, `E` para interactuar, `Q` para construcción y números para la barra rápida. Si entra en conflicto con los controles existentes, resuélvelo de forma coherente, migra los ajustes cuando corresponda y actualiza las ayudas visibles.

- Inventario que se abre con una pulsación, con recursos y equipo organizados, iconos legibles, cantidades y nombres accesibles.
- Barra rápida visible para herramientas y construcciones frecuentes, selección clara y cambio rápido de herramienta.
- Transferencia rápida entre inventario y almacenes, división de pilas y arrastrar/soltar donde aporte comodidad; ofrece alternativas mediante clic y teclado.
- Información de materiales que faltan para construir, sin obligar a abrir y cerrar varias ventanas para saberlo.
- Tooltips claros y ausencia de superposiciones, texto cortado o casillas difíciles de pulsar.
- Apertura, cierre, pausa y foco correctos en solitario y cooperativo. Un inventario online abierto no congela el mundo compartido.

Pide al agente de primera experiencia que compruebe estas acciones usando las indicaciones del propio juego y registre clics innecesarios, confusiones y fallos. Estas mejoras forman parte de la próxima entrega jugable.

## 3. Equipo de agentes y responsabilidad

Usa subagentes reales de Claude Code si están disponibles, con Opus 5.5 conforme al requisito original. Comprueba el modelo efectivo y las herramientas que recibe cada uno. No finjas delegación mediante varios párrafos escritos con distintos nombres.

Organiza estos roles; pueden ejecutarse por turnos y no necesitan estar todos activos simultáneamente:

| Rol | Responsabilidad | Entrega |
| --- | --- | --- |
| Coordinador e integrador | Prioridades, integración, builds, decisiones y continuidad | Roadmap actualizado y versión jugable |
| QA de primera experiencia | Jugar desde cero con la información disponible en pantalla | Sesión documentada, bloqueos, pasos confusos y evidencia |
| QA de sistemas y cooperativo | Construcción, inventarios, cintas, trenes, guardados y red | Casos reproducibles, pruebas y errores |
| Arte e interfaz | Definir e implementar modelos, materiales, iluminación y UI | Escena y pantallas mejoradas dentro del juego |
| Revisor de calidad y rendimiento | Revisar la build y medir el coste de los cambios | Aceptación o incidencias concretas con evidencia |

Empieza con dos o tres agentes simultáneos como máximo, ajustándolo al equipo y a los límites de uso. Usa worktrees o reparto claro de archivos. Un responsable integra y exporta; las pruebas se realizan sobre una build identificada e inmutable durante la sesión. No permitas que varios agentes sobrescriban una escena o exporten al mismo directorio a la vez.

El autor de una función puede probarla, pero su aceptación debe incluir una revisión independiente. No basta con asignar otro nombre al mismo razonamiento ni con pedir una opinión sin abrir la build o revisar evidencia pertinente.

## 4. Cómo deben jugar los agentes

Primero verifica qué control real del escritorio está disponible en esta instalación de Claude Code dentro de VS Code, en Windows. No presupongas que disponer de subagentes, un navegador o un MCP proporciona control de la ventana de Godot.

Orden de trabajo:

1. Reutiliza las herramientas de captura y control de teclado/ratón que ya funcionen en el proyecto.
2. Si hay una integración de escritorio compatible y autorizada, comprueba que puede localizar el ejecutable, enfocarlo, enviar entradas y devolver capturas legibles.
3. Si falta, prepara una herramienta local mínima para capturar la ventana y enviar entradas normales de Windows, utilizando APIs o bibliotecas mantenidas. Limita su actuación al proceso del juego y verifica ventana activa, foco y escala de pantalla antes de actuar. No des por resuelto el control del ratón relativo hasta probarlo en primera persona.
4. Si una operación necesita permisos no disponibles, registra ese bloqueo y sigue con las partes implementables. No instales una API de pago ni cambies de plataforma de desarrollo sin necesidad.

Comprueba la documentación oficial actual: las capacidades de la aplicación Desktop y las de la CLI no son necesariamente las mismas. No me indiques activar una función exclusiva de macOS como solución para Windows. Si una herramienta de pantalla solo está disponible en el agente principal, este ejecutará las acciones y devolverá a los revisores las capturas y resultados; no afirmes que cada subagente posee un escritorio propio.

### Bucle de prueba visual

El agente debe **capturar → observar → elegir una acción → usar teclado/ratón → esperar el efecto necesario → volver a capturar → evaluar**. Una secuencia de teclas repetida sin comprobar qué está ocurriendo no constituye por sí sola una exploración jugable.

Abre la build exportada y comienza una partida nueva de prueba. Durante la revisión de primera experiencia, el agente recibe únicamente los controles y objetivos que recibe un jugador. Usa un contexto independiente que no herede la solución de la campaña cuando las herramientas lo permitan; documenta si no puedes aislarlo. Puede consultar información técnica después para diagnosticar, pero esa ayuda no cuenta como descubrimiento mediante la interfaz.

Verifica al menos:

- Abrir, iniciar partida, orientarse y descubrir el primer objetivo.
- Reconocer recursos, limpiar o recolectar, acceder al inventario y construir.
- Equipar la herramienta, observar su uso y respuesta, cambiar por la barra rápida y transferir o dividir recursos sin perder cantidades.
- Seleccionar, rotar, colocar, conectar y desmontar máquinas y cintas.
- Comprender por qué una máquina produce, espera, está llena o carece de energía.
- Obtener el primer ingreso y entender cómo mejorar la producción.
- Usar trenes, estaciones, contratos y viaje interplanetario cuando estén disponibles.
- Abrir ajustes, cambiar resolución/interfaz/controles y recuperar el manejo del juego al cerrar menús.
- Guardar, salir, cargar y continuar sin depender de comandos ocultos.

Para acreditar una prueba de primera experiencia, no entregues recursos por consola, no invoques directamente métodos de completar objetivos, no teletransportes al jugador ni marques pasos como aprobados usando el mismo código que se está probando. Los escenarios preparados son válidos para regresión o rendimiento: identifícalos como tales.

Una herramienta de inyección interna de entradas puede ayudar a automatizar integración, pero no demuestra por sí sola que el foco, los controles del sistema operativo o el ratón funcionen en el ejecutable normal. Indica exactamente qué ruta de interacción se utilizó.

Solo un conductor controla teclado y ratón en un mismo escritorio a la vez. Los demás revisan capturas, código o informes. Para interacción simultánea se requieren entornos realmente separados; no lances varios agentes peleándose por el foco. Mantén las pruebas headless de carga separadas de las sesiones visuales y de las mediciones gráficas.

Si el escritorio está bloqueado, hay capturas negras, falta foco o la herramienta no consigue entrar en el juego, comprueba la causa y marca la prueba como bloqueada. No debilites la seguridad del sistema ni alteres las pruebas para declararlas correctas. Sigue desarrollando otras tareas útiles.

### Evidencia y criterio

Cada incidencia debe incluir versión/commit, contexto, pasos, comportamiento esperado, resultado observado, gravedad y captura o vídeo cuando aporte información. Conserva solo la ventana del juego y usa perfiles de prueba separados de mis partidas.

Guarda una selección de evidencia en `artifacts/qa/<version>/`, con un índice que relacione cada archivo con su caso. Registra acciones y resultados suficientes para reproducir el fallo. Evita miles de capturas idénticas y revisiones que consuman tokens sin aportar información nueva.

Registra tiempo hasta la primera acción útil y el primer ingreso, intentos fallidos, atascos, esperas sin una decisión disponible y ayudas que faltan. Un agente puede detectar problemas de uso y ritmo; sus apreciaciones sobre diversión son hipótesis, no resultados de jugadores humanos. Que un bot complete la campaña en 2,9 horas no demuestra su duración real ni la diversión de una persona.

Las pruebas entre doce procesos de este PC acreditan ese escenario. No equivalen a doce personas, dos ordenadores o Steam entre cuentas diferentes. Mantén cada afirmación dentro del alcance comprobado. Los agentes comparten posibles sesgos; una revisión independiente reduce errores pero no garantiza calidad absoluta.

## 5. Cintas con objetos visibles y transporte legible

El transporte local de materiales sólidos debe utilizar principalmente cintas abiertas. Quiero ver mineral, chatarra, piezas y productos desplazándose encima con modelos y colores reconocibles.

Implementa un primer recorrido completo: **extractor → cinta → procesador → cinta → almacén o estación**. Observándolo debe entenderse qué entra, qué sale y dónde se está acumulando material.

Requisitos:

- Objetos 3D identificables por tipo, con escala apropiada y separación legible.
- Rectas, curvas y conexiones con puertos, dirección y previsualización comprensibles.
- Capacidad y velocidad reales. Los productos se acumulan cuando el destino está lleno y dejan de entrar cuando no hay espacio.
- La máquina consume lo que llega y produce lo que sale. No uses objetos decorativos que circulan independientemente del inventario.
- Si falta energía o se corta una conexión, se muestra y se aplica el estado definido por las reglas del transporte.
- Al eliminar o sustituir un tramo, se conserva o devuelve su carga mediante una regla clara, sin pérdidas o duplicaciones.
- Las conexiones no atraviesan máquinas o suelo de forma incoherente. Añade soportes y remates visuales reutilizables.
- Muestra cantidad, tipo de recurso, dirección, capacidad y causa de bloqueo al inspeccionar.
- El efecto de recoger recursos debe mostrar qué objeto se obtuvo y cuánto, mediante icono/modelo, animación breve y actualización clara del inventario, sin llenar la pantalla de avisos.

Las cintas cubren las distancias locales. Los trenes mantienen su función en el transporte de grandes volúmenes y distancias mayores; equilibra capacidad y coste para que ambos sean útiles.

Los tubos pueden conservarse para líquidos, gases o tramos donde tenga sentido. Si transportan sólidos, utiliza ventanas amplias o segmentos transparentes que permitan ver los mismos objetos reales. Evita cristales y brillos que vuelvan a ocultar la carga. No necesitas implementar dos sistemas completos de sólidos a la vez: primero termina la cinta abierta.

### Red y rendimiento

La carga y las transferencias siguen siendo autoritativas del servidor. Representa cantidades, colas y posiciones de tránsito de forma coherente; los clientes interpolan el estado recibido. No atribuyas recursos por dibujar una malla ni calcules una economía diferente en cada cliente.

Evita un cuerpo físico y un RPC por frame por cada objeto. Usa representación agrupada, instancias y niveles de detalle cuando convenga. Cerca del jugador, lo visible debe corresponder a la carga lógica; a distancia puede simplificarse la representación, conservando el estado económico y señalando cantidades al inspeccionar.

La nueva carga de cintas debe incluirse en los benchmarks. Una fábrica de 500 máquinas sin objetos en transporte no mide el coste del sistema nuevo.

### Migración

Antes de cambiar los antiguos enlaces o tubos, identifica cómo se guardan y sincronizan. Migra partidas mediante una transformación versionada y con copia de seguridad. Conserva inventarios, recetas y conexiones válidas. Si un enlace antiguo no tiene equivalente geométrico, aplica una política explícita y recuperable; no borres progreso en silencio.

## 6. Dirección artística: industrial semirrealista

Toma decisiones coherentes sin esperar que yo elija entre conceptos. Define una guía breve y lleva una zona representativa del juego al acabado objetivo antes de extenderla al resto.

Dirección elegida:

- Plutón: terreno oscuro, hielo con variación creíble, roca estratificada, polvo y restos industriales a escala humana. La maquinaria debe destacar con señalización cálida sobre el entorno frío.
- Marte: suelo mineral rojizo, polvo, roca y arquitectura con identidad propia. Conserva coherencia de materiales y maquinaria entre planetas.
- Máquinas: cuerpos con volumen, biseles y piezas funcionales; estructura, motores, paneles, depósitos y conexiones reconocibles. Añade movimiento visible donde ayude a entender su función.
- Materiales: metal pintado, acero, goma, vidrio, roca y hielo con respuestas diferenciadas. Utiliza texturas y materiales PBR cuando proceda, con escalas de textura y rugosidad consistentes.
- Iluminación: dirección clara, sombras coherentes, ambiente suficiente para leer máquinas y detalle visible. Equilibra exposición, reflejos y contraste. El bloom y la niebla no deben esconder geometría o empeorar la lectura.
- Entorno: variación de alturas, detalles de suelo, estructuras recuperables y puntos reconocibles. La densidad de detalles debe responder a un presupuesto y no llenar cada metro con objetos caros.

Usa referencias visuales públicas para estudiar materiales, composición y jerarquía; registra las decisiones que aprovechas. Elabora recursos originales o con licencia válida para uso comercial, sin copiar recursos de otros juegos.

Reutiliza Blender, scripts y herramientas ya disponibles. Si una nueva dependencia gratuita y fiable aporta una mejora concreta, úsala con los permisos existentes. No hagas depender el resultado de contratar generadores de imágenes o modelos 3D. Si falta acceso a una herramienta opcional, continúa con los recursos que puedas producir.

Prepara una zona representativa con extractor, dos productos distinguibles, cintas con carga real, procesador, estación, tren, terreno y cielo. Produce capturas antes/después con el mismo encuadre, resolución y ajustes comparables. Las imágenes deben proceder del juego funcionando; un render externo sirve de referencia, no acredita que el acabado exista en el juego.

## 7. Menús e interfaz con acabado profesional

Revisa el sistema visual y las pantallas de mayor uso, conservando claridad durante la gestión:

- Tipografías legibles, tamaños, espaciado y alineaciones consistentes.
- Iconos coherentes para recursos, máquinas, energía, transporte y estados.
- Menú principal con composición cuidada y un fondo real del juego que respete el presupuesto gráfico.
- Catálogo de construcción con categorías, búsqueda o filtros cuando sean útiles, vista de máquina, coste y requisitos.
- Inventario legible con iconos, cantidades, selección, información adicional y acciones claras.
- Panel de máquina con receta, entradas/salidas, progreso, energía, producción por minuto y causa concreta de parada.
- Objetivos y contratos con siguiente acción comprensible, recompensa y progreso.
- Lobby y panel de jugadores con conexión, permisos y acciones disponibles, sin botones que aparenten funciones aún inexistentes.
- Estados de foco, hover, selección, pulsación y deshabilitado; apertura/cierre y navegación de teclado coherentes.
- Ajustes organizados y aplicables de verdad, sin textos cortados ni confirmaciones invisibles.

La calidad debe venir de la composición, los iconos, la tipografía y la información útil. Evita añadir ventanas, bordes, animaciones o efectos que compliquen una acción sencilla.

Comprueba 1080p y ultrawide; ajusta escala de interfaz. La información importante debe seguir visible con máquinas, terreno y luz detrás. Revisa contraste y señales que no dependan solo del color.

## 8. Orden de trabajo y aceptación

Conserva la alfa estable y abre una rama de evolución. Actualiza `CLAUDE.md`, el diseño y el roadmap con este cambio de prioridades. La petición de probar visualmente sustituye la decisión de detenerte hasta recibir mi evaluación.

Orden propuesto:

1. Revisar la build actual, capturar la referencia visual y preparar el control del juego y el equipo de QA. Atender primero errores que corrompan partidas, bloqueen el avance o impidan pruebas fiables.
2. Mejorar la experiencia inicial con brazos, una herramienta completa, inventario y barra rápida; implementar la cadena funcional con cintas y carga visible, compatible con cooperativo y guardados.
3. Llevar esa misma zona, el equipo del personaje y sus pantallas al acabado semirrealista objetivo.
4. Hacer revisión jugable y visual independiente, corregir y exportar una alfa con una mejora perceptible.
5. Extender el acabado aprobado por evidencia al resto de máquinas, transporte, planetas y pantallas.
6. Completar las pruebas pertinentes de campaña, carga, red y regresión sobre la nueva versión.

La nueva alfa se acepta cuando:

- Un agente puede recorrer desde una partida nueva hasta producir y vender usando las pantallas y controles normales, o hay un bloqueo externo documentado que impide demostrarlo. En ese último caso no marques la prueba como aprobada.
- La carga visible corresponde al material transportado; un atasco, un corte y la reanudación se entienden visualmente y conservan cantidades.
- Los brazos y la herramienta son visibles y están animados; recolectar tiene respuesta visual y sonora, el inventario es accesible y la barra rápida funciona. El agente ha comprobado estas acciones dentro del juego exportado.
- Dos clientes ven un transporte coherente y las transferencias simultáneas no duplican objetos. Después se verifica el escenario de doce clientes del alcance original.
- La zona representativa y los menús muestran la mejora de geometría, materiales, luz, iconos y composición mediante evidencia concreta; no basta una puntuación subjetiva «9/10».
- Las partidas de la alfa anterior conservan su progreso mediante la migración documentada.
- El rendimiento se mantiene dentro de los objetivos del proyecto con los nuevos materiales y cintas cargadas. Registra frame time, p95/p99, memoria, configuración, hardware y carga de servidor. Revisa también que el juego conserve buena lectura en el preset necesario para alcanzar 60 FPS.

Al cambiar representación, UI o transporte, no heredes automáticamente las métricas de alfa 0.4.0. Repite las comprobaciones afectadas en la nueva build. Repite ensayos largos cuando cambien rutas relevantes de red, simulación o persistencia; no repitas una hora de prueba tras cada ajuste de color.

El pico de latencia de un segundo que mencionaste sigue siendo una incidencia: mide dónde ocurre y relaciona el hallazgo con las nuevas pruebas. Conserva el registro de la caída gráfica sin atribuir su causa al juego o al equipo sin evidencia.

El audio puede comprobarse en cuanto a activación, ausencia de archivos, niveles, saturación o mezcla mediante herramientas disponibles. No declares que alguien lo escuchó si solo revisaste eventos o formas de onda. Lo mismo se aplica a conexiones por Internet, Linux y otro hardware: indica lo que falta y continúa con lo demás.

## 9. Continuidad, permisos e informes

No necesito autorizar cada corrección, elegir cada material ni jugar cada versión para que avances. Decide según este encargo, implementa, somete a revisión independiente y corrige. Cuando haya una discrepancia de criterio entre agentes, el coordinador elige usando evidencia y registra la razón.

Informa brevemente al cerrar un bloque o entregar una alfa: qué mejoró, qué probaron los agentes, versión y evidencia, problemas abiertos y siguiente tarea. Actualiza roadmap, bugs, resultados y handoff. No concluyas con «sin tus comentarios seguiría ajustando a ciegas» mientras queden mejoras y pruebas concretas autorizadas.

Trabaja mientras la sesión y los límites disponibles lo permitan. Conserva el modo de permisos actual. Si una acción requiere intervención, prepara lo posible, registra el bloqueo y pasa a otra tarea; no eludas denegaciones ni habilites facturación adicional. Las cuentas Steam, pruebas en otro PC o herramientas no disponibles son dependencias reales, no motivos para parar todas las mejoras locales.

Haz un máximo inicial de tres ciclos por un mismo problema sin progreso medible; después cambia el enfoque o clasifícalo como bloqueado. No lances agentes indefinidamente para discutir el mismo acabado. No afirmes que puedes trabajar después de que la sesión haya terminado.

Tu primera respuesta debe identificar qué herramientas usarás para jugar y capturar evidencia, qué agentes crearás y cuál es la primera mejora concreta. A continuación, ejecuta el trabajo en la misma sesión. La primera entrega debe incluir una build utilizable con brazos y herramienta física animados, inventario y barra rápida accesibles, cintas con carga visible, una zona mejorada y pantallas principales más cuidadas, junto con resultados de las pruebas realmente realizadas. Puedes entregar avances intermedios pequeños y seguir trabajando, pero no cierres este encargo hasta satisfacer sus criterios o identificar un impedimento real.

## Referencias para comprobar las capacidades disponibles

- [Claude Code: subagentes y herramientas](https://code.claude.com/docs/en/sub-agents)
- [Claude Code: control del ordenador desde CLI y requisitos de plataforma](https://code.claude.com/docs/en/computer-use)
- [Anthropic: control del ordenador en Desktop](https://support.claude.com/en/articles/14128542-let-claude-use-your-computer-in-cowork)

Consulta las páginas vigentes y el entorno instalado antes de dar por disponible una capacidad.
