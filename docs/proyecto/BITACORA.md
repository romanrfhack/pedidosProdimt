# Bitácora de trabajo

Cada entrada distingue hechos verificados, decisiones, pendientes y siguiente paso. Los resultados sólo se marcan como exitosos después de ejecutar la comprobación correspondiente.

## 2026-10-24 — Análisis y arranque de I01

### Entrada y decisiones

- Revisados dos libros de octubre, mensajes de WhatsApp y repositorios. El análisis produjo un plan integral por etapas.
- Usuario autorizó continuar, indicó que no hay datos operativos que conservar y eligió una ruta para el piloto.
- Nueva evidencia: fotografía de ruta (23 candidatos) y ticket histórico de otro cliente.
- Confirmadas reglas de llave y documento; desconocidos se conservan como tales.
- Se eligió `pedidosProdimt`, base `492bbe9e1069d32ae22565889f149aaf529393fd`, rama `codex/pilot-foundation-2026-10-24`.

### Trabajo

- Creado registro de continuidad en `CONTINUAR.md` y `docs/proyecto/`.
- Preparación privada del piloto separada del código público.
- Implementación I01 y pruebas en curso; el cierre de esta entrada registrará resultados concretos.


### Cierre de implementación I01

- Implementados catálogos, perfiles, snapshots, validaciones y vista administrativa de demanda/preparación descritos en `INCREMENTO-01.md`.
- Conservados desconocidos y separados datos internos de la vista del cliente.
- Corregido intercambio de posiciones del perfil mediante reemplazo transaccional, con prueba de rollback.
- Corregidas longitudes de snapshots para coincidir con el catálogo y evitar truncamiento en SQL Server.
- Revisiones duplicadas/obsoletas devuelven 409 y no vuelven a aplicar cambios.
- Añadido workflow `.github/workflows/ci.yml`, sin despliegue ni secretos.
- Telemetría de Angular deshabilitada; se ejecutó una alternativa segura después del bloqueo inicial del control automático.
- Elevadas tres dependencias NuGet afectadas a parches; evidencia en `../validation/2026-10-24-preflight.md`.

### Validaciones ejecutadas

- Base limpia en worktree separado: 68 pruebas .NET correctas.
- Incremento: 113 pruebas .NET correctas; build Release con cero errores y advertencias.
- Angular: build correcto. E2E: 23/23 con API mock y Chromium preinstalado; prueba visual específica posterior correcta.
- Inspección móvil e impresión con datos ficticios; `git diff --check` correcto.
- Revisión independiente de privacidad, contratos, transacciones y agrupaciones sin bloqueos nuevos.
- `dotnet ef migrations has-pending-model-changes`: sin cambios pendientes. SQL idempotente generado y revisado sin backfill de unidades.
- `graphify update .` no pudo ejecutarse porque la herramienta no está instalada; no existe grafo versionado que actualizar.
- No se ejecutó SQL Server ni se aplicó una migración a una instancia real. Ese paso queda explícito para I02.

### Persistencia y continuidad

- Código y documentación en la rama `codex/pilot-foundation-2026-10-24`; revisar la solicitud de cambios y su último commit antes de continuar.
- El XLSX privado de revisión y el ZIP con fuentes depuradas se guardaron y sus contenidos se verificaron. No forman parte del repositorio público.
- Próximo paso de negocio: validar el paquete privado; siguiente entrega técnica: I02 según `ESTADO.md`.

### Publicación comprobada

Implementación guardada en GitHub como `fde65007cb03614db0b1dcca11395a7451a374a5`, mediante [PR #1 en borrador](https://github.com/romanrfhack/pedidosProdimt/pull/1). Rama remota recuperada y comparada con el trabajo local; sin diferencias después de sincronizar. Los workflows remotos arrancaron; consultar su resultado en la PR. Este registro documental posterior no modifica el código probado.

### Cierre de hallazgos de CI y comprobación HTTP

- El primer CI remoto confirmó backend correcto y detectó una desincronización visual: al editar una cantidad, la aprobación interna quedaba false pero el checkbox podía seguir marcado. Se sustituyó ese control por `FormControl`, manteniendo la invalidación de negocio. El test comprueba tanto la pantalla como el siguiente PUT con aprobación false y perfil pendiente. Diez repeticiones del caso afectado correctas.
- Actualizados Angular/framework/compiler a 21.2.25 y CLI/build a 21.2.28, manteniendo TypeScript 5.9. Se añadió override acotado de undici 7.30.1 en herramientas de build. Auditoría npm completa: de 33 hallazgos a cero; producción: de seis a cero; E2E: cero. No se forzaron peers ni se cambió a Angular 22.
- Build frontend final: correcto, 423.30 kB. Suite completa posterior a los parches y corrección: 23/23 E2E correctos.
- Ejemplos de productos y scripts de comprobación incluyen unidades explícitas. Los smokes autenticados de captura e importación pasan contra una API real de desarrollo InMemory; comprueban kg/docena, snapshots, privacidad y plan protegido. Esa API temporal se detuvo al terminar.
- No hubo cambios nuevos de backend después de las 113 pruebas correctas. CI vuelve a comprobar ambos componentes sobre la versión publicada.
- El estado remoto final se conserva en los checks de la PR; no confundir las corridas anteriores fallidas con el resultado del último commit.

## 2026-10-24 — I02 autorizado tras revisión del XLSX

### Datos y alcance

- Usuario devolvió la hoja revisada, confirmó los valores detectados y autorizó el siguiente incremento.
- Revisión de sólo lectura: 23 clientes, 52 fórmulas simples con cachés correctos, ruta y acceso explícitos; dos instrucciones internas y cinco políticas de contraentrega. Productos y otros campos incompletos siguen separados de lo listo para cargar.
- Las fechas de revisión arrastradas no se utilizaron como fecha de servicio. Nada del archivo privado se publicó en git ni se envió al workflow.

### Implementación

- Fecha de servicio administrativa y consulta/revisión por fecha.
- Importación diaria CSV con preview, origen, referencias externas, clave de lote y resultados persistentes.
- Correcciones con motivo, versión y clave de operación; reemplazo completo y evidencia antes/después.
- Transacciones y restricciones SQL para evitar duplicados y cambios parciales; demo InMemory no simula esta garantía.
- Corregidos casos encontrados: rehidratación/reemplazo EF de líneas, colisión de claves compuestas, unidades históricas en la interfaz, falta de revisión por fecha, notas NoOrder perdidas, CSV malformado y marca UTF-8 de Excel.
- Añadido proyecto SQL Server separado, job CI con base desechable y script de ejecución local.

### Validación y continuidad

- 157/157 pruebas .NET y 37/37 E2E correctos; build Angular y revisión móvil correctos.
- Proyecto SQL de ocho pruebas compilado; falta acreditar ejecución remota en este checkpoint.
- Revisión independiente sin bloqueadores estáticos pendientes y comprobación de privacidad del diff.
- Paquete privado I02 con Excel recibido intacto, normalización, CSV parcial, evidencia de fórmulas y hashes persistido aparte.
- I02 no equivale a despliegue, carga de la ruta o salida de etapa 1. Siguiente: prueba SQL remota, instalación del piloto y preparación de I03.

### Cierre de SQL Server y publicación I02

- I02 publicado como `b73eb435eb7fc2d0bbb2a4c3240b59638a34972a`. Backend157 e interfaz37 correctos en GitHub; primera corrida SQL encontró dos fallos de concurrencia por envoltura de excepciones.
- Corrección publicada como `192c8d7871e2b2021b4d485dd71b5ebd7abbda4d`: reconocimiento de causas SQL envueltas y expansión de auditoría mediante `20261024181934_ExpandOrderAuditSnapshots`.
- [SQL Server real: 9/9 correctos](https://github.com/romanrfhack/pedidosProdimt/actions/runs/40120275407/job/120950109344). Confirmados deadlocks reales con 409, rollback total ante SQL 547, reintento persistente, auditoría extensa íntegra y migraciones consistentes. Contenedor desechable eliminado.
- CSV privado validado por HTTP: 23/23 filas, cero errores, 23 advertencias por teléfono vacío; no se aplicó. Paquete privado actualizado con esa evidencia y hashes.
- Documentación de estado/continuidad actualizada; rama remota recuperada y comparada con el trabajo local. No hay fusión, despliegue ni carga operativa. Próximo hito: instalación de prueba y ensayo I03 después de completar los datos mínimos faltantes.

## 2026-10-24 — I03-A, paquete de instalación autorizado

- Usuario indica «OK, adelante». Se continúa la preparación del siguiente incremento; se distinguen paquete técnico, instalación y ensayo operativo.
- Preparada instalación Linux/WSL con Docker Compose, frontend/API del mismo origen, SQL Express aislado y acceso inicial por loopback/túnel SSH. No se accedió al servidor ni se eligió un destino remoto por suposición.
- Production rechaza clave JWT demo, semillas de desarrollo y persistencia InMemory. Bootstrap explícito crea esquema, canales y administrador; arranque HTTP no ejecuta DDL.
- Credenciales únicas en el destino, configuración privada no ejecutada como shell, conexión SQL habitual sin permisos de esquema, respaldo verificado y rollback de imagen separado del esquema.
- Revisión independiente corrigió generación parcial de secretos, exclusiones en carpetas personalizadas, remapeo de usuario SQL al restaurar y unidad del producto ficticio del smoke.
- Hoja privada de pendientes con dos pestañas y 120 fórmulas; conserva los datos reconocidos y no aprueba perfiles ni inventa unidades/posiciones. Original intacto, auditoría XML contra fuente y revisión visual correctas.
- Validación local: 182 pruebas .NET, 37 E2E, build API y smoke HTTP Production. Cuatro pruebas adicionales SQL compiladas; ejecución real del stack pendiente del workflow en este checkpoint.
- La última corrida documental I02 había encontrado tres fallos E2E de habilitación de formulario. Se agregó sincronización explícita del estado inicial sin relajar aserciones, más trazas sólo ante fallo; no se afirma una causa no reproducida.
- Diff revisado contra nombres e instrucciones privados, sin coincidencias. Datos reales y kit privado permanecen fuera del repositorio público y CI.
- `graphify` sigue no disponible y no existe grafo versionado que actualizar. Los detalles y el resultado remoto se registran en `../validation/2026-10-24-i03.md`.
