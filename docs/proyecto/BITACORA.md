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

## 2026-10-25 — Bloqueo de formato EnvironmentFile antes de actualizar

- Recibido JSON del primer `--apply`: `environment_file_format_unsupported` en `read-only-preflight`, sin cambios ni posible DDL. La normalización estructural sólo figuraba como plan. No hubo respaldo/migración ni activación I04 en ese intento.
- Sustituido el lector reducido por estados compatibles con systemd 255; comillas de varias líneas, escapes/continuaciones, transiciones de comillas, espacios/CR/LF, asignaciones repetidas e ignoradas. No se ejecuta shell ni se modifica el archivo privado. La construcción concreta del archivo del VPS que activó el fallo sigue sin observarse.
- Añadida identificación `updater_revision: 20261025-env2` y diagnóstico de contadores/estados/motivos fijos sin claves ni valores. Se conserva la igualdad de configuración crítica activa/reinicio/candidata y el bloqueo anterior a preparación/parada.
- `python3 -m unittest discover -s tests/pilot -q`: 76/76 correctas, incluidas 46 del actualizador, sin omisiones locales; `git diff --check` correcto. 22 fixtures ficticios contrastados con `load_env_file` de systemd 255 real y dos pruebas de integración de configuración coincidente/conflictiva. Revisión independiente sin bloqueadores y comparación adicional de 12,000 casos ficticios sin diferencias. `graphify update .` intentado, comando no disponible (127).
- Se reemplaza el mismo archivo descargable del actualizador; ZIP, código funcional y SQL permanecen intactos. Próximo paso: el usuario sustituye sólo el script y repite con el mismo paquete. Hasta recibir `status: updated` y revisar su verificación, no se considera completado el despliegue. Evidencia en `../validation/2026-10-25-native-env.md`.
- CI a8df45e: tres jobs correctos (76 Python con oráculo real, smoke Production, 21 SQL, 203 .NET). E2E 52/53: la fecha fija 25 de octubre coincidió con hoy y el botón cambió correctamente a «No pedir hoy», mientras el test esperaba «No pide en esta fecha». Se fija el reloj de ese test al 24 de octubre y se conservan selección/aserciones; aplicación y script sin cambios. `git diff --check` correcto; la comprobación E2E remota queda en el nuevo workflow, sin instalar dependencias innecesarias en el entorno recuperado.

## 2026-10-25 — Prompt administrativo validado con terminal real

- Recibido segundo JSON del usuario: `env2` supera lectura de configuración/preflight y llega a autenticación SQL. Falla con `apply_requires_interactive_tty`, `changes_made: false` y `migrations_may_have_changed: false`; no llegó a pedir ni comprobar credenciales SQL, preparar releases o detener la aplicación.
- Causa reproducida con PTY real/Python 3.12: abrir `/dev/tty` con `r+` usa un buffer que exige seekability y lanza `io.UnsupportedOperation`. La captura genérica lo clasificaba como ausencia de terminal. Los mocks anteriores no detectaban el defecto. Forzar más la asignación SSH no cambia esa causa.
- Corrección mínima: salida de prompts en modo `w`, con UTF-8; getpass abre su entrada raw, desactiva eco y restaura terminal. Se mantienen isatty y advertencia como error para impedir fallback inseguro. Revisión `20261025-tty3`; sin cambios de aplicación, SQL, payload o credenciales.
- Cinco regresiones sin mocks de open/getpass: éxito y rechazo sin eco, EOF con restauración, falta de terminal sin leer stdin y reproducción de r+. `python3 -m unittest discover -s tests/pilot -q`: 81/81 correctas; `git diff --check` correcto. Revisión independiente sin bloqueadores. `graphify update .`: no disponible (127).
- El CI final anterior [40199617322](https://github.com/romanrfhack/pedidosProdimt/actions/runs/40199617322) completó cuatro jobs correctos (76 Python, 53 E2E, 21 SQL, 203 .NET y smoke Production), incluida la fixture de fecha corregida. El cambio de terminal se verifica en el nuevo workflow. Se reemplaza el mismo actualizador descargable; próximo paso: repetir con terminal SSH y el ZIP remoto existente. No se considera desplegado I04 hasta revisar el resultado remoto.

## 2026-10-25 — Respaldo verificado, fallo de migración y recuperación de aplicación

- El usuario ejecutó `tty3`: prompts ocultos y preflight administrativo correctos; snapshot, normalización estructural y staging completados, aplicación detenida y respaldo COPY_ONLY/CHECKSUM verificado. Falla `sql_operation_failed` en `database-migrations`, con posible DDL y cambios efectuados. La reversión confirmó ambos enlaces, arranque y salud de la versión anterior; no hay errores de recuperación y no se restauró SQL.
- Los releases candidatos permanecen preparados y el historial actual se desconoce. Se detiene el flujo de reintento automático: diagnóstico fijo de sólo lectura antes de definir una recuperación. El mensaje SQL original se descartó; no se puede recuperar de un log que el script no guardó.
- Revisión encontró diferencia entre SqlClient de las pruebas y ODBC sqlcmd del actualizador: falta `-I` y el prologue no fija las opciones requeridas para índices filtrados. Causa probable, no todavía error original comprobado del VPS. Se prepara protocolo explícito con las siete opciones y sólo códigos numéricos de futuros errores, sin mensajes/credenciales.
- Nuevo diagnóstico autónomo valida el ZIP exacto y consulta con la cuenta cotidiana, sin prompt, escritura, parada, migración ni restauración. Distingue prefijos de cinco a nueve migraciones, desconocidos y metadatos no visibles; salida de enums/números/IDs conocidos. Doce pruebas específicas correctas y revisión independiente sin bloqueadores; suite Python 94/94 correcta. `git diff --check` correcto; graphify no disponible (127).
- Nueva prueba SQL real por el mismo transporte sqlcmd/stdin/GO: reproduce configuración anterior/índice filtrado, aplica salto cinco→nueve y repetición, conserva datos ficticios y confirma opciones en la misma conexión. Se incorpora al job SQL existente; ejecución remota pendiente del nuevo workflow. El actualizador distribuido sigue siendo `tty3`; no se publica una nueva aplicación hasta revisar el diagnóstico del servidor. Evidencia en `../validation/2026-10-25-native-sql.md`.

## 2026-10-25 — Diagnóstico recibido y reanudación acotada

- Diagnóstico del usuario sin cambios: versión anterior activa, salud/base 200, staging coincidente con probes del ZIP y respaldo previo regular de 4,872,704 bytes/mode 600. Cinco migraciones exactas, ninguna desconocida y cero pedidos. ODBC sqlcmd 17.10 con QUOTED_IDENTIFIER=0/ARITHABORT=0, compatibilidad 160; condición problemática reproducida con el mismo tipo de cliente. La cuenta cotidiana no tiene VIEW DEFINITION; no usar sus null para descartar DDL parcial.
- [CI 40203099261](https://github.com/romanrfhack/pedidosProdimt/actions/runs/40203099261) cerró los cuatro jobs: 94 Python, 203 .NET, 21 SQL, 53 E2E y smoke Production correctos. El contrato por CLI real reprodujo error 1934 y aplicó cinco→nueve dos veces conservando datos ficticios.
- Preparado `--resume-from` sólo para el snapshot concreto revisado. Valida archivos privados sin symlinks/hardlinks, procedencia del fallo/recuperación, env/enlaces/unidad estática y bytes/permisos/conjunto completo de staging; ignora únicamente PID/timestamps operativos al comparar ExecStart anterior.
- Con credenciales administrativas ocultas valida cinco migraciones y esquema previo visible (13 tablas, 23 columnas nuevas ausentes, índices/FK nuevos ausentes, índice anterior y auditoría originales). Verifica de nuevo el respaldo previo y conserva su archivo. Genera snapshot y respaldo frescos, sin recrear staging, revalida tras la parada y antes de DDL/activación y mantiene la recuperación anterior bajo el mismo lock.
- La prueba CLI real amplía el ensayo: script tty3 completo falla y revierte a cinco; se rechazan columna parcial, índice previo ausente, auditoría modificada, falta de visibilidad y esquema de nueve migraciones. Comprobaciones locales, revisión y resultados del nuevo workflow acompañan la distribución. No hubo nueva escritura remota desde este entorno; se necesita el JSON final del usuario. Evidencia en `../validation/2026-10-25-native-resume.md`.
- Validación local de reanudación: 108/108 Python, incluidas catorce nuevas con filesystem real y ciclo de ejecución; sin omisiones. Diff correcto y revisión independiente de esquema/protocolo/flujo. graphify intentado, comando no disponible (127); la prueba SQL ampliada espera el nuevo workflow para su ejecución real.
- CI 40205493674: SQL real (21 pruebas más contrato ampliado por CLI), .NET y navegador correctos; reproduce fallo tty3 completo y rollback íntegro a cinco, y rechaza DDL parcial/falta de visibilidad antes de migrar correctamente. Dos fixtures de Python asumían ejecución root del runner: se añade simulación acotada sólo a esos preflight tests. 108 locales correctas y catorce de reanudación también correctas simulando geteuid global no privilegiado. Código productivo y bytes del script sin cambios; el siguiente workflow verifica el ajuste de fixtures.

## 2026-10-25 — I04 desplegado en el VPS y verificado

- El usuario comprobó el SHA256 `cd3489971c335b86e16cc2c29415dc4ff83e27b69171bf4d53a3d96217b7cd48`, copió el actualizador y ejecutó `20261025-resume5 --resume-from ... --apply` con el ZIP original. JSON final `status: updated`, `phase: completed`, `changes_made: true`; migraciones 5→9, esquema, permisos DML y salud correctos. No se realizó SSH directo desde este entorno; se registra la evidencia compartida por el usuario.
- Procedencia/staging/esquema administrativo y salud anterior verificados; configuración privada conservada. Se reverificó el respaldo previo y se creó otro independiente en `/var/opt/mssql/backup/prodimt-i04-20261025T160429Z-1f5a37e4cab5/ProdimtPedidos-copy-only.bak`; snapshot nuevo correspondiente en `/var/backups/prodimt-pedidos/`. No se restauró automáticamente SQL.
- Ambos releases I04 `20261024-i04-b630072` activos. La verificación comprueba proceso nuevo, base accesible, endpoints protegidos y bytes de frontend por HTTPS. La consulta inicial de salud no requirió proxy HTTPS; ese flag no invalida las comprobaciones HTTPS posteriores.
- Consultado CI final [40205783899](https://github.com/romanrfhack/pedidosProdimt/actions/runs/40205783899): cuatro jobs correctos, 108 Python, 21 SQL, 203 .NET y 53 E2E, contrato sqlcmd y smoke Production/persistencia/respaldo. No se repiten tests por un cambio exclusivamente documental.
- Actualizados continuidad, estado, guía nativa y evidencia; PR permanece en borrador y main sin fusión. Próximo paso: acceso de administrador desde el navegador, conciliación de catálogos existentes y vista previa privada del Excel revisado. La actualización no aplicó esa carga ni completó el ensayo de una semana.

## 2026-10-25 — Recuperación puntual de acceso web solicitada

- El usuario compartió login web inválido y consulta de AdminUsers: `admin` existe y está activo. Autoriza crear cuenta o restablecer contraseña y probar login. Se elige restablecer sólo la contraseña de la cuenta existente; no se reproduce su captura ni sus hashes en git.
- Nuevo `reset-native-admin.py` para el servicio/release I04 y destino SQL revisados. Credenciales SQL existentes y nueva contraseña web repetida mediante PTY sin eco; no recibe contraseñas en flags, shell ni archivos. Hash Identity V3 interoperable con el servicio PasswordHashService desplegado. El estado anterior, incluido su hash, se conserva sólo en respaldo privado root/600.
- UPDATE de una fila por Id, nombre, estado activo y hash anterior exacto, con auditoría System dentro de la misma transacción. Rechaza cambios concurrentes; no activa/crea cuentas ni modifica SQL logins, permisos, configuración o releases. No reinicia servicio ni realiza bootstrap/migraciones.
- Prueba login por HTTPS y lectura protegida con JWT en stdin, sin imprimir respuesta/token. Si falla después del reset, su estado conserva que hubo escritura y la referencia de operación; no revierte credenciales automáticamente ni finge éxito de login.
- 120/120 pruebas Python locales correctas, doce nuevas de recuperación (formato/sal aleatoria, PTY real, transporte privado, cuenta/destino, respaldo, orden de pasos y fallo HTTP). Nueva prueba .NET de interoperabilidad con contraseñas ficticias Unicode; contrato sqlcmd real amplía caso de reset, rechazo de hash obsoleto y rollback ante fallo de auditoría. Se afina el adaptador de CI para ejecutar el transporte propio del resetter (incluido ancho de salida JSON), en lugar del transporte de migración que no devuelve JSON por stdin; el script entregable conserva sus bytes. Resultados remotos se registrarán en la evidencia antes de entregar. graphify no disponible (127). Todavía no se ejecuta recuperación en el VPS.
- [CI final 40213743976](https://github.com/romanrfhack/pedidosProdimt/actions/runs/40213743976), `fe0f19d`: cuatro jobs correctos, 120 Python, 205 .NET (dos nuevas de interoperabilidad), 21 SQL, 53 E2E y smoke Production/persistencia/respaldo. Reset, rechazo de hash obsoleto y rollback de contraseña ante fallo de auditoría probados con la CLI real. El adaptador inicial falló al leer JSON con el ancho predeterminado del cliente de migración; se corrigió sólo la prueba para usar el transporte propio. Entregable de 15,670 bytes/SHA256 `5072118445363332090066bae939ec3977539a95a997008a02f1dc52e7111a15`, guardado y verificado. Pendiente ejecución por el usuario; no se afirman credenciales web recuperadas todavía.

## 2026-10-25 — Reset detenido sin cambios y ruta duplicada en navegador

- JSON recibido: confirmaciones distintas, fase `new-web-password`, sin escritura ni reset. Contraseña anterior vigente. Nueva captura 404 de `/api/api/auth/admin/login`; se reconoce diagnóstico inicial prematuro y se pausa el reset.
- HTTPS público comprobado desde este entorno: ruta correcta responde 401 con identidad ficticia inexistente; doble prefijo devuelve 404. HTML actual referencia `main-ZDENE75O.js`, cuyo contenido publicado tiene apiBaseUrl vacío y un solo prefijo, como Production I04. Hashes/bytes en la evidencia. No se usan contraseñas reales ni SSH.
- La diferencia apunta a caché del navegador, pendiente de confirmación tras recarga sin caché/incógnito y revisión de Network. Indicar esos pasos y probar contraseña anterior antes de reset. No se modifican código, configuración, servicio o SQL del VPS. Diff documental comprobado; se conserva CI técnico anterior correcto sin repetir suites.

## 2026-10-26 — Nueva cuenta con contraseña generada y prueba de acceso

- El usuario confirma ruta correcta y muestra 401; pide cuenta nueva, contraseña generada y pruebas. Una vez que confirme acceso se desarrollará cambio de contraseña propia. Se respeta ese orden, sin iniciar todavía la modificación de la aplicación.
- Recuperada la rama remota tras mantenimiento del workspace; HEAD bb56a34, PR en borrador y main sin fusión. SSH directo vuelve a fallar por falta de ruta de red; la ejecución real se prepara para WSL, sin solicitar secretos en el chat.
- Nuevo `create-native-admin.py` para `roman.prodimt`, con módulo anterior validado por hash exacto. Genera 32 caracteres aleatorios en el VPS y guarda referencia privada root/600 antes de DML. Alta/auditoría System atómicas; reintento sólo si Id/hash/aviso de creación coinciden con la operación guardada. No adopta usuarios anteriores ni los reactiva/edita.
- Comprueba fila creada, login y lectura administrativa HTTPS, con contraseña/JWT por stdin. Sólo entrega contraseña en TTY tras el éxito; el JSON de diagnóstico excluye secretos. Un fallo conserva referencia privada y posible escritura. No hay reinicio, cambio de permisos SQL, bootstrap ni migraciones.
- 129/129 pruebas Python locales correctas, nueve nuevas de generación/registro privado/reintento/alcance/fallos. Contrato SQL real ampliado: alta, repetición sin duplicados/rotación, rechazo de cuenta ajena/inactiva y rollback ante fallo de auditoría. Ejecución CI pendiente antes de entregar. Diff correcto; graphify no disponible (127). No se ha creado aún la cuenta en el VPS.
- Primera corrida 40403021165: Python/Production y .NET correctos; la CLI SQL detecta que SQL Server omite la propiedad JSON users cuando no hay filas, en vez de devolver siempre []. Se normaliza sólo esa ausencia después de validar la consulta fija/base/historial, manteniendo el rechazo de null/tipos inválidos; se verifica también el caso inicial de cuenta inexistente en SQL real. El módulo anterior fijado por hash sigue intacto; la nueva herramienta se vuelve a validar antes de distribuir.
- [CI 40403547948](https://github.com/romanrfhack/pedidosProdimt/actions/runs/40403547948), d75c4de: SQL, .NET y Production correctos (129 Python, 21 SQL, 205 .NET más contrato CLI/Production/persistencia/respaldo). Navegador aún en ejecución; no cambió frontend/aplicación. La CLI confirma cuenta inexistente, alta/reintento sin duplicados ni rotación, preservación, rechazo de adopción/reactivación y rollback por auditoría. Guardado entregable exacto de 12,792 bytes/SHA256 `e0de73de480193318f802ad2561ead35ecc581deaec72b875c968ee6abd04908`; la dependencia previa conserva hash 50721184. Falta ejecución en VPS y confirmación de acceso; no se crea una cuenta por pasar CI.

## 2026-10-26 — Cuenta creada y cambio de contraseña propia I05

- JSON del usuario confirma nueva cuenta creada, fila/auditoría verificadas y login/GET administrativo HTTP 200. Acceso disponible; sus datos de contraseña no se copian a archivos públicos, código, logs o fixtures.
- Se inicia I05 ya autorizado: POST protegido para la cuenta del JWT, contraseña actual/nueva/confirmación, 16–128 caracteres, hash distinto y auditoría sin secretos. UPDATE condicional del hash observado y auditoría en una transacción evitan carreras/escritura parcial; no cambia esquema.
- Sesiones administrativas ligadas al hash mediante HMAC firmado: un cambio de contraseña o desactivación invalida sesiones previas. La pantalla cierra sesión y pide login nuevo. Tokens administrativos antiguos sin sello requieren renovar sesión al instalar; clientes conservan su flujo.
- Build Angular local correcto y 212 .NET locales correctas (5 dominio, 207 aplicación); SQL real y 55 E2E pendientes de CI. Descarga local de Chromium falla por archivo truncado del proveedor; se usa navegador del workflow. Paquete nativo sin migraciones en preparación. No se ha actualizado todavía el VPS con I05.
- CI funcional 40409593142 de 304a0cd cierra los cuatro jobs correctamente: 212 .NET, 24 SQL Server reales, 55 E2E y 129 Python/Production. Verifica cambio persistente entre hosts, rechazo de contraseña/sesiones previas, otras cuentas/cliente preservados, concurrencia con un ganador/auditoría y rollback ante fallo de auditoría; modelo sin migraciones nuevas.
- Preparado empaquetado de binarios externos e instalador nativo acotado al I04 activo: ZIP/manifiesto fijados, configuración efectiva/permiso SQL/historial nueve de sólo lectura, snapshot privado, staging verificable y rollback de aplicación. Conserva archivos de configuración con root:www-data/640; no cambia Nginx, datos ni contraseñas al instalar. 135 Python locales correctas, seis nuevas de integridad/empaquetado/activación/reversión; graphify no disponible. Sigue pendiente ejecución de I05 en el VPS.

- Cierre del paquete I05: código `886bee7b2202b399ee063fe65d524b7f444e205c`, CI 40411669397 con cuatro jobs correctos, 212 .NET, 24 SQL, 55 E2E y 135 Python/Production. Nombres del instalador/payload específicos de PRODIMT para no sobrescribir archivos genéricos de otros proyectos en `/root/`.
- Artefacto `PRODIMT_Cambio_Contrasena_I05.zip` guardado/verificado: 5,591,118 bytes, SHA256 `03a993f8d07691ed1813cd8f5c76625b8328d9c5b55891c9f3cd7a2739c59525`. Verificados checksums externos, manifiesto completo de 54 archivos y plan sin cambios del instalador final; payload `675c953c22db5cdd039dae5381779fd3520029fd6fbcf0e27195b34003adcb1e`. Configuración Production Angular con un solo prefijo API; binarios publicados fuera del VPS.
- Actualizadas continuidad/estado/evidencia y PR para reflejar cuenta ya creada, I05 listo y pendiente de instalación/cambio manual desde navegador. El usuario recibe ZIP y comandos WSL; no se afirma despliegue I05 ni nueva contraseña. Rama remota preservada, PR en borrador y main sin fusión. No se repiten suites por el cierre exclusivamente documental.

## 2026-10-26 — I05 instalado y saludable en el VPS

- El usuario ejecutó desde WSL la instalación con ZIP exterior e interiores verificados. JSON de `20261026-password-install1`: `status: updated`, `phase: completed`, `changes_made: true` y `health_verified: true`. Código `886bee7b2202b399ee063fe65d524b7f444e205c`, backend/frontend activos en `password-886bee7b2202`.
- Nueve migraciones existentes, I04 presente y permisos de lectura/actualización de AdminUsers e inserción de auditoría verificados. No ejecutó migraciones ni cambió contraseñas. Snapshot privado `/var/backups/prodimt-pedidos/password-update-20261026T231120Z-318fd2a6fc3f`.
- Se indica recarga sin caché, login actual, «Cambiar contraseña» y nuevo login después de guardar; no solicitar ni registrar contraseñas. Ese flujo real sigue pendiente de confirmación del usuario. No repetir instaladores, bootstrap ni alta para probarlo.
- Actualizados continuidad, estado, contrato I05 y evidencia, sin cambios de código ni nuevas pruebas. Registro basado en el JSON compartido, sin SSH directo desde este entorno. PR permanece en borrador y main sin fusión.

## 2026-10-27 — Login humano confirmado y primera revisión funcional

- El usuario confirma inicio de sesión y pregunta cuál incremento revisar primero. No se interpreta esa confirmación como cambio de contraseña ya ejecutado.
- Consultados PR/rama actuales y contratos I01/I04, estado, rutas y plantilla Angular del catálogo. Se propone empezar con fundamentos I01 y configuración enfocada I04: Catálogos → Clientes → Configurar.
- La revisión comprueba productos frecuentes, unidades de venta, cantidad sugerida, orden visual, posición en mensaje por cliente, máquinas, llave, entrega/cobro y aprobación. Guardar/recargar conserva el perfil; no crea pedidos ni suma cantidades sugeridas a demanda. No confundir posición en mensaje, orden visual y orden de visita; no asumir WhatsApp automático ni carga del Excel.
- Se registra únicamente confirmación de acceso y guía de revisión. Faltan verificación del cliente real en pantalla y conciliación privada; no se modifica código ni VPS, ni se repiten suites por documentación. PR en borrador, sin fusión.

## 2026-10-27 — Archivos privados para el alta inicial de clientes

- El usuario reporta que no ve clientes y solicita Excel con los primeros ya definidos. Recuperado el paquete privado I04 y su copia devuelta del XLSX, sin editar el original. Se contrastan sus 23 clientes normalizados con el CSV preparado y el encabezado actual del repositorio.
- Generados de forma privada `PRODIMT_Clientes_Iniciales.xlsx` y `.csv`: 23 filas, 13 columnas y códigos estables, sin duplicados ni datos inventados. Se conservan seis marcas de llave, cinco cobros contraentrega y dos instrucciones; teléfonos y orden de visita continúan vacíos. No se publican registros ni instrucciones en git.
- XLSX con hoja Clientes compatible con exportación CSV y guía de importación; filtros, paneles inmovilizados, listas y formato condicional de pendientes. Verificación de fuente, igualdad de las 23×13 celdas entre XLSX/CSV y byte-identidad del CSV con el preparado I04. Resúmenes recalculados y comprobados con ediciones temporales/restauradas; cero errores de fórmula y revisión visual de ambas hojas.
- No se modifican API/VPS/SQL ni se repiten suites de código por generar archivos. Importación real pendiente del usuario: Tipo Clientes, Validar, revisar propuesta y Aplicar. Con catálogo vacío se esperan 23 altas/0 errores; 23 teléfonos vacíos pueden generar advertencias. Productos, máquinas y perfiles quedan para su carga separada y no se aprueban automáticamente.

## 2026-10-28 — Clientes cargados y rediseño I06 autorizado

- El usuario confirma carga inicial y aporta capturas del catálogo con clientes. Solicita formulario de alta en modal, búsqueda principal y tarjetas por pedido/no pedido/pendiente, con descansos al final. Se inicia I06 sin modificar los datos reales ni reutilizar sus capturas como fixtures públicos.
- Modal diferido y rutas de catálogo con carga separada; estados derivados en servidor con fecha operativa, etiqueta de revisión y manejo explícito de conexión perdida. Descanso semanal por cliente mediante columna aditiva con valor inicial sin programación; CSV previo preservado. Pedido vigente prevalece; descanso no crea NoOrder ni producción.
- Build Angular Production correcto sin exceder el presupuesto inicial; cinco nuevas pruebas de navegador locales correctas. Suite completa, SQL Server y paquete nativo pendientes al registrar esta entrada. El navegador de pruebas local usa headless shell oficial de la versión del lockfile mediante configuración externa, sin alterar dependencias ni el workflow para esa adaptación.
- Suite .NET local: 225 correctas (5 dominio, 220 aplicación), con autorización, fecha operativa distinta de UTC, estados, precedencia, descansos y compatibilidad de edición. Primera suite completa de navegador: un test antiguo elegía el primer cliente y ahora abría un pendiente distinto por el nuevo orden; se fija su selección por identidad sin modificar la ordenación requerida. Python existente 135/135 correcto; graphify no disponible (127).

- Commit funcional I06 e9d6799080b8dd7bf2d016b85775f58c56a4de3e persistido en remoto. CI 40709421002: cuatro jobs correctos, 225 .NET, 25 SQL Server, 60 E2E y smoke Production. La selección por identidad resuelve el test de perfil.
- Preparados instalador y empaquetador I06 separados de I05: respaldo COPY_ONLY/CHECKSUM/VERIFYONLY antes del SQL incremental, historial 9/10 comprobado, rechazo de esquema parcial, conteos conservados, configuración privada preservada y recuperación de aplicación sin restauración SQL automática. Protocolo -I, opciones ANSI explícitas, secretos sólo en entorno limpio/stdin. Python 148/148 correcto. Se incorpora ensayo del SQL incremental mediante ODBC real en CI; validación de ese contrato pendiente.

- Cierre técnico I06: código/instalador 965dc65d82d2322b8c897a164c592bad6ed975a9; CI 40710267512 con cuatro jobs correctos. SQL 25/25, navegador 60/60, .NET 225/225, Python 148/148 y smoke Production. Contrato ODBC real nuevo: migración aditiva 9→10 repetible, defecto parcial rechazado, defaults preservados, transacción fallida revertida y constraint no confiable rechazado.
- Publicados binarios framework-dependent linux-x64 para runtimes 10.0.0 compatibles con 10.0.13 existente. Paquete PRODIMT_Catalogos_I06.zip (5,645,785 bytes), SHA256 8d733fe140f369dc810f9d634d4a1ee2f8e3b31b5451dd645eda193ec9dd6fcc; payload f301b87547980e4ea37286abc2318c8a601f80acc630641bdaa665803a471f8f. Extraídos/verificados CRC, checksums, manifiesto y 60 archivos con instalador fijado; guardado privado descargable. No hubo SSH ni cambios en VPS; faltan ejecución y aceptación del usuario.

## 2026-10-28 — I06 desplegado en VPS por el usuario

- El usuario verificó ZIP y checksums, copió instalador/payload y ejecutó PRODIMT_Instalar_I06.py --apply desde SSH con terminal interactiva. Compartió JSON de revisión 20261028-catalog-install1: status updated, phase completed, health_verified true, migraciones 9→10 y migrated_schema_verified/existing_data_counts_preserved true.
- Backend/frontend activos en releases/catalog-965dc65d82d2, código 965dc65d82d2322b8c897a164c592bad6ed975a9. Snapshot /var/backups/prodimt-pedidos/catalog-update-20261028T180203Z-23dae09a3d30; respaldo SQL verificado /var/opt/mssql/backup/prodimt-i06-20261028T180203Z-23dae09a3d30/ProdimtPedidos-copy-only.bak. No se restauró SQL automáticamente ni se cambió contraseña.
- Se registra el resultado aportado por el usuario; no hubo acceso SSH directo desde esta sesión. No se modifica código/binarios ni se repiten suites por actualizar documentación. Siguiente paso: Ctrl+F5 y aceptación desde navegador de búsqueda principal, modal, tarjetas por estado, orden y programación semanal explícita; aún sin confirmación visual del usuario. Conservar clientes cargados y respaldos, no repetir instalación ni bootstrap. PR continúa en borrador, sin fusión.

## 2026-10-28 — Configuración de productos por cliente preparada para importar

- El usuario pide recuperar los productos y su orden ya definidos para evitar alta manual. Recuperadas las copias privadas de la hoja I03 devuelta, el paquete I02 para verificar las 36 relaciones y el CSV de los 23 clientes ya importados. Originales sin cambios; no se solicitan ni usan credenciales del VPS.
- Tres CSV UTF-8 con BOM y encabezados actuales: 36 productos provisionales identificados por relación y con cliente en el nombre, tres máquinas positivas y 36 frecuentes para 23 perfiles. Se conservan 36 posiciones, 30 unidades kg y 19 preferencias positivas; seis unidades y 17 asignaciones siguen pendientes, incluido el cero no interpretado como máquina. Orden visual inicial basado en las posiciones explícitas; cantidades sugeridas vacías y sin aprobación automática. No se fusionan variantes por nombre.
- Validación y aplicación local contra el payload I06 fijado 965dc65d82d2322b8c897a164c592bad6ed975a9 y runtime 10.0.13 verificados: cero errores en productos/máquinas/frecuentes, 23 perfiles/36 relaciones recuperados por HTTP con unidades, orden, posición y máquina exactos; cero pedidos creados y cero perfiles aprobados. Revalidación de frecuentes muestra los 23 avisos de reemplazo esperados. Almacenamiento temporal InMemory; no es aplicación en SQL/VPS. Verificado también el transporte real de BOM.
- Excel de revisión generado con Artifact Tool: 36 filas, conteos 6/17, fórmulas sin errores, cambios temporales de dos requisitos comprobados y restaurados, exportación cotejada con fuente y revisión visual. ZIP final de seis archivos con CRC/checksums verificados, 13,013 bytes, SHA256 bbfe17adeabe1ba5c28adb6c966bfdbd9cf6fb2d5733dc14dd9d390b55b0f3de, guardado privado descargable. Nada identificable ni instrucciones de acceso se publican en git.
- Sin cambio de código, migraciones, despliegue ni credenciales. No se repiten suites generales por preparar datos. Pendiente Validar/Aplicar por el usuario, revisando actualizaciones de máquinas y reemplazo completo de los frecuentes de clientes incluidos, y completar los faltantes antes de capturar/aprobar los productos afectados.

## 2026-10-28 — Catálogos cargados; siguiente paso recomendado: captura operativa

- El usuario confirma que cargó los catálogos sin error y pregunta si avanzar a captura o a otro incremento. Se registra como carga real informada por el usuario; no adjunta conteos/JSON ni confirma haber completado las seis unidades o 17 asignaciones que faltaban en el paquete. No se repite importación ni se altera el VPS.
- Revisado el código desplegado: captura administrativa existente en /admin/clientes-pendientes, fecha de servicio explícita, productos frecuentes y notas internas; comprobación de pedidos ya existentes antes de guardar, corrección con motivo desde /admin/pedidos y avisos en /admin/operacion. Productos con unidad desconocida no admiten cantidades; la máquina desconocida queda separada como no asignada.
- Recomendación: comenzar con 3–5 pedidos reales de clientes con datos suficientes, verificar fecha/guardado/estado/revisión/demanda y ampliar progresivamente al piloto. Fecha de entrega mañana no equivale a estado de hoy en las tarjetas I06. Pedidos sujetos a revisión requieren autorización antes de demanda; NoOrder sólo confirmado. No se ingresan pedidos ficticios ni se modifican cantidades reales para demostrar correcciones.
- Posible desarrollo posterior I07: acceso de captura desde tarjetas, fecha visible y mensaje original como referencia, reutilizando catálogo/perfiles, comprobación de pedido existente y correcciones. Es una propuesta para evaluar tras usar el flujo, no autorización de implementación ni parser/WhatsApp real. Sin cambios de código, pruebas generales o nuevos artefactos por registrar progreso y recomendación.

## 2026-10-28 — I07 captura rápida autorizada

Implementación de campo abreviado, desglose servidor al salir, entrega compacta y captura manual. Alias por cliente/producto, sin inferir HN, migración aditiva 10→11 y preservación de contratos CSV/PUT. Guardado con recibo durable, versión/huella de perfil y control de entrega independiente. Pruebas locales/SQL/instalador y paquete en preparación; VPS sin modificar.

I07: 265 pruebas .NET locales iniciales correctas; 67/68 E2E iniciales y caso restante corregido/comprobado de forma dirigida. CI de checkpoint ejecutó smoke Production y backend correctamente; una prueba SQL requería deserializar enums como texto y se corrigió. Se agregan límites de notas acordes con columnas y prueba concurrente de dos capturas. Nuevas herramientas nativas y 14 pruebas específicas de paquete, 162 Python totales previstas; CLI ODBC 10→11 añadido a CI. `graphify update .` no disponible (127). Sin acceso/modificación al VPS.

### Cierre I07 — paquete listo, VPS pendiente

Código y herramientas `46fed604732eddca0c7ac199b58bb0b429545b89`: [CI 40765239145](https://github.com/romanrfhack/pedidosProdimt/actions/runs/40765239145) con cuatro jobs correctos. 267 .NET, 29 SQL Server reales, 68 E2E, 162 Python y smoke Production completo. La segunda corrección de prueba SQL decodifica JSON antes de comprobar el texto original (el serializador escapa +); la funcionalidad de guardado ya era correcta. Revisión visual corrigió ancho del formulario y strong inline; móvil 390 px sin desbordamiento.

Publicación linux-x64 framework-dependent con SourceRevisionId verificado y Angular Production 469.14 kB. Paquete PRODIMT_Captura_Rapida_I07.zip: 5,696,903 bytes, SHA256 131b0161735197c15a3c7b5fd715648ea2b9258deec066bae0af041cca89acae, 62 archivos comprobados, CRC/hashes/pines correctos y plan sin cambios. Entregado de forma privada; sin datos del Excel ni credenciales. Ver docs/validation/2026-10-28-quick-capture.md.

I06 sigue siendo el último despliegue confirmado; no se ejecutó SSH, no se repitió carga de catálogos y no se modificaron main/Nginx/cuentas. Pendiente instalación del usuario, asociación explícita de HN cuando corresponda, confirmación de perfiles, pedido/fecha/unidades y corrección con aviso. Cierre documental posterior conserva código y binarios.

## 2026-10-28 — Instalación I07 detenida antes de autenticación SQL


El primer intento de instalación I07 reportado por el usuario el 28 de octubre se detuvo en sql-admin-authentication con sql_credentials_invalid. El preflight confirmó diez migraciones y todos los permisos DML previstos; changes_made, database_migrations_run y migrations_may_have_changed fueron false. La validación local de entrada ocurre antes de consultar SQL como administrador: exige usuario 1–128 caracteres, contraseña 1–512 y ausencia de NUL/CR/LF. El reporte no identifica cuál campo falló ni demuestra rechazo de autenticación por SQL Server. Reintentar el mismo instalador con el login SQL existente y su contraseña introducidos en los prompts ocultos; mantener I06 como último despliegue confirmado. No hubo cambios al código/paquete ni intento remoto desde esta sesión.

## 2026-10-30 — Diagnóstico de autenticación SQL I07

Reporte del usuario: segundo intento sin cambios, diez migraciones, sql_operation_failed en fase administrativa y sin números Msg. Los permisos de aplicación sí se consultaron; no atribuir todavía a contraseña incorrecta ni reiniciar/reinstalar. Se verificó el código y el ZIP publicado recuperado de forma privada (SHA exterior original correcto). Instalador entregado SHA256 42b1e7d869a41d3d42851925585035089e5d58450993ebb16b89bb9d44c5fb1b.

Nueva herramienta diagnose-capture-auth.py, publicada como PRODIMT_Diagnostico_SQL_I07.py. Verifica ese pin antes de cargar código y sólo invoca preflight/authenticate_admin originales; proceso con lista de lecturas permite únicamente systemctl show, GET de salud y seis SELECT específicos con el protocolo ODBC original. Identifica errores sin encabezado Msg, conserva sólo categorías/SQLSTATE permitidos/números y permisos booleanos; no imprime login, contraseña, cadenas de conexión ni stdout/stderr crudos, y limpia referencias a credenciales. No llama apply, stage, backup, switch, rollback ni cambia servicios.

173/173 Python locales correctas; 11 nuevas cubren categorías, no filtración de secretos, pin previo a ejecución, rechazo de comandos mutadores, fallos/timeout y verificación de esquema usando el método original. Fixture SQL/ODBC real añadido para un login ficticio inexistente en contenedor desechable, sin intentar contraseñas erróneas en una cuenta existente. CI pendiente en este corte. graphify update . no disponible (127). Binarios y paquete I07 se conservan sin cambios; no se ejecutó el diagnóstico en el VPS desde esta sesión. Pendiente salida del usuario para determinar causa y decidir reintento del instalador.

### Cierre del diagnóstico I07

Código `cd3ba9a034a5331a1437b95118b22ced382d8ccd`, [CI 41105959819](https://github.com/romanrfhack/pedidosProdimt/actions/runs/41105959819): cuatro jobs correctos. 173 Python, 267 .NET, 29 SQL Server, 68 E2E y smoke Production. La prueba de ODBC real clasifica un login ficticio inexistente como sql_login_rejected sin revelar usuario/contraseña ni mensajes originales. Original de instalador, paquete I07 y código de aplicación sin cambios (diff vacío en src/apps/constructor e instalador).

PRODIMT_Diagnostico_SQL_I07.py, 10,977 bytes, SHA256 476c229e0df43ec31f6872cd524fdff8e811f88ff3bd3ab7fbe0828436aab16f. Fuente/copia entregable comparadas y CLI --help comprobada; archivo preparado de forma privada. Uso sólo por SSH -t con credenciales administrativas SQL ocultas, sin --apply. Pendiente resultado del usuario para determinar la causa remota. I06 sigue vigente; no se ejecutó SSH ni se modificó la base/servicio desde este entorno. Cierre documental conserva el diagnóstico probado.

## 2026-10-30 — Corrección propia en diagnóstico I07

El usuario obtuvo diagnostic_local_failure en application-read-only-preflight sin prompts SQL. Reproducción con instalador entregado de hash verificado: AttributeError, atributo DEVNULL ausente, antes de llamar al transporte nativo. La revisión 1 reemplazó subprocess por SimpleNamespace sin ese valor, usado por command() en systemctl/curl. Las pruebas anteriores simulaban preflight completo y no detectaron esta incompatibilidad. No atribuir a credenciales ni estado del VPS.

Corrección mínima: conservar DEVNULL y cambiar revisión a 20261030-sql-auth-diagnosis2. Nuevo caso ejecuta preflight y authenticate_admin originales, incluyendo process(), command(), http(), inspector/configuración, permisos y esquema. Sólo archivos del host/transporte son ficticios; cubre éxito y rechazo ODBC sin filtrar secretos. La regresión reproduce el fallo en v1 y pasa en v2. 174 Python locales correctas. CI pendiente en este corte.

Exportación misma identidad de PRODIMT_Diagnostico_SQL_I07.py: 11,005 bytes, SHA256 8e7f3b99e874adb6b97ee1958311c01f4b25ddd89ba49481280528c2043a03fc. Se prepara comando SSH de reparación de sólo ese script, con hash v1/v2, verificación del resultado y ejecución posterior sin --apply. Parseo de shell comprobado; v1→v2, reintento sobre v2 y bytes desconocidos sin modificación, comprobados localmente. Instalador y paquete intactos. graphify update . no disponible. Aún falta el diagnóstico SQL real y sigue I06 activo según el último reporte.

### Cierre de revisión 2 del diagnóstico

Código `2ecd87f649bfd0ec9e68c246ce51418b422fae0c`, [CI 41160327219](https://github.com/romanrfhack/pedidosProdimt/actions/runs/41160327219): cuatro jobs correctos; 174 Python, 267 .NET, 29 SQL Server, 68 E2E y smoke Production. Prueba de login rechazado ODBC real correcta. Versión 2 de la herramienta guardada sobre su misma identidad privada; sin cambios de aplicación, instalador o payload. Comando de reparación que sólo acepta hashes v1/v2 validado y documentado. Sigue pendiente ejecutar con el usuario y obtener información del error SQL original; no se accedió ni modificó el VPS desde este entorno. Cierre documental conserva los mismos bytes de diagnóstico.

## 2026-10-30 — Diagnóstico I07 correcto en VPS

El usuario repara el archivo con verificación SHA v1/v2 y ejecuta revisión 2. Resultado de sólo lectura: inspected/completed, preflight y comprobaciones administrativas completas, cinco SELECT correctos, historial 10 y DML de aplicación correctos. Hash del instalador verificado, sqlcmd en /opt/mssql-tools/bin/sqlcmd. sysadmin/dbcreator/db_owner/backup/view_definition=1. verify_permission=null aceptado por la rama sysadmin; no requiere cambios de permisos. Sin cambios ni migración. La causa histórica de sql_operation_failed no queda demostrada; las credenciales actuales funcionaron en este corte.

Se indica reintentar --apply en el instalador/payload originales ya copiados, con las mismas credenciales SQL. Base 10 validada, no hace falta resume ni descargar/compilar otra versión. Instalador y binarios intactos; no se ejecutó SSH desde este entorno. Pendiente resultado updated/completed, migraciones 11 y health_verified true antes de declarar desplegado I07 o pedir prueba funcional del rediseño. Sólo documentación: no repetir suites ni regenerar artefactos.

## 2026-10-30 — I07 instalado por el usuario

Instalador original 20261028-capture-install1 ejecutado en VPS: updated/completed, cambios y migración realizados. Historial 10→11; esquema migrado, conteos existentes y salud verificados. Código 46fed604732eddca0c7ac199b58bb0b429545b89, backend/frontend capture-46fed604732e. El campo database_checks conserva el preflight de diez migraciones; migrations_after 11 corresponde al esquema final. Sin cambio de contraseñas ni restauración automática de SQL.

Snapshot privado: /var/backups/prodimt-pedidos/quick-capture-update-20261031T024549Z-90dbaa60d04e
Respaldo SQL verificado: /var/opt/mssql/backup/prodimt-i07-20261031T024549Z-90dbaa60d04e/ProdimtPedidos-copy-only.bak

Se actualizaron estado vigente, continuidad, I07, plan y evidencias. El diagnóstico de preinstalación I06 queda archivado; sus guardas no corresponden a los nuevos releases I07. No repetir instalación/importación/bootstrap. Pendiente Ctrl+F5 y revisión real del campo, hora informativa, desglose/manual, aprobación de perfiles y alias HN explícito, antes de guardar pedidos reales. No se declara realizado el ensayo de ruta, ni completados faltantes de unidades/máquinas. Sin cambios de código/artefactos ni SSH directo desde este entorno; no hace falta repetir suites por este registro documental.


## 2026-10-30 — I07: interpretación correcta, guardado sin confirmar

El usuario reporta fallo al guardar desde Pendientes y aporta captura, petición y respuesta de preview. El desglose, perfil aprobado y unidades confirmadas se observan correctos. No se ha aportado el estado HTTP ni la respuesta de capture; la causa y el resultado persistido del intento no están demostrados. Se revisa el flujo de transacción/recibo, auditoría y manejo Angular de respuestas inciertas sin hallar una causa verificable con esta evidencia. No se altera código ni se accede al VPS.

Se registra la incidencia en docs/validation/2026-10-30-i07-capture-incident.md, ESTADO y CONTINUAR. Se prioriza conservar pestaña/referencia e inspeccionar únicamente Status Code y Response de capture, sin compartir autenticación. No recargar, crear otro intento ni reinstalar. Los datos reales y credenciales compartidos no se copian al repositorio. Sin cambios funcionales ni repetición de suites. I07 instalado conserva once migraciones y aceptación pendiente.


## 2026-10-30 — I07: HTTP 500 confirmado y diagnóstico de guardado

La imagen nueva de Network confirma capture 500 con preview/perfil/plantilla/consulta 200. El cuerpo aportado conserva fecha, versión y huella de preview. No se usan ni guardan las credenciales compartidas; tampoco se copia información real al repositorio. La causa y la confirmación de la transacción siguen pendientes.

Se prepara scripts/pilot/diagnose-capture-save.py y su entrega PRODIMT_Diagnostico_Captura_I07.py, revisión 20261030-capture-save-diagnosis1, SHA e0f83643a4f2a8beff76f85009fbe57fa83dfcfcda4e145f1871fd2b3a2de0d0. Lee sólo ActiveState y journal del servicio; agrupa mensajes/stack antes de emitir metadatos permitidos, sin texto libre. No SQL/config, API, credenciales adicionales, reproducción ni mutaciones. Distingue fallos ajenos; no diagnostica ausencia de canal administrativo sin evidencia. Conserva intento/pestaña existentes y no reusa el diagnóstico SQL previo de I06.

Doce pruebas nuevas y 186 Python locales correctas; CLI real local sin systemd devuelve read_command_failed sin divulgar streams. Sin cambios de aplicación, migraciones, cuentas o datos operativos. graphify update . no disponible (127). Se actualizan continuidad, estado y evidencia. Pendiente JSON remoto para identificar la excepción antes de una corrección; no se opera SSH desde este entorno.


## 2026-10-30 — I07: canal administrativo faltante confirmado y reparación puntual

El usuario devuelve diagnóstico inspected/completed: servicio activo, releases I07, tres excepciones admin_sales_channel_missing dentro de AdminOrderCaptureService/EfSalesChannelRepository, sin errores SQL ni tablas denegadas. Se confirma la ausencia del canal requerido por administración. La excepción precede al alta del pedido dentro de la transacción; no se reproducen pedidos reales ni se usan credenciales compartidas.

Se prepara reparación idempotente scripts/pilot/repair-native-admin-channel.py y entrega PRODIMT_Reparar_Canal_Admin_I07.py. Usa inspector del payload I07 original con hash fijo, proceso/configuración/target esperados, consulta con cuenta de aplicación y permisos SELECT/INSERT. Inserta sólo AdminManualCapture si falta y su AuditLog (System entero 2); la transacción comprueba conteos de pedidos, recibos y catálogos. Respeta canal existente y falla ante configuración incompatible. Sin bootstrap, migración, cuentas, reinicio o reproducción de captura. Resultado ambiguo de transporte queda marcado; reejecutar reparación no duplica el canal.

197 Python locales correctas (11 nuevas), carga y parser comprobados con el payload original entregado. Se agregan dos regresiones SQL Server con datos ficticios: 500 por ausencia sin pedido/recibo, reparación con permisos DML limitados y mismo intento que guarda/repite una sola entrega; fallo de auditoría revierte canal y permite reintento. El esquema real confirmó ActorType entero para AuditLogs; se corrigió en la herramienta antes de su entrega. Código final 63b955ad333c1d4e3cc2f3f4eb73187a1629cef7, CI 41167167347 con cuatro jobs correctos: 197 Python, 31 SQL Server (dos regresiones nuevas), 267 .NET, 68 E2E y smoke Production. Sin pruebas SQL locales ni SSH al VPS. graphify update . no disponible (127). Pendiente JSON de aplicación de reparación y prueba del botón Reintentar guardado.


## 2026-10-31 — I07: reparación del canal aplicada por el usuario

El usuario verifica SHA 39147bfb8d029c5865c102fec05e6d4da837400be78abeec12d2afd816c3fa6e y ejecuta PRODIMT_Reparar_Canal_Admin_I07.py --apply por SSH/entrada estándar. JSON de revisión 20261030-admin-channel-repair1: repaired/completed, canal creado/verificado, auditoría insertada y conteos existentes conservados. Sin migraciones, reinicio, reproducción de pedidos ni impresión de secretos.

Se actualizan continuidad, estado, I07 y evidencia. application_save_verified=false es esperado porque el programa no envía pedidos; channel_missing=true pertenece al preflight anterior a insertar. No repetir reparación. Falta Reintentar guardado en la misma pestaña/referencia y confirmar una sola entrega con sus cantidades. No se declara guardado real ni aceptación de negocio a partir de este JSON. Sólo documentación, sin repetir suites ni regenerar artefactos, y sin SSH directo desde este entorno.


## 2026-10-31 — I08: configuración amplia y días de trabajo

El usuario aporta imágenes privadas del espacio desperdiciado en configuración y pide usar ancho con márgenes, vista habitual de laptop sin scroll y adaptación a tablet/móvil. Solicita siete casillas con marcada=trabaja/sin marcar=descansa. Se conservan las imágenes/datos fuera del repositorio.

Se extrae el HTML del componente de catálogos y reorganiza sólo la ruta enfocada: cabecera compacta, productos en filas, unidades/contexto, orden/posiciones/abreviaturas, aprobación explícita, calendario y máquinas laterales, tokens desplegables. Ancho y cabecera se amplían sólo mientras esta ruta está activa. En móvil se apilan controles con desplazamiento vertical; listas largas mantienen scroll propio. No se oculta información mediante overflow global.

Nuevo componente semanal reutilizado también en el modal de cliente, con inversión de restDays I06. Guardar días consulta el cliente vigente para conservar entrega/contacto y detecta un calendario cambiado; no toca borrador ni versión leída del perfil. El contrato PUT heredado no recibe concurrencia atómica nueva. Descansar no crea demanda, NoOrder, cancelaciones o desactivaciones.

Build Angular correcto con advertencia de estilo 7.83 kB, bajo límite 8 kB sin relajar presupuestos. 79 E2E locales correctos, once nuevos, incluidos calendario/persistencia/conflicto y tres productos en 1366x768 sin scroll de página; revisados también 390/768/1920 px con datos ficticios. 206 Python correctos, nueve del paquete/instalador frontend. Browser local Chrome headless 148 mediante copia temporal. graphify no disponible (127).

La revisión del flujo completo detecta que el endpoint heredado de pendientes devuelve todos los clientes sin respuesta, incluidos descansos. Se conserva ese contrato y la capa de datos del frontend cruza los calendarios persistidos con la fecha de servicio explícita. Descansos se ocultan y pueden mostrarse al final para capturar excepciones; no se usa el estado de hoy para otra fecha. Se preserva la captura abierta bloqueando ese filtro. Se corrige además el reintento de carga tras error y se ignoran respuestas de otra consulta/fecha. Tres E2E nuevos verifican lunes/domingo con navegador UTC+14, captura excepcional sin pedidos/NoOrder y recuperación de fallo.

Se prepara instalador nativo I08 sólo frontend, con payload verificado por hash, comprobación HTTPS y autorización, snapshot privado y restauración del enlace ante fallo/interrupción. Código be3995fdce6948ff00f44edd6025a1da1899887c, [CI de cierre](https://github.com/romanrfhack/pedidosProdimt/actions/runs/41217210669) con cuatro jobs correctos: 79 E2E, 206 Python, 267 .NET, 31 SQL Server y smoke Production. Build Angular, 79 E2E locales y 206 Python correctos. Paquete PRODIMT_Configuracion_Cliente_I08.zip verificado y guardado: 170004 bytes, SHA256 5d950c83bcb59ff8b14dda9fb556dfdfe3205b79bd6d9f27dc68e966da2702ab; payload SHA256 3c35c329cf3434839336ffbb43ce2ee2e163cfc28b17d3e12792d4d4c4f328e3. Diez archivos frontend, incluidos avisos de terceros. Conserva backend capture-46fed604732e y cambia sólo frontend a client-config-be3995fdce69. Sin SQL, credenciales adicionales o reinicio. I08 no aplicado aún en el VPS; guardado real del pedido I07 pendiente de confirmar. Conservar cualquier captura incierta antes de recargar. No se operó SSH desde este entorno.
