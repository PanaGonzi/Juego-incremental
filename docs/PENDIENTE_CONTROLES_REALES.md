# Comprobaciones que necesitan teclado y ratón reales

Estas pruebas toman el foco y el ratón, así que solo se ejecutan con `tools/PlayDriver` cuando el titular no esté usando el equipo, o las hace una persona. Siempre con un perfil `test*`. El resto del trabajo no depende de ellas.

Hasta que se hagan, todo lo que aparece aquí está **NO VERIFICADO** con entradas reales, aunque tenga comprobación automática.

## Base: repetir sobre la última versión

La última sesión con entradas reales fue la de `0.5.0-qa2`.

- [ ] Primera experiencia: de partida nueva a vender, y hasta donde se llegue (en qa2, el objetivo 10 de 23).
- [ ] Escenario `fp` y bucle completo sin `-NoCapture`: captura del ratón y comprobaciones de entrada (`InputPaths`).
- [ ] Cerrar el juego, reabrirlo y seguir la partida.
- [ ] Provocar un fallo de energía y entender qué pasa.

## Cintas y construcción

- [ ] Retroceso quita el último punto; N cambia el ajuste a conectores.
- [ ] R, M, clic de punto intermedio y Z, de nuevo sobre esta versión.
- [ ] Al tender una cinta desde una pieza grande, M recorre los conectores con el resaltado nuevo (INC-20).
- [ ] Fantasma de pieza grande en movimiento, de cerca y de lejos: las marcas cambian de tamaño sin molestar (INC-20).
- [ ] Tender cintas hacia y desde un divisor y un unificador.
- [ ] Colocación consecutiva y desmontaje con ratón.
- [ ] Insertar un divisor y un unificador sobre una cinta tendida, apuntando con el ratón.
- [ ] Botón «Mover» del panel: fantasma, rotar, confirmar y cancelar.

## Ferrocarril

- [ ] Con la herramienta de vías, R recorre recta, curvas y señal.
- [ ] Poner y quitar una señal con el ratón, con la vista previa del coste y del estado.
- [ ] Tender un ramal desde la mitad de una recta.
- [ ] Una persona entiende por qué un tren espera ante una señal.

## Inventario y barra rápida

- [ ] Mayús+clic y Ctrl+clic.
- [ ] Fijar un objeto a la barra rápida.

## Gestión

- [ ] Botón «Seguir cadena» del panel, con ratón.
- [ ] Rótulo del cuello de botella al girar la cámara con el ratón: no parpadea y no se mete bajo el objetivo (INC-14).

## Herramienta y presentación

- [ ] Multiherramienta a 1080p y 3440×1440, y con otros FOV.
- [ ] Partículas al cortar hielo y roca.
- [ ] Calidad baja y alta; escala de interfaz distinta de 100.
- [ ] Vapor en movimiento y existencias en los puertos de la zona inicial.

## Solo una persona puede decidirlo

- [ ] Si el sonido se oye bien.
- [ ] Duración real de la campaña y comprensión de los objetivos.
- [ ] Dos personas sobre la misma fábrica; dos PC; Internet; Steam entre cuentas.
