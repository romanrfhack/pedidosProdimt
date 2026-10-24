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

### Cierre técnico I03-A

- Publicado `26eb6d8d47786bb18f2fe8117596e74f4f277430`; rama remota recuperada y comparada sin diferencias.
- [CI 40123481595](https://github.com/romanrfhack/pedidosProdimt/actions/runs/40123481595): cuatro jobs correctos, 182 pruebas backend, 13 SQL reales y 37 navegador. Modelo/migraciones consistentes.
- Smoke Docker Production completo correcto: inicialización, login, mismo origen, ausencia de demo, SQL con permisos limitados, secretos preservados, backup verificado y datos después de recrear la aplicación. Recursos desechables eliminados.
- Preparado kit privado con fuente exacta, CSV revisado, hoja de pendientes e instrucciones; los datos privados se colocan fuera del contexto Docker. No incluye secretos operativos.
- I03-B queda para ejecutar el paquete en el destino; I03-C para una semana real de comparación tras completar los datos mínimos. PR continúa en borrador, sin fusión ni despliegue.

## 2026-10-24 — I04 autorizado: correcciones visibles y configuración por cliente

- Usuario solicita que una modificación de la misma entrega se registre como corrección con nota, conserve historial y avise a la máquina aunque ya hubiera terminado de preparar. Confirma interés en captura asistida desde WhatsApp y control de duplicados entre puntos de entrada.
- Se reutiliza la corrección I02. I04 guarda avisos persistentes junto con pedido/auditoría y añade reconocimiento idempotente; el total solicitado y lo físicamente producido permanecen separados.
- Configuración enfocada por cliente sobre el catálogo existente, orden visual y posiciones explícitas. Snapshot coherente de perfil/productos, versión esperada y unidad observada evitan sobrescrituras/aprobaciones con datos obsoletos.
- Revisión corrigió carreras entre modificación de producto y aprobación de un perfil nuevo; las transacciones cubren validación y escritura. El tablero lee plan y avisos con control de versión para evitar mostrar total anterior junto con aviso nuevo.
- Recibido XLSX revisado: 67 entradas modificadas (30 unidades, 36 posiciones y un cero de máquina), sin cambios en filas ni fórmulas. Persisten faltantes; no se aprueban perfiles o fusionan productos automáticamente.
- El término «Adicional» de la evidencia original se reconoce como rótulo impreciso, con significado pendiente. No se transforma la hipótesis de ampliaciones telefónicas en datos operativos históricos.
- Destino y subdominio elegidos. Preparados inventario readonly, instalación aislada y Nginx/HTTPS por etapas. No se ejecutó despliegue porque no hay ruta SSH desde el entorno; el usuario ejecutará el primer paso desde WSL.
- Validación local y controles remotos se registran en `../validation/2026-10-24-i04.md`. El código público sigue separado de clientes, archivos e instrucciones privados.

- Cierre local I04: 203 pruebas .NET, 53 E2E y 15 pruebas Python correctas; 20 SQL compiladas, pendientes de ejecución remota. `graphify update .` intentado, herramienta no instalada (127).

### Cierre técnico I04

- Publicado `b630072ce67a691c3757d0590cc7f7d45f423d52`; rama remota recuperada y comparada sin diferencias.
- [CI 40130113089](https://github.com/romanrfhack/pedidosProdimt/actions/runs/40130113089): cuatro jobs correctos, 203 pruebas .NET, 20 SQL Server reales, 53 E2E y 15 Python. Modelo/migración consistentes.
- Smoke Docker Production correcto incluso con configuración extraída con permisos 600; bootstrap, permisos SQL limitados, respaldo y persistencia después de recrear aplicación verificados. Recursos de prueba eliminados.
- Revisión independiente y diff sin datos privados. Preparado paquete I04 con fuente exacta, revisión privada y workbook original intacto; sin secretos operativos.
- PR sigue en borrador y main sin cambios. Próximo paso: JSON del inventario de sólo lectura en el VPS; instalación, HTTPS, carga real y ensayo aún pendientes.

## 2026-10-24 — Inventario del VPS recibido

- El usuario ejecutó el preflight de sólo lectura y compartió su JSON. Confirma Ubuntu x86-64, Docker operativo, resolución IPv4 elegida y espacio disponible; el puerto previsto del nuevo stack está libre.
- `compose_version: null` indica que la consulta de versión falló, no prueba por sí sola que Compose esté ausente. Falta inspeccionar salida de error y paquetes instalados/candidatos.
- La RAM disponible está por debajo del umbral previsto para la compilación inicial y no hay swap. Se revisará carga actual y opción de compilar fuera del VPS; no se cambian límites ni se detienen otras aplicaciones por suposición.
- Existe un virtual host para el dominio con nombre distinto del gestionado por I04, y un certificado previo. Faltan upstream, vigencia y método de renovación; la existencia del archivo no acredita servicio válido. No ejecutar el publicador genérico ni crear otro virtual host antes de revisar esta evidencia.
- Preparado siguiente diagnóstico dirigido de sólo lectura. No se instaló ni cambió nada remotamente; código funcional I04 conserva la validación registrada.

## 2026-10-24 — Despliegue nativo existente identificado

- El diagnóstico dirigido del usuario confirma servicio PRODIMT activo, frontend/backend con releases, proxy a su API y certificado vigente con renovación webroot. Compose v1 existe, pero faltan los plugins v2/Buildx y WSL no tiene Docker.
- Se recuperó el antecedente de la instalación: SQL Server nativo y base propios ya usados por PRODIMT. Se requiere lectura actual de configuración y migraciones; no se publica ni se solicita ninguna contraseña.
- Se prepara I03-B como actualización nativa de esa instalación: compilar API/Angular fuera del VPS, inspeccionar configuración efectiva/estado SQL, respaldar/verificar, migrar y cambiar releases conservando infraestructura. No crear otro SQL ni otro virtual host por defecto.
- Binarios preparados desde el commit funcional probado b630072; no se modificó código de la aplicación ni se inició la actualización remota. La inspección nueva es de sólo lectura y sus resultados son condición de la fase de escritura.

## 2026-10-24 — Inspección nativa compatible; actualización preparada

- El usuario ejecutó el inspector del kit nativo y compartió su JSON. Configuración Production y credenciales de aplicación compatibles; runtime 10.0.13, base con cinco migraciones, cero pedidos y cero duplicados de secuencia. Backend/frontend activos corresponden a releases anteriores a I04.
- La identidad de aplicación está correctamente limitada: sin DDL, BACKUP ni permisos de verificación. La actualización debe usar una identidad administrativa existente, introducida mediante terminal oculta y nunca pegada o almacenada en el chat. El antecedente de junio usó sa con prompt privado; no hay un archivo administrativo conocido que reutilizar automáticamente.
- Preparado actualizador para el payload nativo exacto: validar configuración de reinicio, respaldar/verificar, aplicar cuatro migraciones y reemplazar enlaces con ventana breve. Mantener la cuenta cotidiana sin DDL; permisos de datos adicionales se limitan a los objetos nuevos si hacen falta.
- Se añade prueba SQL real del salto5→9 y respaldo, preservación de cliente/administrador/hash/estados/SID y DML sin DDL; compilación SQL sin advertencias, ejecución remota pendiente del workflow. 71 pruebas Python correctas (41 del actualizador), con escenarios de ciclo/fallos, señales, preservación y health; dos revisiones cerradas sin bloqueadores. Resultados remotos y límites en `../validation/2026-10-24-native-update.md`.
- Ninguna escritura ni reinicio en el VPS desde esta sesión. La aplicación y el paquete nativo conservan el código funcional probado b630072.
