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
