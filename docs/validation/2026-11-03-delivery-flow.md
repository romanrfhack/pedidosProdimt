# RF1 — Integración de acceso y preparación de reparto

Fecha: 3 de noviembre de 2026.

## Evidencia de partida

- I22 consta instalado por el reporte del usuario: `delivery-0cfe3ef511d8`, 17 migraciones, salud/frontend verificados, repetición `already_current` sin cambios.
- La nueva captura muestra Pedidos sin enlace de reparto, con ocho pendientes de revisión y quince NoOrder. No acredita permisos de administración ni pedidos elegibles.
- Consulta HTTPS independiente del índice público: SHA256 `3f067596a66a8fdd7b4c2ad9fb8acc71fec9247b76742ba71c0430c7e04375a4`, idéntico al `frontend/index.html` del payload I22. No se consultaron datos internos con credenciales ni se operó el VPS.
- Comprobación HTTPS de salud200/BD `reachable`; cuatro GET anónimos de clientes, próximo pedido y reparto propio/gestión devolvieron401, incluidos los endpoints exactos usados por RF1.
- La revisión de código confirma elegibilidad `Submitted`/`Accepted`, permisos propios separados de gestión y creación explícita de rutas/vueltas/reservas. Los pedidos pendientes no deben aceptarse automáticamente.

## Verificación local

El cambio se limita a Angular, pruebas E2E y herramientas/documentación de entrega. No cambia backend, migraciones, SQL, ledger, reglas de aprobación o concesiones de acceso.

La compilación Angular de producción pasó: 494.67kB inicial, 65.92kB reparto lazy y 6.77kB guía lazy. No se elevaron presupuestos; permanecen avisos CSS históricos. Cierre de imports del build real:32 archivos, incluida licencia Angular del directorio superior incorporada en memoria por el constructor. La revisión independiente comprobó que la guía sólo consulta permisos propios, que las pantallas operativas conservan guardas y que los enlaces respetan la fecha. Las cantidades no se agregan entre unidades. Tres escenarios del método de protección de navegación (normal/corrección/sesión cerrada) se comprobaron sin navegador.

Las pruebas Playwright de integración y reparto existente se colectan y verifican con TypeScript estricto:22 casos (once nuevos, once previos). Cubren permisos/guía, fecha, revisión, configuración, rol canónico aditivo, corrección y salida de sesión; anchos320/768/1440. Se intentó ejecutar la versión inicial de20 casos con `/usr/bin/chromium`: el proceso abortó en `process_singleton_posix.cc` al crear un socket (`Operation not permitted`, SIGABRT), antes de iniciar assertions. El runner reportó un fallo de lanzamiento y19 casos sin ejecutar. No se reintentó el navegador tras el bloqueo. No se afirma cobertura de navegador, capturas revisadas ni aceptación visual.

El actualizador RF1 requiere ambos releases I22 exactos, las cuatro DLL propias y el conjunto completo del frontend base. Comprueba salud y autorización anónima de endpoints existentes; no consulta historia SQL. Rechaza recuperaciones pendientes y cambios concurrentes. Pasaron90 pruebas Python de reparto, incluidas20 nuevas RF1. El ensayo RF1 usa archivos/datos sintéticos y transporte HTTP simulado, con activación atómica, repetición, fallo de assets, interrupción, preservación de enlace ajeno y recuperación; no es una instalación real.

El procedimiento de cierre exige verificar el ZIP final con CRC, SHA256, manifiesto, cierre de imports y cargador exportado, además de restaurar el bundle autosuficiente en un checkout independiente. El registro de entrega conserva la fuente y los hashes concretos del paquete resultante.

## Límites y validación pendiente

Sin conexión SSH desde este agente ni instalación RF1 realizada. No se asignaron usuarios, aprobaron pedidos, crearon rutas/vueltas ni registraron kilos o entregas reales. Falta ejecutar el paquete en el destino y validar el flujo con una cuenta autorizada y la fecha correcta.

El trabajo paralelo I23 Horarios/Confirmación parte de I22 y puede cambiar backend/esquema/frontend. RF1 no lo sustituye: si existe ese u otro release distinto, el preflight debe bloquearse. No ampliar pines ni ejecutar instaladores anteriores para eludirlo; se requiere integrar fuentes y preparar otra entrega.

La fuente se entrega por el bundle existente sin publicar GitHub. No se actualizó el grafo Graphify porque no están disponibles ni herramienta ni grafo en este entorno.
