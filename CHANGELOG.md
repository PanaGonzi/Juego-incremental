# Cambios

## 0.5.0 (en desarrollo; protocolo 10, guardado 7; migra 1 a 6)

Experiencia de fábrica: cintas con carga real, primera persona con inventario y barra rápida, construcción y gestión, señales y desvíos. Detalle por tarea en `docs/ROADMAP.md`. Tras `0.5.0-qa8`:

- **Nueva expedición** (prestigio opcional, B-7): tras el Proyecto Noveno Planeta, el propietario puede reiniciar la campaña desde el panel de jugadores, con confirmación del alcance. Cada expedición da +10 % al precio de venta y +300 ¤ al empezar, hasta 5. Se conservan jugadores, roles, cosméticos y eventos.
- El cliente reindexa solo lo que cambia y la vista repasa solo lo tocado: construir ya no recorre vías ni cintas, y cada delta ya no recorre toda la fábrica (H-4, H-6).
- La carga de las cintas no se vuelve a subir al motor si está quieta (H-8). Las vías tienen distancia de dibujo (B-4).
- Menos blancos quemados y halos: umbral del brillo más alto, lámparas de estado menos intensas y menos reflejo del foco (H-10).
- Los demás jugadores caminan: piernas y cuerpo animados según su velocidad (B-6).
- Corregido INC-34: el cliente podía perder un escombro llegado a la misma celda que otro retirado en el mismo delta.
- Servidor dedicado comprobado en Linux (musl, WSL2) con 12 clientes, con `tools/linux-test.ps1`. Necesita `libstdc++` y `libgcc` del sistema (B-5).
- Corregido INC-35: una dirección que no se resuelve en «Unirse por IP» provocaba una excepción en vez de avisar de que el servidor es inalcanzable.
- Corregido INC-36: el servidor dedicado ignoraba `--content` si no tenía `content` al lado.
- Los costes avisan si el material solo alcanza contando lo que la terminal va a vender (B-3).

## 0.4.1 (protocolo 4, guardado 4; compatible con 0.4.0)

Marcador en el mundo y distancia para el objetivo actual. Viento ambiente por planeta. La barra de herramientas se retraduce al cambiar de idioma.

## 0.4.0 (protocolo 4, guardado 4; migra 1, 2 y 3)

Marte jugable: silicio, obleas, núcleos de navegación, regla de calor con radiadores y radiador criogénico, plataformas de carga interplanetaria con envíos persistentes. Campaña completa hasta el Proyecto Noveno Planeta. Transferencia por tipo de objeto en los paneles. Avisos de licencias en el paquete.

## 0.3.0 (protocolo 3, guardado 3; migra 1 y 2)

Evento «El Expreso de las Sombras» por datos, con activación manual sin tocar el reloj y tren espectral permanente. Progreso sin conexión acotado a 8 h. Reserva de vías para varios trenes. Panel de jugadores con permisos y expulsión. Inglés, selector de idioma y controles reasignables. Transporte de Steam Networking Sockets implementado, sin integrar en el menú.

## 0.2.0 (protocolo 2, guardado 2; migra 1)

Ferrocarril: vías, estaciones, trenes, yacimientos ricos. Lanzadera y Marte mínimo como prueba técnica. Tráfico reducido a menos de la mitad. Iconos de estado sin tirones. **Corrige caída del servidor cuando dos jugadores terminaban el mismo escombro.** Aviso visible y guardado de emergencia si el servidor local falla.

## 0.1.0 — primera alfa (protocolo 1, guardado 1)

Plutón: limpieza con cortador, recicladora, extractores, fundición, ensambladora, cadena criogénica, enlaces locales, energía, depósito, terminal de exportación con venta automática y reservas, contratos, mejoras, objetivos. Modo individual sin red, anfitrión por IP, servidor dedicado para Windows, chat de texto, reconexión. Guardado manual, autosave y respaldo.
