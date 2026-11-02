# Validación I22

Decisiones del usuario: 2-nov-2026, Ciudad de México.

- Compilación backend con SDK .NET 10.0.403 y .NET 10.0.14: correcta.
- Suite .NET: 413 pruebas (5 dominio y 408 aplicación/API/persistencia), sin omisiones. Incluye horario cliente/ruta, borrado/herencia, versión obsoleta, 18:00, martes, solicitud restringida por enlace vencido, rechazo/revocación, idempotencia, no duplicación y producción sólo tras aceptación.
- Compilación Angular de producción: correcta. Persisten avisos de tamaño de CSS de componentes anteriores; ningún error.
- Migración EF: generado script del incremento; `migrations has-pending-model-changes` no identifica diferencias. No se ejecutó este DDL contra SQL Server real en esta sesión.
- Suite Python: 417 pruebas, 413 correctas y cuatro omitidas por requisitos de entorno. Tres pruebas históricas I19 requieren ownership root; se identificaron explícitamente como tales, conservando los controles del instalador. La cuarta omisión es del conjunto previo.
- Pruebas I22 del paquete: ocho correctas, incluyendo exportación/importación del instalador, pines y hashes, rechazo de corrupción, migración única, respaldo verificado antes de DDL, historia/base exacta, permisos mínimos y fallo posterior a migración con API detenida.
- Nuevas pruebas Playwright: comprobación TypeScript sin errores. La ejecución visual no estuvo disponible: Chromium local fue bloqueado al crear un socket Unix por la política del entorno y el navegador remoto no pudo acceder a localhost. No se presentan como E2E aprobadas. Los tests quedan listos para ejecutarse en un runner con navegador.
- Entrega: actualización preparada para instalación por el usuario; no se accedió al VPS ni se modificó su BD.

## Verificación de negocio tras instalar

1. Configurar ruta a 10:00 y cliente a 09:00; comprobar aviso pendiente después de las 09:00.
2. Borrar hora del cliente; comprobar herencia 10:00. Borrar ruta: valor general 10:00.
3. Con enlace activo vencido recientemente, capturar solicitud pendiente; revocarlo y comprobar bloqueo.
4. Aceptar desde Por confirmar: aparece en producción una sola vez. Rechazar: no aparece y se prepara mensaje de propuesta de otra fecha.
5. Lunes desde las 18:00 y martes: fecha ofrecida miércoles, confirmada explícitamente antes de captura.
6. Abrir WhatsApp y revisar el mensaje; no se espera envío automático ni creación del pedido propuesto.
