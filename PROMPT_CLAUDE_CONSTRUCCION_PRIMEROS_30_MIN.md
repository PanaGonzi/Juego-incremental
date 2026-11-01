# Encargo posterior: construcción, gestión y primeros 30 minutos

Añade este encargo a la cola del proyecto. Primero termina e integra el trabajo actual de brazos, herramientas, inventario, cintas y gráficos. Después continúa con estas mejoras, sin esperar mi valoración. Conserva todos los requisitos anteriores, incluido el cooperativo para 12 jugadores.

Lee las instrucciones del proyecto y docs/HANDOFF.md, STATUS.md y ROADMAP.md. Comprueba qué existe realmente antes de implementar: mejora los sistemas actuales y evita crear versiones duplicadas.

## Objetivo

Conseguir que los primeros 30 minutos permitan entender, construir y mejorar una fábrica con comodidad, buena respuesta visual y decisiones interesantes. Prioriza una zona pequeña y cuidada. Delega las decisiones reversibles y documenta las importantes.

## 1. Construcción cómoda

Implementa o perfecciona la previsualización de máquinas y cintas antes de colocarlas, con rotación por pasos, alineación opcional y conexiones fáciles de seleccionar. Muestra el coste y explica por qué una colocación no es válida.

Permite colocar varias piezas consecutivas sin reabrir el menú. Las cintas deben mostrar claramente origen, destino y dirección. Añade divisores y unificadores si faltan y encajan con el sistema actual.

Desmontar debe ser preciso, mostrar lo que se recuperará y evitar borrar objetos cercanos por error. En cooperativo, valida costes, permisos y devoluciones en el servidor, sin duplicaciones.

## 2. Entender la fábrica de un vistazo

Al mirar una máquina, muestra su receta, progreso y estado: trabajando, sin energía, sin materiales o salida bloqueada. Diferencia capacidad teórica y producción real medida.

Permite seguir una cadena para encontrar su cuello de botella. Usa indicadores discretos, iconos y animaciones coherentes. Evita llenar la pantalla de texto y no dependas solo del color.

Los objetos de las cintas deben corresponder al transporte real, también cuando hay acumulación, desconexiones o entradas y salidas de jugadores.

## 3. Una progresión inicial satisfactoria

Revisa el recorrido desde recoger materiales con la herramienta hasta automatizar una cadena y comprar una mejora significativa.

Presenta objetivos breves y contextuales, mostrando para qué sirve lo que se construye. Evita esperas sin decisiones, viajes repetitivos innecesarios y situaciones donde gastar mal los recursos impida avanzar.

Comprueba los tiempos mediante recorridos reproducibles. Las pruebas automáticas pueden detectar bloqueos y esperas; no las presentes como demostración de que el juego resulta divertido.

## 4. Una zona inicial con personalidad

Termina una zona representativa de Plutón: terreno reconocible, instalación industrial, iluminación coherente, materiales diferenciados y referencias visuales para orientarse.

Cuida especialmente lo que se ve de cerca: manos, herramienta, máquinas, conexiones y productos. Añade respuesta sonora y visual a acciones importantes. La fábrica debe transmitir actividad y cambiar perceptiblemente al ampliarla.

Mantén legibles las interfaces y ofrece ajustes para reducir movimiento de cámara y efectos intensos.

## 5. Pruebas y rendimiento

Prueba la build exportada con controles normales: construir, conectar, cancelar, desmontar, llenar una salida, cortar energía, guardar y cargar. Comprueba también dos jugadores intentando interactuar con la misma construcción.

Captura pruebas visuales y registra fallos reproducibles. Separa lo observado en pantalla de lo comprobado mediante código o bots.

Compara rendimiento antes y después en condiciones equivalentes. Conserva el objetivo de 12 jugadores y ejecuta las regresiones relevantes cuando cambies sistemas compartidos.

## 6. Trabajo autónomo con consumo controlado

Usa como máximo dos subagentes simultáneos, con tareas delimitadas y sin delegación en cascada. Evita que varios agentes controlen el mismo escritorio.

No repitas lecturas completas, revisiones generales ni pruebas largas sin un cambio que lo justifique. Trabaja por lotes y verifica cada mejora con pruebas concretas.

Conserva la última alfa estable y entrega otra ejecutable cuando este conjunto esté integrado. Actualiza el roadmap, los problemas pendientes y las instrucciones para jugar.

Si encuentras un bloqueo que requiere mi intervención, documéntalo y continúa con una tarea independiente. Cuando completes este encargo, avanza por el roadmap existente según prioridad; no inventes trabajo infinito ni amplíes el alcance para mantenerte ocupado.

## 7. Prioridad alta: cintas largas y trazado automático fiable

Me frustra que las cintas se coloquen de una forma distinta a la que quiero. Este sistema debe ser cómodo, predecible y preciso.

### Longitud entre conectores

Elimina cualquier límite arbitrario de longitud que obligue al jugador a colocar conexiones intermedias. Quiero seleccionar un conector de salida y otro de entrada y construir una cinta continua entre ambos, siempre que existan una ruta válida y materiales suficientes.

Si el motor necesita dividirla en tramos internos para geometría, simulación, red o rendimiento, hazlo de forma transparente. El jugador debe manejarla como una sola conexión. Respeta los límites reales del mundo y explica cualquier restricción técnica inevitable.

### Trazado automático

Al seleccionar origen y destino, calcula una ruta que respete obstáculos, terreno, máquinas, estructuras y otras cintas. Considera la anchura y altura reales de la cinta y de los objetos transportados.

Prioriza recorridos comprensibles, con pocas curvas, cambios de altura suaves y un coste razonable. No elijas siempre el camino matemáticamente más corto si atraviesa zonas de paso o genera una construcción incómoda.

Respeta la orientación de los conectores, los radios de giro y las pendientes permitidas. Coloca soportes cuando sean necesarios y muestra su coste.

### Control del jugador

Ofrece trazado automático y puntos intermedios manuales para decidir por dónde debe pasar la cinta. Los puntos fijados por el jugador deben respetarse al recalcular.

Permite alternar entre rutas válidas cuando existan alternativas útiles. Muestra una previsualización completa, dirección de transporte, longitud y materiales necesarios antes de construir.

El ajuste a conectores debe ser estable: resalta claramente el seleccionado y permite cambiarlo o desactivar el ajuste automático. Evita saltos involuntarios entre conectores cercanos.

La construcción final debe coincidir con la última previsualización confirmada. No cambies silenciosamente el destino ni el recorrido al hacer clic. Si la ruta deja de ser válida, explica el problema y solicita una nueva confirmación dentro del juego, sin consumir materiales.

Permite cancelar, retirar el último punto y corregir el trazado sin empezar desde cero. Nunca coloques una ruta defectuosa como alternativa cuando no encuentres una válida.

### Rendimiento y cooperativo

Evita que calcular rutas largas congele el juego. El servidor debe validar el recorrido confirmado, los conectores, los permisos y el coste antes de construirlo, y transmitir el mismo resultado a todos los jugadores.

### Pruebas obligatorias

Verifica distancias largas, desniveles, obstáculos, conectores muy próximos, falta de materiales, cancelación y construcción simultánea de dos jugadores.

Comprueba visualmente que la cinta construida coincide con su previsualización y que los objetos recorren toda su longitud, conservando el trazado al guardar, cargar y reconectarse.
