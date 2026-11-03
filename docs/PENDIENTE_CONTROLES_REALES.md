# Comprobaciones que necesitan teclado y ratón reales

Estas pruebas toman el foco y el ratón, así que solo se ejecutan con `tools/PlayDriver` cuando el titular no esté usando el equipo, o las hace una persona. Siempre con un perfil `test*`. El resto del trabajo no depende de ellas.

Lo que sigue sin marcar está **NO VERIFICADO** con entradas reales, aunque tenga comprobación automática. Una casilla marcada significa que un agente lo hizo con teclado y ratón reales de Windows, no que una persona lo haya valorado.

`agent-desktop` no cubre esta lista: no da teclas mantenidas ni ratón relativo (T-025).

## Última sesión

02-11-2026, con `tools/PlayDriver`, perfil `test-real`, a 1600×900 en el monitor secundario salvo donde se indica. Build `alpha-dev`, de código igual o anterior a `0.5.0-qa8`: los arreglos de esta sesión se fueron incorporando durante ella, así que no todo se repitió sobre el código final. El paquete `0.5.0-qa8` pasó después la regresión automática (T-029), no una repetición con entradas reales. Detalle en T-028; capturas en `artifacts/qa/0.5-real/`.

## Base

- [ ] Primera experiencia completa: de partida nueva a vender. Hecho solo el principio: partida nueva, mirar, andar y cortar con clic mantenido (+14 chatarra, 102 ¤, objetivo 1/3).
- [x] Escenario `fp` y bucle completo con captura del ratón: `RESULTADO: CORRECTO` en `alpha-dev`, antes de los arreglos de la sesión. Lo mueve el recorrido automático, no PlayDriver.
- [x] Cerrar el juego, reabrirlo y seguir la partida: créditos, máquinas, barra, objetivo, posición y escala de interfaz se conservan.
- [x] Provocar un fallo de energía: 35/30 en rojo en el HUD; el panel dice «Energía insuficiente» y da 6,9/min reales frente a 20 teóricos.

## Cintas y construcción

- [x] Retroceso quita el último punto; N cambia el ajuste a conectores.
- [x] R, M, clic de punto intermedio y Z.
- [x] Al tender una cinta desde una pieza grande, M recorre los conectores con el resaltado (INC-20).
- [x] Fantasma de pieza grande de cerca y de lejos, en capturas. Si molesta en movimiento lo decide una persona.
- [ ] Tender cintas hacia y desde un divisor y un unificador. Hecho **desde** un divisor hasta una fundición. **Hacia** una de esas piezas, solo por inserción.
- [x] Colocación consecutiva, con aviso de espacio ocupado, y desmontaje con devolución.
- [x] Insertar un divisor y un unificador sobre una cinta tendida. La primera y la última celda se rechazan con mensaje claro.
- [x] Botón «Mover» del panel: fantasma, rotar, confirmar y cancelar; las cintas se retrazan.

## Ferrocarril

- [x] Con la herramienta de vías, R recorre recta, curvas y señal.
- [x] Poner y quitar una señal, con la vista previa del coste y del estado.
- [x] Tender un ramal desde la mitad de una recta, con el desvío visible. En rectas de 10 m o menos no se puede: el encaje a 5 m lleva el punto a un extremo.
- [x] Tramo rechazado, tramo repetido y encaje al eje con ratón (INC-25, INC-26, INC-32). El texto nuevo de INC-26 tiene prueba de lógica, no se ha visto en pantalla.
- [ ] Una persona entiende por qué un tren espera ante una señal.

## Inventario y barra rápida

- [x] Mayús+clic, Ctrl+clic y clic. Desde el depósito, la mitad se limita a una pila de 500.
- [x] Fijar un objeto a la barra rápida: pasar por encima y pulsar el número.

## Gestión

- [x] Botón «Seguir cadena» del panel, con ratón.
- [ ] Rótulo del cuello de botella al girar la cámara (INC-14). En capturas queda bajo el objetivo sin meterse debajo. Si parpadea en movimiento no se puede juzgar con capturas.

## Herramienta y presentación

- [x] Multiherramienta a 1920×1080 en el recorrido `fp`, a 3440×1369 en el monitor ultrawide (ventana, no pantalla completa) y con FOV 101.
- [ ] Partículas al cortar hielo y roca. La partida de QA tiene mejoras que destruyen los bloques en menos de 0,25 s y ninguna captura las cogió.
- [x] Jugar con calidad baja y alta y con escala de interfaz 125 %.
- [x] Existencias en los puertos.
- [ ] Vapor en movimiento.
- [ ] Nueva expedición con P, el panel de confirmación y lo que se ve justo después del reinicio (solo con el recorrido automático `expedition`, T-030).
- [ ] Aviso de material a la venta en el panel de mejoras, en contratos y en la barra rápida (solo xUnit).
- [ ] Avatar de otro jugador caminando: la zancada en movimiento (solo capturas fijas en `coop-test`).
- [ ] «Unirse por IP» con una dirección mal escrita: debe avisar de que el servidor es inalcanzable (INC-35, solo xUnit).

## Solo una persona puede decidirlo

- [ ] Si el sonido se oye bien.
- [ ] Duración real de la campaña y comprensión de los objetivos.
- [ ] Si se entiende el aviso de falta de energía.
- [ ] Dos personas sobre la misma fábrica; dos PC; Internet; Steam entre cuentas.
