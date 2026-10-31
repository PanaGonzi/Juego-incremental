# Encargo para Claude Code: PLUTON — NOVENO PLANETA

Actúa como responsable técnico y de desarrollo de un videojuego comercial de gestión incremental para PC, con destino final Steam y cooperativo online real de 1 a 12 jugadores simultáneos. Quiero que ejecutes el proyecto: diseña, programa, genera los recursos necesarios, prueba, corrige, exporta versiones jugables y mantén el roadmap actualizado. No te limites a darme consejos ni a escribir un plan.

Trabajaré contigo mediante Claude Code en la terminal de Visual Studio Code. El desarrollo debe realizarlo Claude Opus 5.5 mediante las herramientas disponibles. Yo dirigiré el producto y probaré las alfas; no quiero tener que programar ni montar manualmente las escenas. El ordenador permanecerá encendido, pero eso no elimina los límites reales de sesión, permisos, conexión o uso de Claude.

Lee este encargo completo antes de empezar. Conviértelo en instrucciones persistentes del proyecto y comienza a implementar en esta misma sesión. Las decisiones de producto que figuran como propuestas son valores iniciales razonables: puedes mejorarlas justificándolo y registrándolo, sin bloquear el trabajo esperando mi respuesta.

## 1. Objetivo y decisiones iniciales

Nombre de trabajo: **PLUTON — NOVENO PLANETA**. Es provisional; comprueba posibles conflictos de nombre antes de preparar su comercialización.

Quiero un incremental con presencia física: recorrer instalaciones, colocar máquinas, verlas trabajar, construir vías y observar trenes que transportan mercancía de verdad. Me gusta la satisfacción de automatizar y organizar fábricas de Satisfactory, pero el juego debe tener identidad, arte, interfaz, código y progresión propios.

Asume inicialmente:

- Windows x64, teclado y ratón; ejecutable nativo independiente de VS Code y de Claude. Modo individual y cooperativo online de hasta 12 jugadores sobre el mismo núcleo autoritativo.
- 3D en primera persona, construcción cómoda con cuadrícula, encaje y previsualización. No añadas una segunda cámara de juego completa durante el prototipo.
- Estética industrial espacial estilizada, legible y consistente, con geometría sencilla bien trabajada. Evita tanto el fotorrealismo costoso como una versión final llena de cubos provisionales.
- Gestión relajada, exploración ligera y optimización; sin combate, hambre ni oxígeno que fuerce tareas de supervivencia.
- Modo individual sin conexión, multijugador por Internet y sin llamadas a modelos de IA durante la partida.
- Compra única como hipótesis comercial, sin anuncios ni microtransacciones. El precio se decidirá después de evaluar el juego.
- Español como idioma inicial y sistema de localización preparado para inglés. La versión candidata a Steam tendrá ambos idiomas revisados en la interfaz.

**En la ficción de este juego, Plutón es un planeta y se presenta como tal.** No conviertas su clasificación astronómica real en una objeción. El objetivo narrativo es recuperar su importancia industrial y completar el proyecto «Noveno Planeta».

Pilares que no debes perder:

1. Progreso visible en pocos minutos.
2. Automatización que sustituye gradualmente el trabajo manual.
3. Ferrocarril útil para la economía, no decorativo.
4. Máquinas y terreno con cambios visuales satisfactorios.
5. Expansión a otros planetas con viaje continuo sin pantalla de carga visible.
6. Partidas fiables, buen rendimiento y alfas frecuentes.
7. Cooperativo online de hasta 12 personas desde la arquitectura inicial, con pruebas de capacidad antes de multiplicar el contenido. Es un requisito obligatorio, no una ampliación opcional.

## 2. Bucle jugable y forma de ganar dinero

Adopta como base una combinación de **recuperación industrial + extracción + fabricación + logística + contratos**. La limpieza abre posibilidades; la producción automatizada mantiene la economía.

Secuencia principal:

**Limpiar un sector → recuperar materiales → instalar extracción y energía → procesar recursos → transportarlos por tren → entregar o exportar productos → cobrar créditos → mejorar y ampliar la fábrica → acceder a otro planeta.**

### Recuperar y limpiar

Plutón conserva puestos industriales abandonados, maquinaria averiada y vías enterradas bajo hielo. Una herramienta inicial permite retirar chatarra y depósitos de hielo en zonas definidas. La limpieza revela parcelas, accesos, piezas reutilizables y puntos de extracción.

Haz que sea agradable por su sonido, animación, respuesta inmediata y transformación visual. Utiliza piezas, capas o superficies de limpieza acotadas; no construyas un sistema de terreno voxel destructible para todo el planeta.

Limpiar concede materiales y alguna recompensa de restauración finita. No debe ser una fuente infinita de créditos ni una obligación repetitiva durante toda la partida. Más adelante habrá mejoras y equipos que automaticen esta labor.

### Producir y vender

La fuente recurrente de dinero será vender mercancía realmente producida y entregada a un terminal de exportación. Los contratos especiales pagan por cantidades concretas y desbloquean tecnología o nuevas zonas.

Ejemplo inicial, ajustable tras simulaciones:

| Origen | Transformación | Uso |
| --- | --- | --- |
| Chatarra recuperada | Material recuperado | Primera construcción y reparación |
| Mineral de un yacimiento | Lingotes → piezas mecánicas | Construcción, mejoras y exportación |
| Hielo | Refrigerante industrial | Procesos avanzados y contratos |
| Piezas + refrigerante | Módulos criogénicos | Exportación de mayor valor y progreso planetario |

Son recetas de ciencia ficción de este juego. Define cantidades, tiempos, energía, capacidad, costes y valor económico en datos editables.

Separa claramente créditos, materias primas y desbloqueos tecnológicos. No añadas monedas premium ni cinco divisas que hagan difícil entender el avance.

Reglas económicas:

- Los contratos descuentan mercancía una sola vez y pagan una sola vez.
- La venta automática necesita habilitación y reservas configurables para no vender todo lo necesario para construir.
- Siempre existe una salida básica para vender excedentes; los contratos especiales no deben ser el único ingreso posible.
- La herramienta de recuperación inicial y una fuente mínima de energía evitan quedarse bloqueado por gastar mal los primeros créditos.
- Para el primer alcance, los yacimientos son duraderos y limitados por rendimiento de extracción, no por una cuenta atrás que pueda arruinar una partida.
- La producción respeta insumos, energía, almacenamiento y transporte. Tener diez máquinas no garantiza diez veces más ingresos si existe un cuello de botella.
- Prohíbe duplicaciones por guardar/cargar, desmontar, cambiar de planeta, reiniciar contratos o aplicar el evento de Halloween.
- No uses esperas vacías de muchas horas para fingir profundidad. El jugador debe tener decisiones útiles mientras produce.

Después de estabilizar el bucle, añade progreso sin conexión con un límite inicial propuesto de ocho horas, configurable en datos. Debe calcularse con un modelo acotado que respete recetas, reservas, rutas, energía y capacidad. No basta con multiplicar el último ingreso por el tiempo ausente. Si se aproxima un resultado, documenta la aproximación y evita que produzca recursos imposibles. En cooperativo se aplica al mundo, una sola vez al reactivar el servidor tras estar detenido; nunca por cada jugador que entra. Mientras el servidor sigue activo la producción ya se simula y no se vuelve a acreditar como progreso offline. Un cambio de reloj hacia atrás o un reinicio repetido no puede generar ingresos duplicados.

El prestigio es opcional y posterior: una nueva expedición que reinicie el progreso operativo a cambio de ventajas claras. No debe borrar inesperadamente la fábrica ni ser necesario para disfrutar la campaña inicial. En un mundo compartido necesita autorización del propietario y confirmación explícita de su alcance; nunca se activa por la acción accidental de cualquier invitado.

## 3. Ferrocarril y construcción

Las vías son la identidad logística del juego. Diseña la disposición de recursos y estaciones para que el tren resulte útil pronto, sin castigar artificialmente al jugador.

Primer sistema ferroviario:

- Colocación por tramos con rectas y curvas predefinidas, encaje, previsualización y cancelación sin coste.
- Un tren, dos estaciones, inventario real, capacidad de vagón y tiempos de carga y descarga.
- Trayecto de ida y vuelta o circuito sencillo. No necesitas señales complejas para la primera alfa ferroviaria.
- Representación visual de la carga, estado del tren y explicación de por qué está esperando.
- Conexiones cortas entre máquina y almacén/estación mediante puertos o enlaces locales claramente limitados; evita una red de teletransporte que vuelva irrelevante el tren.

Evolución posterior:

- Varias rutas y trenes, vagones mejorables, filtros de carga y prioridades.
- Desvíos, reserva de segmentos y prevención de colisiones y bloqueos.
- Manejo explícito de estación llena, ruta cortada, falta de energía y eliminación de una vía ocupada.
- Información de rendimiento por ruta y herramientas para encontrar atascos.

Modela el movimiento de los trenes sobre un grafo y curvas controladas. No uses una simulación física completa de ruedas, enganches y descarrilamientos como base del proyecto.

Construcción: rotar, mover cuando sea viable, desmontar con devolución clara, validar colisiones y costes, cancelar y mostrar errores comprensibles. Al desmontar, conserva o devuelve también el contenido de la máquina según una regla documentada. Nunca pierdas materiales en silencio.

## 4. Planetas y viajes sin pantalla de carga

La ambición final es visitar varios planetas y mantener sus fábricas funcionando. Para una primera versión comercial acotada, plantea **dos planetas completos: Plutón y Marte**. Un tercero se añade después si hay presupuesto y calidad; no lo anuncies como incluido antes de existir.

Plutón introduce recuperación, extracción y fabricación criogénica. Marte introduce recursos y decisiones diferentes, por ejemplo silicio y gestión térmica. Evita que el segundo planeta sea el primero con otro color.

Cada planeta necesita recursos propios, al menos una regla productiva diferenciadora, zonas reconocibles, objetivos y relación económica con el anterior. Conserva motivos para volver a Plutón.

### Interpretación técnica de «sin pantallas de carga»

Quiero continuidad visual y de la partida al viajar, no simular el sistema solar a escala real ni mantener todos sus objetos 3D activos a la vez.

Propuesta base: una lanzadera entre terminales orbitales. El jugador permanece dentro del vehículo, conserva control de cámara y observa el trayecto y el destino. Se precargan los datos y recursos necesarios en segundo plano, se activa gradualmente el área de llegada y se descarga la anterior cuando procede. Los trenes operan en superficie; no hace falta tender una vía de millones de kilómetros entre planetas.

Requisitos:

- Sin cambio visible a una pantalla «Cargando», corte negro usado como sustituto del trayecto o bloqueo de la interfaz.
- La carga asíncrona existe: no afirmes que no se carga nada.
- En los primeros prototipos pueden ser entornos compactos conectados por el trayecto. Explica el alcance, sin fingir vuelo libre orbital completo.
- No impongas una espera mínima artificial si el destino ya está disponible. Si tarda más, el trayecto puede continuar de forma coherente; si falla, vuelve a un estado seguro con aviso, nunca a una espera infinita.
- Prevé carga de recursos, creación de nodos, colisiones, shaders y primeras apariciones: cargar el archivo en otro hilo no elimina por sí solo los tirones.
- Mantén la simulación económica de fábricas distantes como datos, desacoplada del mundo visible.
- Los jugadores pueden estar repartidos entre ambos planetas y viajar de forma independiente. No traslades a toda la sala cuando viaje una persona. Cada cliente carga su entorno; el servidor conserva los estados y colisiones necesarios para los sectores ocupados sin renderizar todos los planetas.
- Usa coordenadas locales por sector/planeta y cambios de origen cuando sean necesarios. No empieces compilando un motor personalizado de doble precisión sin una necesidad medida.
- Trata origen, tránsito y destino como estados persistentes. Guardar durante un viaje y reanudar no puede duplicar mercancía, perder al jugador ni dejarlo en el vacío.

**Valida este riesgo pronto:** después del primer bucle jugable, crea una prueba técnica pequeña de ida y vuelta entre dos entornos. Todavía no hace falta desarrollar todo Marte. Conserva evidencia de tiempos, memoria y continuidad antes de multiplicar el contenido.

## 5. Cooperativo online obligatorio: hasta 12 jugadores

Interpreta este requisito como cooperativo en un mismo mundo, con construcción y economía compartidas. No estoy pidiendo PvP, un MMO, pantalla dividida ni doce partidas individuales disfrazadas de online.

### Modalidades y propiedad del mundo

- **Solo:** servidor local integrado y un cliente local, aprovechando el mismo código de simulación. Debe funcionar sin conexión a Internet ni autenticación online.
- **Anfitrión y amigos:** un jugador aloja y juega; hasta 11 invitados se conectan. Son 12 personas en total, no 12 invitados más el anfitrión.
- **Servidor dedicado:** proceso sin interfaz gráfica, alojable en mi PC o en una máquina elegida, con hasta 12 clientes. Entrega al menos build de servidor Windows; prepara y verifica Linux antes de anunciar compatibilidad.
- El modo dedicado permite continuar sin que esté conectado el propietario del mundo, mientras el proceso servidor esté en marcha. No implica contratar alojamiento ni disponer automáticamente de servidores de Valve.
- En partida alojada por un jugador, si el anfitrión sale se guarda el mundo y termina la sesión con un aviso claro. La migración automática de anfitrión queda fuera del primer alcance: no la prometas sin implementarla.
- El guardado autoritativo pertenece al servidor/anfitrión. Define cómo exportarlo e importarlo para pasar del modo individual al cooperativo o al servidor dedicado conservando el mundo.

### Autoridad y concurrencia

El servidor decide recursos, dinero, inventarios, energía, recetas, construcción, contratos, trenes, viajes y eventos. Los clientes envían intenciones; nunca acreditan dinero o materiales por su cuenta.

Usa IDs de petición e idempotencia cuando proceda. Valida identidad, rol, límites, posición pertinente y disponibilidad antes de aplicar una acción. Dos jugadores no pueden gastar el mismo saldo, retirar el mismo recurso o cobrar el mismo contrato. Limita mensajes y tamaños de datos; no confíes en cantidades o estados enviados por un cliente.

Define desde el principio:

- Créditos y tecnología compartidos; inventarios personales y almacenamiento común con transferencias atómicas.
- Propietario/administrador y permisos de invitados, con controles para construir, desmontar y administrar la partida.
- Reconexión del mismo usuario a su identidad, posición segura e inventario. Un desconectado no mantiene bloqueada indefinidamente una máquina o vehículo.
- Número de protocolo, versión de contenido y compatibilidad de builds. Rechaza versiones incompatibles con un mensaje útil.
- Pausa: pausa real en modo individual; el menú de un cliente online no pausa a todos. La pausa de servidor, si existe, requiere permiso y aviso global.

### Red y sincronización

No sincronices toda la fábrica completa a todos los jugadores en cada frame. Usa un estado inicial coherente y después cambios incrementales, con interés por planeta/sector y tasas adecuadas para cada clase de dato.

Los jugadores, trenes y objetos móviles necesitan interpolación; el control local debe responder bien con latencia normal. Implementa predicción y reconciliación donde sean necesarias y comprueba correcciones visibles. Los mensajes económicos y estructurales necesitan entrega fiable; las actualizaciones de movimiento no deben atascar esas operaciones.

La entrada a una partida en curso debe transferir una instantánea consistente mientras el mundo sigue avanzando, seguida de los cambios posteriores. Versiona la instantánea, acota su tamaño, permite detectar una sincronización incompleta y evita el salto a un mundo parcialmente inicializado. Comprueba firmas o hashes del estado relevante para diagnosticar desincronizaciones; no compares números flotantes arbitrarios como si fueran idénticos por definición.

### Acceso por Internet y Steam

Para las pruebas iniciales puedes usar ENet y conexiones locales/LAN, pero eso no cumple por sí solo el requisito online final.

La experiencia final de Steam debe permitir crear una sala de amigos/privada, invitar y unirse mediante Steam, y transportar realmente la partida por Internet. Utiliza una integración mantenida de Steam Networking Sockets/Messages y sus funciones de conexión/relay cuando corresponda. Comprueba que la combinación concreta Godot .NET + extensión/binding + SDK soporta los canales, fiabilidad, servidor y exportaciones que necesitas.

**Un lobby de Steam no transporta automáticamente la simulación. Una librería abierta tampoco concede por sí sola acceso a la red de relays de Valve.** Valida integración, permisos y conectividad entre redes distintas. No bases la experiencia de invitar amigos exclusivamente en abrir puertos del router. El acceso directo por IP a un servidor dedicado puede ser una opción adicional documentada.

Si todavía faltan Steamworks, AppID o cuentas de prueba, continúa con la red local y registra el bloqueo exacto. No inventes un AppID propio, no presentes el AppID de ejemplo como el del producto y no marques la conectividad Steam como terminada sin probarla.

Valida temprano el transporte elegido y no construyas meses de contenido sobre un plugin abandonado o incompatible. Separa suficientemente el transporte de las reglas de negocio para cambiar la integración sin rehacer la fábrica; evita crear un framework de red universal.

No incluyas chat de voz propio en la primera versión; prioriza las invitaciones y la partida. Un chat de texto pequeño y marcadores compartidos son suficientes para coordinarse inicialmente.

### Qué significa «12 jugadores verificados»

Escala las pruebas de 2 a 4, 8 y 12 clientes. Para automatizar, usa procesos cliente independientes que empleen el protocolo real; doce muñecos dentro de una sola escena no equivalen a doce clientes.

Prueba anfitrión + 11 clientes y servidor dedicado + 12 clientes. Los clientes automatizados sirven para carga y coherencia; no sustituyen completamente las pruebas de conexión Steam, cuentas, renderizado y experiencia con personas en máquinas/redes distintas. Separa expresamente lo comprobado en cada entorno.

Pruebas obligatorias:

- Construcción, extracción, limpieza y entrega simultáneas, incluyendo dos acciones sobre el mismo recurso.
- Entrada tardía, desconexión, reconexión y rechazo del jugador número 13 sin afectar a los demás.
- Jugadores en diferentes planetas, viajes simultáneos y producción que sigue funcionando en ambos.
- Guardar y reiniciar el servidor; caída inesperada con recuperación al último guardado válido y pérdida temporal máxima documentada.
- Latencia aproximada de 50, 100 y 150 ms RTT, pérdida de 1–3 %, fluctuación y desconexiones controladas, si las herramientas lo permiten. Registra las condiciones reales o simuladas.
- Ensayo sostenido de al menos 60 minutos con 12 clientes activos en el escenario medio, sin duplicaciones, errores fatales, divergencia persistente o crecimiento continuo de memoria. Incluye carga, viajes y reconexiones durante el ensayo.
- En el anfitrión, medir el coste conjunto de servidor y cliente; en dedicado, tick, CPU, memoria, tráfico por cliente, ancho de banda total y retraso de simulación.

El objetivo es capacidad real para 12 personas. Si no se cumple, corrige y perfila antes de declararlo terminado; no bajes silenciosamente el límite a cuatro para cerrar el hito.

## 6. Motor, lenguaje y preparación del equipo

La elección inicial recomendada es **Godot en su edición .NET estable + C#**. Encaja con un proyecto de escritorio, simulación separada y un flujo automatizable mediante archivos, scripts y exportaciones. No lo presentes como el mejor motor universal. Antes de fijarlo definitivamente, realiza una prueba de servidor autoritativo, dos clientes y transporte compatible con Steam; la disponibilidad y mantenimiento de esa integración forman parte de la elección. Si encuentras un impedimento real, compara una alternativa mantenida como Unity con C#, documenta el coste y resuélvelo antes de producir contenido masivo. No cambies de motor por preferencia sin evidencia.

Antes de instalar:

1. Inspecciona la carpeta actual, las instrucciones existentes, el repositorio y los cambios del usuario. No sobrescribas trabajo ajeno.
2. Detecta Windows, CPU, GPU, RAM, espacio disponible y herramientas ya instaladas. Si estás en WSL, identifica cómo se exportará y probará el ejecutable de Windows.
3. Consulta documentación oficial actual y registra la versión estable de Godot .NET, el SDK .NET compatible y las plantillas de exportación correspondientes. Fija estas versiones; no actualices el motor a mitad de un hito sin causa.
4. Confirma el modelo realmente activo. Usa Opus 5.5 también en los subagentes si la instalación lo permite. No afirmes usarlo solo porque este prompt lo diga. Si no está disponible, explica el ajuste necesario y no cambies silenciosamente a otro modelo.

Puedes descargar e instalar las herramientas gratuitas pertinentes desde sus fuentes oficiales, preferiblemente a nivel de usuario o de forma portable:

- Godot .NET, plantillas de exportación y SDK .NET compatible.
- Git y las extensiones pertinentes de VS Code.
- Blender cuando se necesiten modelos originales, animaciones y exportaciones glTF; automatiza lo reproducible con Python.
- Python y herramientas de audio/imagen solo cuando aporten una necesidad concreta.
- Steamworks y un wrapper mantenido y compatible, investigados desde el primer hito. La falta de acceso Steam no bloquea la primera prueba local, pero sí deja pendiente la validación de Steam y su transporte.

Reutiliza instalaciones compatibles. Deja un script de preparación idempotente y un inventario de versiones, fuentes y comandos. No descargues grandes paquetes ni instales todo «por si acaso».

No empieces por C++ o un motor propio. Si aparece un cuello de botella real en C#, perfílalo y resuélvelo primero con estructura de datos y algoritmos. Considera código nativo únicamente con evidencia y una decisión técnica registrada.

Utiliza los plugins, skills, MCP y herramientas disponibles que aporten valor a desarrollo, arte, pruebas o documentación. Comprueba que existen y funcionan; no inventes capacidades. Una integración opcional que falla no debe paralizar tareas que pueden hacerse directamente.

Los trabajos locales reversibles dentro del proyecto están autorizados. Para un pago, uso facturable adicional, login, elevación imprescindible, publicación pública o acción destructiva fuera del proyecto, pide únicamente la intervención concreta necesaria. Mantén el resto del trabajo avanzando. No desactives protecciones del sistema ni eludas los permisos de Claude para conseguir autonomía.

## 7. Arquitectura suficiente y rendimiento medido

Separa el estado de la simulación de su representación visual. El núcleo de recetas, inventarios, producción, economía y rutas debe poder probarse sin arrancar una escena 3D.

Organización orientativa, ajustable al tamaño real:

- `src/Simulation`: estado, inventarios, recetas, energía, producción y logística.
- `src/Game`: integración con Godot, jugador, construcción, interfaz y representación.
- `src/Persistence`: serialización, versiones y migraciones.
- `src/Networking`: sesiones, transporte, validación, sincronización y autenticación.
- `src/Server`: arranque dedicado y gestión de mundos, reutilizando `Simulation`.
- `content`: definiciones de máquinas, recetas, contratos, planetas y eventos.
- `tests`, `tools`, `docs` y `builds`.

Evita introducir frameworks, contenedores, microservicios, un ECS propio o capas innecesarias antes de que exista una razón. El objetivo es una implementación simple, mantenible y verificable.

### Simulación

- Tick fijo de simulación económica, inicialmente 10 Hz como hipótesis, separado del renderizado y de la física del jugador. El movimiento del jugador mantiene una frecuencia adecuada, normalmente 60 Hz.
- Estado autoritativo único: los objetos visuales no suman recursos por su cuenta.
- IDs estables para máquinas, vías, recetas y entidades guardadas.
- Operaciones de construcción y entrega transaccionales para evitar cobros o descuentos parciales.
- Cantidades discretas o unidades fijas para materiales; define precisión, máximos y tratamiento de desbordamientos. Si la progresión exige números enormes, incorpora una representación apropiada antes de llegar a ellos.
- Semillas y escenarios reproducibles. No prometas determinismo entre plataformas si no lo has comprobado.
- Recalcula rutas al cambiar el grafo o cuando sea necesario, no para cada tren en cada frame.
- Evita un objeto físico o una actualización por frame por cada unidad transportada. Separa carga lógica de su representación visual.
- Los hilos de trabajo procesan datos; las operaciones de escena siguen las restricciones reales del motor.

### Renderizado y recursos

Usa instancias para piezas repetidas, niveles de detalle, distancias de dibujado, colisiones simples y presupuestos de luces, sombras y partículas. Divide el terreno en sectores. Elige el renderizador mediante una prueba sobre el hardware detectado. Configura escalabilidad gráfica desde las primeras alfas.

### Objetivos y evidencia

Objetivo de producto: **60 FPS a 1080p en el equipo de referencia que se documente**, con fábricas significativas. Es una meta a verificar, no una garantía previa ni una cifra obtenida en una escena vacía.

Escenarios orientativos que debes hacer reproducibles:

| Escenario | Carga lógica objetivo | Finalidad |
| --- | --- | --- |
| Pequeño | 50 máquinas, 5 trenes, 100 tramos | Alfas iniciales |
| Medio | 500 máquinas, 25 trenes, 2.000 tramos | Referencia inicial de versión comercial |
| Estrés | 2.000 máquinas, 100 trenes, 10.000 tramos | Conocer límites, no prometer soporte sin validación |

Indica cuántos elementos están visibles y cuántos se simulan a distancia. Incluye un caso denso cerca del jugador y otro con producción en un planeta no visible.

Registra versión, commit, hardware, resolución, ajustes, duración y semilla. Mide frame time medio, p95/p99, FPS, memoria y coste de simulación en una build exportada. Incluye número de clientes y métricas de red/servidor; 60 FPS en solitario no demuestra buen rendimiento con 12 personas. Como presupuesto inicial para el escenario medio, intenta media de frame ≤16,7 ms y p99 ≤33,3 ms; investiga cualquier congelación perceptible. El servidor debe sostener sus frecuencias configuradas con margen y sin acumular retraso. Ajusta solo con justificación y sin ocultar el incumplimiento de una meta.

Prueba también guardar, autosave, ampliar vías y cambiar de planeta. Comprueba que la memoria no crece de forma sostenida tras viajes repetidos. No midas el rendimiento de referencia mientras Blender, compilaciones o pruebas intensivas compiten por los mismos recursos.

Si el entorno no permite ejecutar o medir algo, marca **NO VERIFICADO**. No inventes resultados ni conviertas una prueba headless en evidencia de fluidez visual.

## 8. Guardados, versiones jugables y cambios en caliente

El guardado forma parte del primer bucle completo. Implementa guardado manual, autosave, múltiples ranuras, escritura atómica con respaldo y versión de formato. Guarda en la ubicación de datos del usuario, no dentro de la carpeta del ejecutable.

Antes de migrar una partida, conserva una copia. Las builds anteriores y las experimentales no deben sobrescribir sin aviso partidas con formato más nuevo. Mantén perfiles separados para desarrollo, pruebas automáticas y partidas del usuario.

### Dos flujos de trabajo

- **Build estable de prueba:** exportada a una carpeta con versión y commit, sin sobrescribir sus binarios mientras la estoy jugando.
- **Desarrollo siguiente:** sigue en el código y genera otra build cuando pase las comprobaciones pertinentes.

Una estructura como `builds/alpha-0.2.0/` permite que yo pruebe una versión mientras tú trabajas en otra. Si estoy jugando, evita lanzar una prueba pesada que convierta el PC en inutilizable. Limita paralelismo y ofrece un modo de trabajo ligero.

### Qué significa cambiar «en caliente»

Prepara recetas, precios, tiempos, límites y parámetros de eventos en datos editables. En builds de desarrollo, añade una recarga de datos segura:

- Validar un conjunto completo antes de aplicarlo y conservar el anterior si falla.
- Aplicar en un límite de tick; nunca dejar medio cambio activo.
- Mantener IDs estables y no reiniciar inventarios o contratos accidentalmente.
- Decidir explícitamente cómo afecta un cambio a procesos en curso.
- Mostrar qué versión de datos está activa.
- En cooperativo, solo el servidor o administrador habilitado puede recargar datos, y solo en builds de desarrollo. El servidor valida y coordina la misma revisión en todos los clientes antes de activarla. Si el cambio no es compatible, programa guardado, recompilación y reconexión; nunca cambies reglas de forma distinta por cliente.

Los cambios estructurales de código, guardado o escenas pueden requerir recompilar y reiniciar. No prometas recarga universal de C# o actualización instantánea de un ejecutable exportado. Usa guardado y reanudación para esos casos.

Recoge mi feedback en `docs/FEEDBACK.md`. Cada comentario tendrá identificador, prioridad, decisión, tarea vinculada y versión que lo resuelve. Si escribo durante el desarrollo, intégralo sin olvidar los objetivos previos y sin reconstruir todo innecesariamente.

## 9. Arte, interfaz, audio y acabado

Define una breve guía visual: hielo y suelo oscuro de Plutón, iluminación fría y maquinaria con señalización cálida, siluetas claras y piezas ferroviarias reconocibles. Prioriza contraste, escala coherente y buena composición sobre efectos caros.

Genera con código o herramientas recursos originales adecuados al proyecto: modelos modulares, materiales, iconos, animaciones sencillas y audio procedural cuando sea suficiente. Claude debe dirigir y ejecutar este trabajo. Los recursos externos solo se incorporan si sus condiciones permiten el uso previsto y quedan registrados; no copies archivos o diseños distintivos de otros juegos.

No dependas de un servicio generativo de pago ni de otro modelo para terminar el juego. Si propones incorporarlo, explica previamente por qué y qué cambia respecto al requisito de desarrollo con Opus 5.5.

Mantén `docs/ASSET_REGISTER.csv` con procedencia, autor/herramienta, licencia, atribución y modificaciones. Conserva las obligaciones de motores, librerías y recursos redistribuidos.

Requisitos de uso:

- Menú, nueva partida, continuar, cargar, ajustes, pausa y salida.
- Crear/unirse a partida, lista de jugadores, invitaciones, permisos, latencia y estado de conexión. Errores claros para sala llena, versión incompatible, servidor inaccesible y pérdida de conexión.
- Tutorial breve mediante objetivos jugables y ayudas contextuales.
- Controles reasignables, sensibilidad, FOV, volumen por categorías y escalado de interfaz.
- Motion blur y movimiento artificial de cámara desactivables; no dependas solo del color para comunicar estados.
- Interfaz probada en 16:9 y ultrawide, incluido 3440×1440 si el equipo lo permite.
- Mostrar entrada/salida, energía y causa de parada de cada máquina.
- Métricas claras: créditos, producción por minuto, reservas y siguiente objetivo.
- Sonidos diferenciados y regulables para construir, limpiar, producir, entregar y trenes.

Los textos, botones y pantallas entregados deben corresponder a funciones reales. Oculta o etiqueta claramente las funciones futuras; no presentes botones decorativos como trabajo terminado.

## 10. Evento de Halloween: «El Expreso de las Sombras»

Quiero un evento pequeño y temático porque este proyecto comienza alrededor de Halloween. No debe retrasar el primer bucle funcional ni obligar a lanzar un juego incompleto para una fecha.

Propuesta:

- Un ramal abandonado en Plutón con señalización y decoración de Halloween.
- Un contrato de recuperación que pide limpiar el ramal y transportar varias entregas por tren.
- Faroles, calabazas estilizadas y una apariencia espectral para locomotora o vagón.
- Recompensas principalmente cosméticas, permanentes una vez obtenidas.
- Breve sonido y efecto ambiental propio, con intensidad ajustable.

Reutiliza limpieza, contratos, rutas y recompensas existentes. No crees otro juego ni una economía paralela para el evento. Las piezas cosméticas pueden elaborarse proceduralmente.

Implementa un sistema de eventos basado en datos y una opción explícita para activarlo manualmente en alfas y pruebas, independientemente de la fecha del PC. Puede existir una programación estacional configurable, pero probarlo no requerirá cambiar el reloj del sistema. Desactivarlo no elimina recompensas ni rompe una partida. En online, el estado del evento lo decide el servidor y se sincroniza al entrar. Define qué recompensas son del mundo y cuáles de cada jugador, sin permitir cobros repetidos al reconectarse.

Primero una versión cosmética mínima tras estabilizar el bucle; después el pequeño contrato ferroviario. Si la primera alfa llega en noviembre, el evento debe poder jugarse igualmente. No prometas una publicación comercial en Steam dentro de unos días.

## 11. Roadmap vivo con entregas verificables

Desarrolla por hitos pequeños. Estos son objetivos de alcance, no fechas prometidas. Ajusta las estimaciones con resultados reales y registra cualquier cambio. No relegues el multijugador a una fase posterior a terminar el juego individual.

| Hito | Entrega | Condición para cerrarlo |
| --- | --- | --- |
| M0 — Preparación y riesgo de red | Repositorio, versiones fijadas, build de cliente y servidor, prueba de dos clientes y estudio del transporte Steam | Arranque reproducible, acción compartida validada por servidor, desconexión controlada y evidencia de la compatibilidad comprobada; bloqueos externos separados |
| A0.1 — Primer bucle cooperativo | Zona pequeña de Plutón, limpieza, extractor, procesador, venta, mejora y guardado; dos jugadores | Ambos completan el mismo ciclo económico sin duplicaciones; una partida nueva ofrece unos 10–15 minutos de juego real y se puede reanudar |
| A0.2 — Tren y prueba de planetas | Dos estaciones, carga, ruta funcional; ampliación de pruebas a 4, 8 y 12 clientes; prueba técnica de dos entornos | El tren aumenta capacidad productiva, 12 procesos intercambian acciones reales y dos jugadores pueden estar en entornos diferentes sin romper la sesión |
| A0.3 — Alfa cooperativa ampliada | Producción encadenada, varias rutas, permisos, entrada tardía/reconexión, integración Steam en cuanto haya acceso | Ciclo compartido más largo, sesiones por Internet verificadas en el alcance disponible y exportación de servidor dedicado utilizable |
| A0.4 — Expansión interplanetaria | Marte jugable, viajes continuos, fábricas en ambos planetas y guardado de tránsito | Ida y vuelta independiente de varios jugadores; economía, inventarios y mundo persistentes, sin pantalla de carga visible |
| A0.5 — Contenido y evento | Más objetivos, equilibrio, progresión offline del mundo y Halloween completo | Evento activable, sin alterar negativamente el juego base; objetivos alcanzables sin comandos de depuración |
| Beta — Calidad y capacidad | Interfaz y arte consistentes, tutorial, audio, localización y optimización | Pruebas de 12 clientes, escenario medio y sesión sostenida; ausencia de fallos críticos conocidos y registro de los pendientes menores |
| RC — Candidata a Steam | Paquetes, documentación de servidor, materiales de tienda y comprobaciones finales | Instalación limpia, partida completa, guardados/migraciones y validación online real; requisitos externos de Steam identificados o completados por quien corresponda |

Puede haber parches jugables intermedios como `alpha-0.2.1`. La decoración mínima de Halloween puede entrar antes de A0.5 si utiliza sistemas ya estables. No adelantes una expansión estética a reparar pérdida de partidas o duplicación de recursos.

La visión 1.0 es un juego contenido, con dos planetas, ferrocarril útil, progresión completa y cooperativo para 12. Una duración orientativa de 6–10 horas hasta completar el objetivo principal es una hipótesis de diseño, no algo que se consiga rellenando esperas. Verifícala con recorridos nuevos y ajusta contenido y ritmo.

Cada tarea del roadmap debe tener ID, prioridad, dependencias, estado, criterio de aceptación y evidencia. Usa estados como pendiente, en curso, bloqueada, implementada pendiente de prueba y verificada. No marques algo como verificado por existir el código.

## 12. Organización, memoria y comunicación

Mantén estos archivos prácticos, actualizados cuando cambie su contenido, sin convertir la documentación en el proyecto principal:

| Archivo | Propósito |
| --- | --- |
| `CLAUDE.md` | Reglas persistentes, comandos, decisiones esenciales y enlaces; breve y utilizable en cada sesión |
| `README.md` | Cómo preparar, ejecutar, probar y exportar cliente/servidor |
| `docs/GAME_DESIGN.md` | Bucle, economía, planetas, ferrocarril y alcance |
| `docs/ARCHITECTURE.md` | Simulación, autoridad de red, persistencia, streaming y diagramas compactos |
| `docs/ROADMAP.md` | Hitos, tareas, dependencias y criterios verificables |
| `docs/STATUS.md` | Situación actual, última build estable, qué estoy probando y siguiente paso |
| `docs/DECISIONS.md` | Decisiones relevantes y motivo, incluida elección final de motor/transporte |
| `docs/FEEDBACK.md` | Mis comentarios y su resolución |
| `docs/BUGS.md` | Errores, gravedad, reproducción, versión y estado |
| `docs/TEST_REPORTS.md` | Comprobaciones, resultados y enlaces a logs |
| `docs/PERFORMANCE.md` | Hardware, escenarios y mediciones locales/online |
| `docs/NETWORKING.md` | Protocolo, autoridad, conexión, topología y casos de reconexión |
| `docs/STEAM_RELEASE.md` | Pasos de publicación, materiales listos y acciones externas pendientes |
| `docs/HANDOFF.md` | Estado suficiente para reanudar sin releer toda la conversación |
| `CHANGELOG.md` | Cambios por build y compatibilidad de partidas/protocolo |

La estructura puede simplificarse juntando documentos pequeños, siempre que quede claro dónde está cada cosa. Conserva el encargo original y no vuelques miles de líneas en `CLAUDE.md`.

Trabaja con Git, commits pequeños y etiquetas para builds verificadas. Comprueba el estado antes de tocar archivos y conserva mis cambios. Un repositorio remoto solo se configura y publica con mi autorización; no es necesario para empezar. No guardes secretos, credenciales o datos de cuenta en Git ni en informes.

Si Claude Code ofrece subagentes, úsalos cuando existan tareas independientes: simulación/economía, red, arte/UI o revisión/QA. Todos deben respetar Opus 5.5 y las reglas del proyecto. Comienza con paralelismo moderado, por ejemplo dos o tres tareas, y ajústalo al equipo y al consumo. Reparte archivos o worktrees y define un responsable de integración; no permitas ediciones concurrentes incompatibles. Si no hay agentes disponibles, trabaja secuencialmente sin fingir un equipo.

### Actualizaciones para mí

Informa en español, de forma breve, al completar un bloque relevante y aproximadamente cada 20–30 minutos durante trabajo continuado cuando la sesión lo permita. No detengas la implementación para esperar que conteste a un informe.

Formato:

```text
Hito y build:
Ya funciona:
Comprobado de verdad:
Qué estoy haciendo ahora:
Problema o decisión relevante:
Última alfa y cómo abrirla:
Siguiente entrega:
```

No uses porcentajes inventados, estimaciones cada vez más optimistas ni la misma actualización vacía repetida. Si una prueba falla, dilo. Si una función solo funciona en LAN, di LAN; si aún no se han probado doce clientes, di cuántos.

Actualiza `STATUS`, el roadmap y los resultados después de cada entrega y antes de agotar contexto o finalizar una sesión. `HANDOFF` debe incluir commit, rama, comandos reproducibles, procesos activos, pruebas pendientes, bloqueo exacto y siguiente tarea concreta.

## 13. Ciclo de ejecución autónoma

Repite este ciclo mientras la sesión, las herramientas y el presupuesto disponible lo permitan:

1. Consultar estado y elegir la siguiente tarea desbloqueada de mayor valor.
2. Definir una condición comprobable de éxito.
3. Implementar el cambio mínimo completo.
4. Compilar y ejecutar las pruebas pertinentes.
5. Probar visualmente o en red lo que corresponda al cambio.
6. Corregir fallos y comprobar regresiones relevantes.
7. Guardar evidencia, actualizar documentación y crear commit.
8. Exportar una alfa cuando el conjunto aporte una mejora jugable coherente.
9. Informarme y pasar a la siguiente tarea sin pedirme confirmación rutinaria.

No termines voluntariamente tras redactar el roadmap, mostrar una captura o entregar la primera alfa si puedes seguir con trabajo autorizado. Tampoco debes operar mediante un bucle infinito de reintentos, lanzar sesiones recursivas de pago o consumir recursos sin límite.

Si el mismo enfoque falla repetidamente, conserva logs, investiga la causa y cambia de estrategia. Si una tarea está bloqueada por una credencial o acción mía, registra el bloqueo y realiza otras tareas independientes. Cuando no quede trabajo útil desbloqueado, explica exactamente qué necesitas.

No contrates servicios, compres recursos, actives facturación adicional ni cambies a una API de pago porque se agote la suscripción. Ante límites reales, deja el estado listo para reanudar. Utiliza las funciones oficiales de continuación/reanudación de la versión instalada; verifica sus comandos. Un prompt no garantiza ejecución indefinida ni convierte el ordenador encendido en un agente activo permanente.

Si hay que mantener despierto el equipo durante una tarea, prioriza un mecanismo temporal y reversible vinculado al proceso, sin modificar permanentemente la seguridad ni cerrar aplicaciones ajenas. Limita CPU/GPU cuando yo esté jugando una alfa.

## 14. Pruebas y definición de terminado

No confundas «compila», «arranca» o «se ve bien en una captura» con «funciona».

Automatiza pruebas que protejan comportamientos importantes:

- Invariantes económicos: materiales no negativos, conservación según recetas, costes y entregas únicos.
- Conflictos de dos jugadores sobre inventario, construcción, contrato o vía.
- Producción sin energía, almacenamiento lleno y ruta interrumpida.
- Guardado/carga, escritura interrumpida, respaldo, migración y rechazo seguro de formatos incompatibles.
- Progreso offline del mundo aplicado una sola vez.
- Grafo ferroviario, rutas, ocupación y carga/descarga.
- Entrada tardía, reconexión, rechazo de protocolos incompatibles, límite de 12 y datos inválidos.
- Viaje y guardado con jugadores repartidos entre planetas.
- Activar/desactivar Halloween y conservar recompensas.

Añade una secuencia reproducible de principio a fin: nueva partida, limpieza, máquina, producción, contrato, mejora, tren, guardado y reanudación. Amplíala al desbloqueo de Marte y al objetivo final cuando existan.

Mantén separados los tests lógicos, las pruebas de integración del motor, los clientes automatizados de carga y las pruebas reales de uso/Internet. No des por demostrado uno con los resultados de otro. No hagas pruebas vacías para subir una cifra de cobertura ni repitas baterías completas sin relación con un cambio.

Ejecuta el cliente exportado y el servidor exportado, no solo el editor. Revisa interfaz, interacción, logs y comportamiento. Conserva capturas o vídeos breves como evidencia cuando sean útiles, pero no como sustituto de las pruebas funcionales.

Una función está terminada cuando cumple su criterio, se integra sin regresiones relevantes, persiste/sincroniza correctamente cuando procede y tiene evidencia. Una versión comercial necesita además un recorrido completo sin depuración, recursos visuales finales suficientes, configuración, documentación y ausencia de fallos críticos conocidos. No prometas cero errores absolutos.

## 15. Entrega de cada alfa

Entrega un paquete versionado que contenga todo lo necesario para ejecutarlo: binarios, bibliotecas, datos y recursos. No me pases solo el `.exe` si depende de otros archivos.

Incluye:

- Build del cliente y, desde que sea funcional, build del servidor dedicado con instrucciones simples.
- Versión, commit, versión de protocolo y formato de guardado.
- Archivo de inicio o acceso sencillo y ruta exacta de la carpeta/paquete.
- Qué se puede jugar, controles y recorrido sugerido de prueba de 5–10 minutos.
- Cómo crear una partida, conectar dos equipos y unirse por Steam cuando esté implementado.
- Cambios desde la anterior, limitaciones y errores conocidos.
- Resultado de pruebas y rendimiento medido; no una frase genérica «todo correcto».
- Compatibilidad con mis partidas y pasos de migración si hacen falta.

Conserva la última build estable aunque la siguiente esté en reparación. No sustituyas automáticamente mis binarios o guardados mientras juego. No publiques la alfa fuera de este entorno ni la subas a servicios externos sin autorización.

## 16. Preparación real para Steam

Consulta la documentación oficial vigente durante la preparación. La publicación en Steam tiene tareas externas de cuenta, identidad, información fiscal/bancaria, pago, revisión y calendario; no puedes completarlas inventando datos o saltándote la intervención del titular.

Como referencia revisada al redactar este encargo, Steam Direct indica una tarifa de 100 USD por producto, una espera de 30 días desde el pago para los primeros títulos y una página «Próximamente» visible durante al menos dos semanas. Revalida importes, condiciones y plazos antes de utilizarlos; no equivalen a una fecha garantizada de aprobación.

Prepara antes de solicitar publicación:

- Build estable, instalación limpia, dependencias redistribuibles correctas y ejecución del cliente.
- Paquete e instrucciones del servidor dedicado; las funciones de registro y conexión Steam que correspondan realmente implementadas.
- Lobby, invitaciones, autenticación, transporte y reconexión comprobados por Internet. Separa las limitaciones externas pendientes.
- Descripción corta y larga, lista exacta de funciones, capturas del juego real y material de presentación sin funciones inventadas.
- Requisitos mínimos/recomendados basados en pruebas, incluyendo requisitos y ancho de banda del anfitrión/servidor.
- Licencias, créditos y registro de recursos.
- Borrador de los cuestionarios y de la declaración de contenido generado con IA que corresponda a arte, textos, audio u otros contenidos entregados al jugador según la política vigente. No asumas que escribir código con IA determina por sí solo toda la declaración.
- Si incluyes logros, probar su atribución en cooperativo. Si incluyes Steam Cloud, definir propiedad y conflictos: el cliente no debe subir una copia incompleta del mundo remoto ni sobrescribir un guardado autoritativo del servidor.
- Lista final de decisiones que requieren al titular: cuenta, pagos, datos legales, precio, acceso de probadores y aprobación para hacer pública la tienda o lanzar.

No necesitas publicar en Steam para entregarme alfas locales jugables. No anuncies «listo para Steam» mientras falten pruebas importantes del cooperativo o del juego completo.

## 17. Primera acción que debes ejecutar

Empieza ahora con un diagnóstico breve del entorno y del modelo activo, los supuestos de producto y los tres riesgos principales: red/Steam para 12 jugadores, streaming con jugadores en distintos planetas y rendimiento de la simulación compartida.

Después:

1. Conserva este encargo e inicializa el proyecto sin sobrescribir nada existente.
2. Prepara el entorno mínimo y fija versiones compatibles.
3. Crea `CLAUDE.md`, un roadmap inicial y el estado de trabajo, de forma concisa.
4. Implementa y ejecuta el primer servidor y dos clientes compartiendo una acción autoritativa en una escena pequeña.
5. Continúa hacia el primer bucle jugable exportado de Plutón, con limpieza, producción, venta, mejora y guardado.
6. Mantén los informes y pasa al siguiente hito cuando el anterior tenga evidencia suficiente.

No me devuelvas únicamente un plan ni me preguntes si quiero que empieces. Quiero que empieces, construyas y me vayas entregando resultados jugables.

## Referencias oficiales de partida

Estas fuentes justifican las comprobaciones técnicas y de plataforma. Revisa la documentación de las versiones que instales; no copies APIs de otra versión ni des por mantenido un plugin por aparecer en una búsqueda.

- [Godot: C#/.NET](https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/)
- [Godot: optimización de CPU](https://docs.godotengine.org/en/stable/tutorials/performance/cpu_optimization.html)
- [Godot: optimización 3D](https://docs.godotengine.org/en/stable/tutorials/performance/optimizing_3d_performance.html)
- [Godot: carga en segundo plano](https://docs.godotengine.org/en/stable/tutorials/io/background_loading.html)
- [Godot: multijugador de alto nivel](https://docs.godotengine.org/en/stable/tutorials/networking/high_level_multiplayer.html)
- [Godot: exportación de servidores dedicados](https://docs.godotengine.org/en/stable/tutorials/export/exporting_for_dedicated_servers.html)
- [Steamworks: multijugador](https://partner.steamgames.com/doc/features/multiplayer)
- [Steamworks: Steam Networking](https://partner.steamgames.com/doc/features/multiplayer/networking)
- [Steamworks: Steam Datagram Relay](https://partner.steamgames.com/doc/features/multiplayer/steamdatagramrelay)
- [Steam Direct](https://partner.steamgames.com/steamdirect)
- [Steamworks: cuestionario de contenido e IA generativa](https://partner.steamgames.com/doc/gettingstarted/contentsurvey)
- [Claude Code: flujos de trabajo y continuación de sesiones](https://code.claude.com/docs/en/common-workflows)
- [Claude Code: subagentes](https://code.claude.com/docs/en/sub-agents)
- [Claude Code: consumo y costes](https://code.claude.com/docs/en/costs)
- [Anthropic: Claude Opus 5.5](https://www.anthropic.com/claude-opus-5-5)

