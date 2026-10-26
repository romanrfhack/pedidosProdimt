# Estado del proyecto

Actualizado: 26 de octubre de 2026.

## Ubicación y etapa

- Repositorio: `romanrfhack/pedidosProdimt`.
- [PR #1](https://github.com/romanrfhack/pedidosProdimt/pull/1), rama `codex/pilot-foundation-2026-10-24`. Borrador, sin fusión; binarios funcionales I04 desplegados en el VPS.
- Base de I02: `a2eb7fb533982ba719ce74d0a73b4f631cd0d2f9` (I01 verificado).
- Código I02 verificado: `192c8d7871e2b2021b4d485dd71b5ebd7abbda4d`; [CI](https://github.com/romanrfhack/pedidosProdimt/actions/runs/40120275407). El commit documental posterior conserva el mismo código.
- **I02 implementado y validado en CI, incluido SQL Server real**: captura/importación diaria y correcciones. Contratos en `INCREMENTO-02.md`.
- **I03-A implementado y validado en CI**, código `26eb6d8d47786bb18f2fe8117596e74f4f277430`; [cuatro jobs correctos](https://github.com/romanrfhack/pedidosProdimt/actions/runs/40123481595). Instalación reproducible Linux/WSL, SQL aislado, acceso por túnel y credenciales propias. Hitos separados en `INCREMENTO-03.md`; instrucciones en `../../infra/pilot/README.md`.
- **I04 implementado y validado en CI**, código `b630072ce67a691c3757d0590cc7f7d45f423d52`; [cuatro jobs correctos](https://github.com/romanrfhack/pedidosProdimt/actions/runs/40130113089). Avisos persistentes de cambios de demanda, configuración enfocada por cliente y control de edición simultánea. Contratos y límites en `INCREMENTO-04.md`.
- **Etapa 1 en desarrollo; preparación operativa de etapa 0 aún incompleta.**
- **I04 desplegado el 25 de octubre de 2026.** El usuario ejecutó `20261025-resume5` y compartió `status: updated`, `phase: completed`, nueve migraciones, esquema/permisos DML/salud verificados. Ambos releases activos son `20261024-i04-b630072`; configuración privada conservada y dos respaldos verificados. Pendiente comprobar login desde su navegador; la ruta privada I04 no se ha cargado desde esta sesión y el ensayo operativo aún no se ha realizado.

## Terminado

- I01: catálogos, unidades, perfiles versionados, máquina por producto, snapshots y demanda por máquina/ruta.
- I02: fecha de servicio explícita para administración y consulta/revisión por fecha.
- CSV diario revisable, origen y referencia externa persistentes, protección de reintentos dentro y entre lotes.
- Aplicación transaccional: lote inválido o fallo de base no deja pedidos ni recibos parciales.
- Corrección con motivo, versión esperada y clave de operación; reemplazo completo de partidas con historial antes/después.
- Preservación del nombre/unidad/máquina históricos al ajustar una partida existente; productos agregados usan el catálogo vigente.
- Interfaz de importación separada de catálogos, reintentos con el mismo contenido e historial legible.
- CSV de catálogos compatible con la marca UTF-8 de Excel.
- Migraciones aditivas `20261024181206_AddDailyOrderOperations` y `20261024181934_ExpandOrderAuditSnapshots`; historial completo sin el límite anterior de 2,000 caracteres.
- Revisión del XLSX devuelto: 23 identidades/ruta, acceso explícito, cinco condiciones de contraentrega y dos instrucciones internas. 52 fórmulas simples verificadas. CSV privado preparado; productos incompletos separados.
- Código/documentación públicos separados de los datos privados.
- I03-A: Angular Production usa el mismo origen sin credenciales demo; API sirve la interfaz y reserva `/api` y `/health`.
- Inicialización explícita de SQL, canales y administrador; el arranque normal no migra ni siembra datos demo. Reintentar no reinicia contraseñas.
- Docker Compose con volumen persistente, SQL sin puerto publicado, aplicación en `127.0.0.1:5098`, respaldo verificado y reversión de imagen sin bajar esquema.
- Hoja privada de pendientes I03: 36 relaciones y 23 clientes, valores confirmados conservados, unidades y otros desconocidos explícitos.
- I04: avisos de corrección/aceptación con antes/después/diferencia por máquina y reconocimiento explícito, guardados junto con pedido y auditoría. Reconocer no significa producir.
- Plan y pedidos se actualizan con sondeo mientras la pantalla está visible; lecturas antiguas se descartan, el borrador de corrección se conserva y las pérdidas de conexión quedan visibles.
- Configuración enfocada `/admin/clientes/:id/configuracion`: agregar/quitar productos, cambiar orden visual y asignar posiciones explícitamente, con snapshot de perfil y conflictos de versión/unidad observada.
- Captura administrativa comprueba pedidos existentes del cliente/fecha y permite abrirlos para corregir; una entrega independiente requiere una decisión explícita y conserva revisión.
- Hoja devuelta I04 revisada: 30 unidades kg, 36 posiciones válidas por cliente; seis unidades aún vacías, 16 máquinas vacías y un cero sin significado confirmado. El original permanece intacto.
- VPS/subdominio elegidos. Guía por etapas en `../../infra/pilot/DEPLOY-VPS.md`, diagnóstico de sólo lectura y publicación Nginx aislada con protección de conflictos.

## Validación

- **203/203 pruebas .NET locales y remotas** correctas: 5 dominio y 198 aplicación; cero omitidas. I04 añade avisos, perfiles y protección concurrente.
- **53/53 E2E I04** con API mock correctas en local y CI, build Angular correcto y revisión móvil a 390 px sin desbordamiento.
- Revisión estática independiente de contratos, privacidad, snapshots, fechas y transacciones completada; hallazgos corregidos.
- I02: **9/9 pruebas HTTP/SQL Server reales correctas**: migraciones, persistencia entre hosts, concurrencia con deadlocks reales y respuesta 409, rollback, correcciones, auditoría extensa y perfiles. Los fallos encontrados en la primera ejecución quedaron corregidos.
- SQL Server se ejecutó en CI contra una instancia desechable, eliminada al terminar. No se usaron datos reales ni una base operativa. Evidencia en `../validation/2026-10-24-i02.md`.
- Vista previa HTTP del CSV privado: **23/23 clientes válidos, cero errores**, 23 advertencias por teléfono vacío. No aplicado.
- InMemory conserva las funciones de I01 y la vista previa diaria. Aplicar lotes o corregir pedidos exige persistencia relacional; no se finge éxito durable.
- La última corrida documental de I02 encontró tres fallos E2E durante el llenado inicial de formularios. I03 hace explícita la espera del estado editable y conserva trazas sólo ante fallo; **37/37 locales y remotos correctos**.
- I03: **13/13 pruebas SQL Server reales correctas**, incluidas cuatro nuevas de bootstrap. **Smoke completo Docker Production correcto**: login, mismo origen, sin demos, permisos limitados, reintentos, respaldo y persistencia. Evidencia en `../validation/2026-10-24-i03.md`.
- I04: **20/20 pruebas SQL Server reales correctas**, incluidos rollback de avisos y carrera de aprobación/unidad. Modelo/migración consistentes. [Evidencia de cierre](../validation/2026-10-24-i04.md).
- 15/15 pruebas Python de inventario y publicación aislada correctas en local y CI. Smoke Docker Production completo correcto, incluidos archivos extraídos con permisos privados. No se ejecutaron Nginx ni certificado en el VPS.
- Comprobación SSH directa: el entorno no tiene ruta de red al servidor. Se entregan comandos para ejecutar desde WSL; no hubo cambios remotos.
- Inventario dirigido del usuario confirma PRODIMT nativo activo mediante systemd, frontend/backend en releases y proxy hacia la API existente. Certificado del dominio vigente y renovación webroot previa. Compose v2/Buildx no están instalados; no se requieren para preparar una actualización nativa con binarios compilados fuera del VPS.
- Inspector nativo ejecutado por el usuario: destino SQL y principal coinciden; Production/JWT/seed compatibles, runtime .NET/ASP.NET 10.0.13, cinco migraciones de mayo, índice anterior no único y cero pedidos/grupos duplicados. La cuenta de aplicación no tiene permisos DDL, BACKUP ni verificación. La base conserva sus otros datos; no se considera una instalación vacía ni se ejecuta bootstrap.
- I03-B prepara un actualizador para ese despliegue exacto: payload verificado, credenciales administrativas ocultas sólo en la sesión, respaldo COPY_ONLY/CHECKSUM verificado antes de migrar y cambio/reversión de releases. El usuario ya ejecutó una fase de escritura; el resultado y sus límites se detallan abajo.
- Primer intento `--apply` del usuario detenido en `read-only-preflight` con `environment_file_format_unsupported`, `changes_made: false` y `migrations_may_have_changed: false`. El lector anterior sólo admitía un subconjunto de EnvironmentFile; se amplía siguiendo systemd 255, con diagnóstico de sintaxis sin claves ni valores. La configuración efectiva de reinicio sigue debiendo coincidir con la del proceso activo antes de autenticar SQL o preparar releases. No se conoce todavía la construcción exacta del archivo que disparó el bloqueo; no se solicita ni modifica su contenido privado. Evidencia en `../validation/2026-10-25-native-env.md`.
- Segundo intento con `env2`: preflight completo correcto; detecta diez asignaciones válidas y una ignorada por nombre, sin exponer contenido. Se detiene en autenticación administrativa con `apply_requires_interactive_tty`, sin cambios ni DDL. Reproducido el defecto del script con una PTY real: `open('/dev/tty', 'r+')` exige seek y falla. La revisión `20261025-tty3` abre sólo la salida de prompts; getpass conserva su lectura oculta independiente. Cinco nuevas pruebas con terminal real y suite Python 81/81 correctas. Evidencia en `../validation/2026-10-25-native-tty.md`.
- Tercer intento `tty3`: autenticación/capacidades SQL, snapshot privado y respaldo COPY_ONLY/CHECKSUM/VERIFYONLY completos; fallo `sql_operation_failed` en migración. El usuario confirmó recuperación sin errores y salud anterior verificada. `migrations_may_have_changed: true`; la base no se restauró y los releases I04 ya existen como staging. El error SQL original no se conservó; no inferir cinco o nueve migraciones por el resultado de rollback de código.
- Preparado diagnóstico autónomo de sólo lectura para historial/esquema, opciones de sesión del cliente original, salud, staging y metadatos del respaldo. El protocolo anterior de sqlcmd omite `-I` y la migración crea índices filtrados que requieren QUOTED_IDENTIFIER ON; causa probable pendiente de contrastar con el VPS. Se prepara corrección explícita de opciones y prueba real por CLI, sin distribuir aún otra ejecución `--apply`. Evidencia en `../validation/2026-10-25-native-sql.md`.
- Recibido diagnóstico posterior: sqlcmd ODBC 17.10, QUOTED_IDENTIFIER=0 y ARITHABORT=0, compatibilidad 160, cinco migraciones conocidas y ninguna desconocida, cero pedidos, índice anterior no único y auditoría nvarchar(2000). Aplicación anterior y base saludables; staging presente y probes del paquete coincidentes. Respaldo previo regular, 4,872,704 bytes y modo 600. La cuenta cotidiana carece de VIEW DEFINITION: sus metadatos null no prueban ausencia de columnas/tablas.
- Preparada revisión `20261025-resume5`: reanudación sólo del snapshot revisado, verificación íntegra de staging/configuración, comprobación administrativa del esquema previo, VERIFYONLY del respaldo conservado y respaldo nuevo antes de migrar con opciones corregidas. No borra ni sobrescribe staging/respaldo anteriores; conserva recuperación de aplicación. Evidencia en `../validation/2026-10-25-native-resume.md`.
- [CI 40205783899](https://github.com/romanrfhack/pedidosProdimt/actions/runs/40205783899), commit `dd83eba0a81f27850e8af63c05957e3f4e6357f4`: los cuatro jobs terminaron correctamente; 108 Python, 21 SQL, 203 .NET, 53 E2E, contrato sqlcmd real y smoke Production/persistencia/respaldo. El ajuste de fixtures no cambió bytes del actualizador ni aplicación.
- Resultado remoto final del usuario: `resume_source_verified`, `staged_releases_verified`, `resume_administrative_schema_verified`, `previous_database_backup_reverified`, `migrated_schema_verified`, `application_new_table_dml_verified` y `health_verified` verdaderos. Migraciones 5→9. La verificación de salud incluye proceso I04, base accesible, endpoints nuevos con 401 sin autenticación y bytes de HTML/JS/CSS por HTTPS; el flag `health_used_https_proxy: false` sólo indica que la consulta inicial de salud usó loopback sin redirección. No acredita todavía login humano ni pedidos operativos.
- Respaldo nuevo verificado: `/var/opt/mssql/backup/prodimt-i04-20261025T160429Z-1f5a37e4cab5/ProdimtPedidos-copy-only.bak`; snapshot nuevo `/var/backups/prodimt-pedidos/i04-20261025T160429Z-1f5a37e4cab5`. Se conservó y reverificó el respaldo `20261025T142158Z-11ceb110bb44`. No se restauró automáticamente la base ni se modificó Nginx/certificado.
- El usuario abrió I04 y recibió credenciales web inválidas. Su captura SQL muestra que la cuenta `admin` existe y está activa; no confundir su contraseña web con la identidad SQL administrativa. Se prepara recuperación local puntual `reset-native-admin.py`: TTY oculta, respaldo privado del hash anterior, actualización por Id/hash observado y auditoría atómica, sin activar/crear usuarios, cambiar logins SQL, migrar o reiniciar. Prueba login y consulta administrativa por HTTPS sin imprimir JWT. Aún no ejecutado en el VPS; [evidencia](../validation/2026-10-25-admin-recovery.md).
- Recuperación validada en [CI 40213743976](https://github.com/romanrfhack/pedidosProdimt/actions/runs/40213743976): cuatro jobs correctos, 120 Python, 205 .NET, 21 SQL, 53 E2E y smoke Production. El servicio .NET acepta los hashes generados (incluido Unicode); la CLI real rechaza conflictos y revierte la contraseña ante fallo de auditoría. Script de recuperación entregado; faltan ejecución y JSON remoto.
- El reset del usuario se detuvo en `new-web-password`, `password_confirmation_mismatch`, sin cambios ni reset. Su nueva captura muestra POST `/api/api/auth/admin/login` con 404; no acredita contraseña incorrecta. Se pausa el reset para resolver la ruta.
- HTTPS público comprobado: POST `/api/auth/admin/login` con identidad ficticia inexistente devuelve 401; el doble prefijo devuelve 404. El bundle publicado `main-ZDENE75O.js` contiene apiBaseUrl vacío y un solo `/api` para login, como Production I04. Apunta a frontend anterior en caché del navegador; falta confirmar tras recarga sin caché. No se cambió servidor/configuración ni se usaron contraseñas reales.
- El 26 de octubre el usuario confirma ruta correcta y aporta 401 con sus credenciales. Autoriza crear una cuenta nueva con contraseña generada y probarla. Preparado `create-native-admin.py`: cuenta `roman.prodimt`, contraseña aleatoria de 32 caracteres generada en el VPS, referencia privada root/600 para repetir sin rotación y transacción de alta/auditoría. Reutiliza el módulo de recuperación previo por SHA256; verifica login/consulta administrativa HTTPS antes de mostrar datos sólo en terminal. No modifica cuentas previas. [CI 40403547948](https://github.com/romanrfhack/pedidosProdimt/actions/runs/40403547948): 129 Python, SQL y .NET/Production correctos, incluido contrato real de creación/reintento/rechazo/rollback. Navegador aún en ejecución al registrar; no cambió la aplicación. Entregable preparado; ejecución real y JSON pendientes. [Evidencia](../validation/2026-10-26-generated-admin.md).

## Siguiente paso

1. Ejecutar la creación de `roman.prodimt` desde WSL y revisar JSON de alta/login/consulta HTTPS. Guardar la contraseña que el programa muestra tras verificarla y confirmar acceso en navegador. Se usa SQL administrativo sólo mediante prompt oculto. No repetir actualización ni bootstrap.
2. Una vez confirmado acceso, implementar cambio de contraseña propia desde la aplicación, según autorización nueva del usuario. No confundirlo con la herramienta local de alta ya preparada.
3. Conciliar los catálogos existentes con la revisión privada I04 y preparar su vista previa antes de aplicar la carga. La actualización no cargó el Excel ni borró la base previa. No publicar ni sembrar clientes reales en CI.
4. Completar las seis unidades, máquinas faltantes/cero y orden de visita. Las posiciones de interpretación ya fueron llenadas, pero la aprobación del perfil sigue siendo explícita; cobros y saldos se completan para su etapa.
5. Ratificar corte de 10:00 a.m. y calendario del ensayo; conservar esa regla mientras no se cambie explícitamente.
6. **I03:** ensayo de una semana por ruta, comparando captura→demanda→preparación y documentando diferencias; después avanzar a registro físico.

## Límites vigentes

- No hay parser ni conexión real con WhatsApp, producción física, empaque/carga/entrega ni pagos.
- La importación diaria requiere CSV depurado con códigos y unidades explícitos; no interpreta `15 + 20` ni importa XLSM.
- Los avisos I04 y el historial conservan evidencia; el reconocimiento no registra producción/entrega física, ni garantiza atención si nadie consulta el tablero. No hay push fuera del navegador.
- La corrección conserva el estado del pedido. Un pendiente sigue excluido de demanda hasta su aceptación; un adicional sigue siendo otro pedido sujeto a revisión.
- El ticket es una condición operativa; no se generan tickets monetarios ni se marca pago al imprimir.
- Los campos vacíos y las fechas arrastradas del Excel no se convierten en valores operativos por suposición.
- Validación técnica, carga privada, despliegue y uso real son hitos distintos.
