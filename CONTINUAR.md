# Continuar PRODIMT

Última sesión: 1 de noviembre de 2026. Zona operativa: `America/Mexico_City`.

Este archivo es el punto de entrada para retomar el proyecto en otro chat o equipo. El repositorio conserva código, decisiones y verificaciones; los datos reales del piloto se mantienen privados.

## I12 — Bloqueo identificado: api.env 0640; ajuste puntual pendiente

El usuario verificó el ZIP I12 y ejecutó la revisión `20261101-access-token-management-install1`. El instalador terminó `failed/read-only-preflight`, paso `running-service`, con `environment_file_identity_unexpected`, sin cambios, migraciones, clave o reinicio. El diagnóstico posterior de sólo metadatos confirma archivo regular, root:www-data, enlace único, ruta canónica y modo `0640`. La única condición incompatible de esta guarda es el permiso: I12 exige `0600`, mientras la cadena anterior admitía `0640`.

Se indica ajustar únicamente el permiso de `/etc/prodimt-pedidos/api.env` a `0600` y volver a ejecutar el instalador original con el payload ya verificado/subido. El servicio nativo usa EnvironmentFile de systemd; la lectura de configuración por el gestor no requiere acceso del grupo www-data. No cambiar contenido, propietario, clave, unidad o paquete, ni añadir reinicio separado: el instalador conserva preflight, respaldo SQL antes de migrar y verificación de clave efectiva tras su reinicio. Corrección e instalación pendientes del reporte del usuario; no se operó el VPS desde este entorno. I11 continúa siendo el último despliegue confirmado y este intento no revocó tokens. `health_verified=false` del intento detenido significa comprobación no alcanzada.

## I12 — Gestión de tokens activos validada y empaquetada; aplicación pendiente

El usuario autoriza revocar todos los tokens anteriores y crear los nuevos a demanda. Se implementa listado de accesos habilitados/no vencidos, copia del token o enlace por fila, edición precargada de descripción/vigencia conservando el enlace y apertura individual de WhatsApp para reenvío. El teléfono sigue siendo del cliente. No mostrar historial ni eliminar auditoría.

Los tokens nuevos conservarán hash de autenticación y una copia cifrada con clave dedicada persistente fuera de los releases/SQL. Recuperación sólo ante acción administrativa explícita, sin secretos en el listado, caché, logs o auditoría. Edición versionada para evitar sobrescrituras; revocados/vencidos no se reactivan. La migración trece revoca los accesos heredados con auditoría en transacción y conserva sus registros. No se envían mensajes automáticamente ni se reproducen pedidos.

Base de código y despliegue: I11 confirmado, backend/frontend prices-148a52dd7428 y doce migraciones. Rama de trabajo codex/i12-access-token-management desde 7e87a624b10597ead05d9dc4100c8d11c5af999c. Actualización nativa I12 prevista con respaldo SQL verificado, copia privada de configuración, clave estable, migración 12→13 y comprobación de salud/archivos. Un fallo no restaura SQL ni pierde la clave de cifrado. No se ha aplicado revocación o modificación remota desde este entorno. Código `65560ca3cf98f3a1cc44e7f3ef50d4276e1f3df8`, [PR #4](https://github.com/romanrfhack/pedidosProdimt/pull/4), rama codex/i12-access-token-management sobre I11. [CI final 41350276236](https://github.com/romanrfhack/pedidosProdimt/actions/runs/41350276236) con cuatro trabajos correctos: 335 Application/Domain, 66 del proyecto SQL Server/criptografía, 121 E2E y 288 Python más smoke Production. Capturas PC/tableta/móvil e integridad del ZIP revisadas. Paquete PRODIMT_Gestion_Tokens_I12.zip, 7,304,892 bytes, SHA256 `ee52910b3d1d0be45241a4d7accb47c86f31ce6b45bbafc3b7c7369a97b676df`. Instalación y revocación remotas aún pendientes; I11 sigue siendo el último despliegue confirmado. Al aplicar I12 se revocan los accesos anteriores autorizados y se crean nuevos a demanda. No repetir I09/I10/I11 ni reparaciones/importaciones para esta actualización. Ver `docs/proyecto/INCREMENTO-12.md`.

## I11 instalado — 1 de noviembre de 2026

El usuario ejecutó el instalador revisión 2 verificado y compartió dos salidas: lectura planned/completed con preflight_step=completed, database_mismatches vacío y once migraciones; después --apply terminó updated/completed. La operación migró once→doce versiones, verificó respaldo, esquema, conteos existentes, permisos anexables del historial, salud de API y frontend. Configuración conservada; contraseñas sin cambio. Hubo reinicio únicamente de PRODIMT. No se restauró SQL automáticamente.

Fuente de aplicación desplegada `148a52dd7428cd1f948441f6709407a22be5944b`; backend/frontend activos `prices-148a52dd7428`. Actualizador `20261101-commercial-catalog-install2` (fuente `152cfc95ab2d68cf8f32c9867b4305bb1a329628`). Snapshot reportado `/var/backups/prodimt-pedidos/commercial-catalog-update-20261101T194810Z-6963d06f26a1`; respaldo SQL verificado `/var/opt/mssql/backup/prodimt-i11-20261101T194810Z-6963d06f26a1/ProdimtPedidos-copy-only.bak`. Se trata de evidencia suministrada por el usuario; no se accedió por SSH desde este entorno.

Esquema comprobado: tres tablas nuevas, 27 columnas válidas, tres índices únicos, tres columnas del índice de vigencia, dos checks y cinco FKs confiables. Precio e historial: lectura/inserción permitidas; UPDATE/DELETE de revisiones y recibos denegados, DELETE de cabeceras denegado. Los once de database_checks pertenecen al preflight; migrations_after=12 y migrated_schema_verified=true confirman el resultado final. existing_data_counts_preserved, price_history_append_only_verified, health_verified y frontend_verified en true.

products_consolidated=false y prices_preloaded=false son esperados: la instalación habilita las pantallas y conserva los datos. Pendientes revisión/confirmación de equivalencias, precios reales y aceptación funcional. Recargar Ctrl+F5, Catálogos → Productos → Revisar productos duplicados; comenzar por un grupo conocido, revisar misma identidad/unidad e impacto y confirmar nombre compartido/motivo. Revisar y aprobar perfiles afectados; en configuración del cliente abrir Precio e historial para registrar importe real por unidad, vigencia y motivo. Pedidos nuevos fijan precio; los existentes y sus correcciones conservan la tarifa aplicada. Históricos sin precio siguen pendientes. No repetir instaladores, importaciones o reparaciones por este reporte.

## I11 — Comprobación previa detenida y actualizador revisión 2

El usuario verificó el ZIP original `0f97c7bb...`, copió sus archivos y ejecutó sólo lectura. Resultado: failed/read-only-preflight/local_operation_failed, changes_made=false, sin migraciones, reinicio, cambio de contraseñas, precios o consolidaciones. I11 no quedó instalado.

Se reproduce el defecto del instalador: no configuraba QUERIES del inspector, que rechaza PERMISSION_QUERY con InspectionError(sql_query_not_authorized) antes de ejecutar SQL. La excepción terminaba como error local genérico. Revisión `20261101-commercial-catalog-install2`, fuente `152cfc95ab2d68cf8f32c9867b4305bb1a329628`: autoriza únicamente los tres SELECT fijos de I11, conserva el rechazo de cualquier otra consulta, informa paso de preflight y muestra sólo códigos fijos del inspector. No relaja destino, permisos, configuración, respaldo o validaciones.

273 Python locales correctas; cuatro regresiones nuevas recorren proceso/configuración/inspector/guardia SQL/JSON y health sin sustituir query_sql. Los dos recorridos de éxito fallan en la revisión anterior con sql_query_not_authorized y pasan en la nueva. Se comprueban I09 con modal I10, already_current con los tres SELECT, rechazo de SQL ajeno y redacción de salidas privadas. [CI final 41340426602](https://github.com/romanrfhack/pedidosProdimt/actions/runs/41340426602) con cuatro trabajos correctos: 273 Python, 323 .NET, 46 SQL Server, 114 E2E y smoke Production.

Entrega independiente `PRODIMT_Instalar_I11_v2.py`, 32,663 bytes, SHA256 `b1185a8ebb01fc139410c55f4d2d8e8a23bd709d24991d76c5fb9a7cffd659e0`. Reutiliza el payload ya copiado: SHA256 `8a43682026c7da6d3dc4a3cf0ef0e47475bf1bfe7d7883748005053e32f6d436`, fuente de aplicación `148a52dd7428cd1f948441f6709407a22be5944b`, release `prices-148a52dd7428`. Binarios y migración intactos; no recompilar o volver a cargar el payload. ZIP actualizado de 5,743,517 bytes, SHA256 `671c743b7da1b98a05dd032aa16e3e75aabd4f0de43a8b0e9c86c02c6f834c1d`; reemplaza la entrega anterior conservando su identidad/historial. Validación CRC/hashes/carga y payload idéntico completada. Archivos verificados y guardados.

Siguiente paso: descargar sólo el instalador revisión 2, verificar su hash, copiarlo al VPS y ejecutarlo sin --apply contra el payload existente. Si resulta planned/completed, aplicar con SSH -t, el mismo archivo v2 y credenciales ocultas del administrador SQL. No se accedió al VPS desde este entorno; instalación I11 y aceptación siguen pendientes. Ver evidencia I11 y apartado de incidente al final.

## I11 — Aplicación validada y entrega inicial (antecedente)

Implementado en `codex/i10-shared-products-prices`, PR #3 en borrador sobre la rama I09. Un producto físico puede atender a varios clientes; cada cliente conserva posiciones, cantidades sugeridas, abreviaturas y máquinas, con su propia tarifa. La consolidación exige revisar equivalencia/impacto y motivo; archiva duplicados como equivalencias y conserva pedidos. El editor de precio registra importe MXN por unidad, vigencia, instante de registro, usuario y motivo en revisiones anexadas. Nuevas partidas fijan el precio aplicado; cambios posteriores y correcciones de cantidad conservan esa tarifa. Precio faltante sigue pendiente, no cero. No hay cobros, saldos, impuestos o facturación.

Código `148a52dd7428cd1f948441f6709407a22be5944b`; [CI 41329868536](https://github.com/romanrfhack/pedidosProdimt/actions/runs/41329868536) con cuatro trabajos correctos: 323 .NET, 46 SQL Server, 114 E2E, 269 Python y smoke Production. Los nueve casos SQL nuevos cubren concurrencia, precio histórico, autorización, rollback de precio/consolidación y el esquema/permisos exactos del instalador. Build Angular y publicación linux-x64 correctos. Se integra la rama base `9998e30` con modal de tokens I10, cuyo alcance/documentación se conservan; por eso productos/precios quedan como I11. I09 instalado es el último backend confirmado por reporte del usuario. El modal I10 independiente no necesita instalarse antes de I11.

Paquete `PRODIMT_Productos_Precios_I11.zip`, 5,742,775 bytes, SHA256 `0f97c7bb460b92c1070d9e0f333e49bbe1e60b1f4bcbf867754e0801affb463c`; payload `8a43682026c7da6d3dc4a3cf0ef0e47475bf1bfe7d7883748005053e32f6d436`. Actualizador nativo con preflight real de lectura, respaldo COPY_ONLY/CHECKSUM verificado antes de once→doce migraciones, permisos anexables del historial, snapshot de configuración y recuperación de enlaces ante fallo. Acepta I09 con/sin modal I10, además de las bases I07/I08 conocidas; no instala Docker ni modifica otros servicios/Nginx/contraseñas. Nuevo backend/frontend `prices-148a52dd7428`. La instalación no consolida productos, registra pedidos ni precarga precios.

Entrega verificada y guardada; instalación I11, consolidación real y captura de precios reales pendientes del usuario. No se operó el VPS. Resolver guardados inciertos antes de reiniciar. Aplicar LEEME del ZIP, revisar `updated/completed`, doce migraciones y verificaciones de salud/conteos/permisos; luego Ctrl+F5. En Productos usar «Revisar productos duplicados», revisar y consolidar cada equivalencia; volver a aprobar perfiles afectados. En Configuración del cliente abrir Precio e historial y registrar tarifas reales. No reimportar catálogos antiguos ni reinstalar después un frontend anterior. Ver [contrato I11](docs/proyecto/INCREMENTO-11.md) y [evidencia](docs/validation/2026-11-01-commercial-catalog.md).

## I10 — Tokens en modal responsivo, validado y empaquetado

El usuario solicita ampliar Tokens de acceso tras instalar I09. Implementado modal nativo amplio con configuración/vista previa en PC, contenido apilado en tableta y móvil, historial a todo el ancho y cuerpo desplazable con cabecera/cierre visibles. Escape/fondo/Cerrar restauran foco; operaciones pendientes bloquean el cierre. Borrador y enlace permanecen al cerrar/reabrir; se limpian al cambiar de configuración como antes. Mensajes de revocación visibles dentro del modal. Sin contratos API/SQL o reglas de negocio nuevas.

Código `89b51ddc3308b42e5ab9b4e0532826b568beb684`. Build Angular y 250 pruebas Python correctas (12 nuevas del actualizador); [CI 41323199461](https://github.com/romanrfhack/pedidosProdimt/actions/runs/41323199461) con cuatro trabajos correctos: 103 E2E, 305 .NET, 37 SQL Server y smoke Production. Capturas PC/tableta/móvil revisadas y controles accesibles; se corrigió la altura del botón de acceso a 1366×768 sin relajar los criterios. Actualizador nativo I10 sólo frontend sobre I09 exacto, sin reinicio, con snapshot y reversión de frontend. ZIP de 180,442 bytes, SHA256 `1ff5df6abbf1ce0066662c1dccb88e1ff503f4291f8fc9754d52c5434f0657fc`, payload y exportación verificados. No se ha instalado I10 en el VPS ni confirmado aceptación del usuario. Ver `docs/proyecto/INCREMENTO-10.md` y `docs/validation/2026-11-01-token-modal.md`. I09 sigue siendo el despliegue confirmado.

## I09 instalado — 1 de noviembre de 2026

El usuario reportó la reparación `20261101-customer-channel-repair1` con `repaired/completed`: canal Customer creado y verificado, auditoría insertada y conteos existentes preservados. No ejecutó migraciones ni reinició el servicio durante la reparación. Después, el instalador original `20261031-customer-access-install1` terminó `updated/completed`, con `service_restarted`, `health_verified` y `frontend_verified` en true. El preflight ya encuentra Customer y AdminManualCapture válidos, once migraciones y todos los permisos esperados. La instalación no ejecutó escrituras SQL, migraciones ni cambios de contraseña; la reparación previa sí insertó el canal y su auditoría.

Código desplegado `6cfdc7ff366a2caa7dac399b9842b3630a30328a`. Backend y frontend activos en `customer-access-6cfdc7ff366a`. Snapshot privado reportado: `/var/backups/prodimt-pedidos/customer-access-20261101T150922Z-7dc011800110`. Evidencia suministrada por el usuario; no se accedió por SSH desde este entorno. La instalación está confirmada por ese reporte; la aceptación funcional del cliente sigue pendiente. `application_save_verified=false` de la reparación es esperado: esa herramienta no crea pedidos.

Próximo paso: recargar con Ctrl+F5 y volver a iniciar sesión si se solicita. En Catálogos → Clientes → Configuración → Tokens de acceso, guardar teléfono si falta y crear el enlace con descripción y vigencia. Abrirlo en el teléfono o una ventana privada para comprobar acceso automático, fecha de entrega y captura rápida/manual; con un pedido real autorizado, comprobar su registro único en administración. Abrir WhatsApp prepara el mensaje y el operador decide enviarlo; no hay envío automático. No repetir instaladores, reparación o importaciones.

El corte de fecha usa Ciudad de México: antes de las 18:00 entrega el mismo día; desde las 18:00 el siguiente. La madrugada pertenece al día calendario actual. El corte no sustituye la revisión tardía de las 10:00. Si cambia la fecha entre vista previa y confirmación, el portal conserva cantidades y pide confirmar otra vez. Tokens históricos conservados; sesiones cliente anteriores requieren reautenticación una vez.

Implementación y CI de aplicación: `6cfdc7f`, cuatro trabajos correctos en CI 41218846462 (305 .NET, 94 E2E, 217 Python, 33 SQL Server y smoke Production). Reparación: `7d44699`, CI 41318844811 con cuatro trabajos correctos (238 Python, 37 SQL Server, 305 .NET, 94 E2E y smoke Production). Los intentos previos se detuvieron en preflight por Customer ausente; el diagnóstico lo aisló sin mutaciones y la reparación puntual resolvió esa condición. Ver `docs/validation/2026-10-31-customer-access.md` y `docs/validation/2026-11-01-customer-channel-repair.md`. El PR #2 sigue en borrador; despliegue no equivale a aceptación de negocio.

## I08: configuración amplia del cliente y calendario de trabajo

El usuario autorizó el rediseño enfocado el 31 de octubre: usar ancho disponible con márgenes, vista habitual sin scroll de página en laptop, adaptación a tablet/teléfono y casillas de los siete días (marcada trabaja, sin marcar descansa). Implementado en frontend con los contratos existentes de I06/I07, sin cambios de API/esquema. Ver `docs/proyecto/INCREMENTO-08.md` y `docs/validation/2026-10-31-client-configuration.md`.

El calendario también se aplica en Pendientes según la fecha de servicio elegida: oculta descansos por defecto y permite mostrarlos al final para una captura excepcional. El cruce usa restDays persistidos; no cambia el contrato de la API ni genera NoOrder. Conserva borradores/referencias de captura.

Código be3995fdce6948ff00f44edd6025a1da1899887c, [CI de cierre](https://github.com/romanrfhack/pedidosProdimt/actions/runs/41217210669) con cuatro jobs correctos: 79 E2E, 206 Python, 267 .NET, 31 SQL Server y smoke Production. Build Angular, 79 E2E locales y 206 Python correctos. Paquete PRODIMT_Configuracion_Cliente_I08.zip verificado y guardado: 170004 bytes, SHA256 5d950c83bcb59ff8b14dda9fb556dfdfe3205b79bd6d9f27dc68e966da2702ab; payload SHA256 3c35c329cf3434839336ffbb43ce2ee2e163cfc28b17d3e12792d4d4c4f328e3. Diez archivos frontend, incluidos avisos de terceros. Conserva backend capture-46fed604732e y cambia sólo frontend a client-config-be3995fdce69. Sin SQL, credenciales adicionales o reinicio. I08 no aplicado aún en el VPS; guardado real del pedido I07 pendiente de confirmar. Conservar cualquier captura incierta antes de recargar.

## I07: canal reparado; guardado en navegador pendiente

El diagnóstico ejecutado por el usuario confirma tres excepciones de captura `admin_sales_channel_missing`, sin códigos SQL ni denegación de permisos; servicio activo y ambos releases I07. `AdminManualCapture` faltaba en SalesChannels. La búsqueda falla antes de crear el pedido, dentro de la transacción del intento. La vista previa y aprobación del perfil ya funcionan. No reinstalar, importar otra vez ni ejecutar bootstrap.

El usuario aplicó `PRODIMT_Reparar_Canal_Admin_I07.py`, fuente `scripts/pilot/repair-native-admin-channel.py`. Agrega únicamente ese canal interno si está ausente, con auditoría atómica; conserva registros existentes. Usa la conexión de aplicación del proceso I07 y el inspector del payload original, verificado por SHA. No pide administrador SQL, migra, cambia credenciales, reinicia ni reenvía pedidos. SHA/validaciones en `docs/validation/2026-10-30-i07-capture-incident.md`.

Aplicación confirmada por JSON del usuario: status repaired, phase completed, channel_created/channel_verified/audit_inserted/existing_data_counts_preserved true. Sin migraciones, reinicio o reproducción del pedido. application_save_verified false es esperado: la herramienta no envía pedidos. Próximo paso: Reintentar guardado en la misma pestaña con la misma referencia y confirmar una sola entrega con las cantidades correctas. No aplicar Ctrl+F5 mientras siga incierto el intento ni repetir la reparación. No se ha operado el VPS desde este entorno.

## I07 desplegado — 30 de octubre de 2026

Confirmado por salida del instalador ejecutado por el usuario: revision 20261028-capture-install1, status updated, phase completed, historial 10→11, migrated_schema_verified, existing_data_counts_preserved y health_verified true. Código 46fed604732eddca0c7ac199b58bb0b429545b89. Backend/frontend activos en capture-46fed604732e. Contraseñas sin cambio; no se restauró SQL automáticamente.

- Snapshot privado: `/var/backups/prodimt-pedidos/quick-capture-update-20261031T024549Z-90dbaa60d04e`.
- Respaldo SQL verificado: `/var/opt/mssql/backup/prodimt-i07-20261031T024549Z-90dbaa60d04e/ProdimtPedidos-copy-only.bak`.

El instalador verificó esquema, permisos DML, proceso, /health/db, autorización de los endpoints y frontend/JS/CSS por HTTPS. Se trata de evidencia reportada por el usuario; no se accedió al VPS desde este entorno. El campo database_checks.migrations_count=10 pertenece al preflight; migrations_after=11 y migrated_schema_verified=true confirman el resultado final.

Siguiente paso: Ctrl+F5 y revisar Pendientes → Capturar pedido. Comprobar fecha, entrega habitual informativa, texto abreviado/desglose por posiciones y alternativa manual. Perfiles importados aún requieren confirmación explícita para guardar captura rápida; HN debe asociarse al producto correcto por cliente. Revisar primero la vista previa sin crear pedidos ficticios. Instalación no equivale a aceptación de negocio ni al ensayo de ruta. Conservar catálogos y respaldos; no repetir instalador, bootstrap o importaciones.

## Leer primero

1. `AGENTS.md`.
2. `docs/proyecto/ESTADO.md` (avance y límites verificados).
3. `docs/proyecto/PLAN.md` (objetivo integral y etapas).
4. `docs/proyecto/DECISIONES.md` (decisiones vigentes y pendientes).
5. `docs/proyecto/BITACORA.md` (cambios y pruebas por sesión).
6. `docs/proyecto/INCREMENTO-08.md` (vista amplia, calendario y actualización sólo frontend).
7. `docs/proyecto/INCREMENTO-07.md` (captura rápida y actualización nativa I06→I07).
7. `docs/proyecto/INCREMENTO-06.md` (catálogo y descansos diarios).
8. `docs/proyecto/INCREMENTO-05.md` (cambio de contraseña propia y sesiones administrativas).
9. `docs/proyecto/INCREMENTO-04.md` (avisos operativos y configuración por cliente).
10. `docs/proyecto/INCREMENTO-03.md` (instalación y preparación del ensayo).
11. `docs/proyecto/INCREMENTO-02.md` (captura diaria y correcciones).
12. `docs/proyecto/INCREMENTO-01.md` (fundamentos ya construidos).

La documentación de mayo de 2026 es antecedente. Cuando difiera, las decisiones de octubre en `docs/proyecto/` prevalecen. No confundir capacidad implementada, prueba técnica, dato validado y operación real.

## Cómo retomar

```text
Continúa el sistema PRODIMT en romanrfhack/pedidosProdimt.
Lee CONTINUAR.md y los documentos que enlaza. Revisa la rama y PR
indicados en ESTADO.md antes de cambiar código. Ejecuta las comprobaciones
pendientes y continúa el siguiente incremento, sin repetir el análisis inicial.
No publiques datos reales de clientes ni instrucciones de acceso a locales.
```

El paquete privado de preparación del piloto se adjunta al chat de esta sesión. El usuario devolvió una revisión del XLSX y autorizó I02. Su normalización distingue valores confirmados, fórmulas resueltas y campos todavía ausentes. Consultar el paquete privado I02 más reciente antes de preparar una carga; no asumir que todas las celdas o perfiles quedaron completos. No recuperar pedidos, precios o saldos actuales a partir de fotografías históricas.

I03 agrega un paquete instalable y una hoja breve de pendientes que conserva esas confirmaciones. Antes de instalar, leer `infra/pilot/README.md`. No regenerar `.env` si ya existe un volumen del piloto; recuperar las credenciales privadas. CI usa exclusivamente datos ficticios y no prueba que el VPS del usuario esté instalado.

El usuario devolvió la hoja I03 con 30 unidades y 36 posiciones completadas. La revisión privada I04 es ahora la fuente más reciente; aún contiene seis unidades vacías, máquinas desconocidas y un valor cero sin interpretación confirmada. No reutilizar la hoja vacía anterior para una carga. Los números etiquetados como «Adicional» proceden de una columna original sin encabezado: no interpretarlos como cantidades, máquina ni posición. La explicación de pedidos aumentados por teléfono es una hipótesis del usuario pendiente de confirmar sobre ese archivo, no una reclasificación de los datos históricos.

I04 quedó desplegado en el VPS el 25 de octubre: el usuario compartió `status: updated`, nueve migraciones, permisos DML y salud/proceso/frontend HTTPS verificados con `20261025-resume5`. Se conservan el respaldo previo y uno nuevo verificado; no repetir el actualizador ni el bootstrap. El siguiente paso es comprobar login/catálogos desde el navegador y preparar la vista previa de la carga privada contra los datos existentes. La instalación no importó la ruta del Excel ni completó el ensayo operativo. Ver `ESTADO.md` y la evidencia de reanudación para las rutas de releases/respaldo.

El 26 de octubre el usuario confirmó la ruta correcta del API y ejecutó el alta de `roman.prodimt`: cuenta/auditoría verificadas, login y consulta administrativa HTTPS 200. La contraseña no se copia a git ni a esta documentación. No repetir el alta: la referencia inicial deja de coincidir tras cambiar la contraseña.

I05 está implementado, validado y empaquetado para el I04 nativo vigente. Código final `886bee7b2202b399ee063fe65d524b7f444e205c`; CI 40411669397 con cuatro jobs correctos (212 .NET, 24 SQL, 55 E2E, 135 Python y smoke Production). `PRODIMT_Cambio_Contrasena_I05.zip` contiene binarios y el instalador sin migraciones; hash y límites en `docs/validation/2026-10-26-password-change.md`. El usuario ejecutó el instalador el 26 de octubre y compartió `status: updated`, `phase: completed`, `health_verified: true`; ambos releases activos son `password-886bee7b2202`. Conserva nueve migraciones y no cambió la contraseña. La conexión SSH directa sigue sin ruta: el usuario ejecuta los comandos desde WSL y comparte sólo el JSON de instalación, sin credenciales.

El 27 de octubre el usuario confirmó login en navegador y solicitó la primera revisión funcional. Se comienza por I01: Catálogos → Clientes → Configurar, con la pantalla enfocada de I04. Comparar un cliente conocido con su información confirmada: productos, unidad de venta, orden visual, posiciones por cliente, máquina y datos internos de entrega/cobro. Guardar/recargar debe conservar la configuración; cantidad sugerida no es un pedido de hoy. La instalación no importó automáticamente el Excel: conciliar lo existente antes de preparar la vista previa privada. El cambio de contraseña no está confirmado por el simple hecho de haber podido iniciar sesión.

Para comenzar esa revisión el usuario informa que no ve clientes. Se preparan los 23 clientes confirmados de I04 en `PRODIMT_Clientes_Iniciales.xlsx` y `.csv`, guardados de forma privada. Solo datos básicos: códigos, nombres, ruta, llave, cobro e instrucciones confirmadas; teléfonos, orden de visita y otros desconocidos siguen vacíos. Importar el CSV en `/admin/importacion`, Tipo Clientes, mediante Validar y después Aplicar. Con catálogo vacío se esperan 23 altas y 0 errores; los teléfonos vacíos generan advertencias. Si se edita el XLSX, exportar su hoja Clientes como CSV UTF-8. La carga de clientes fue confirmada por el usuario el 28 de octubre; no repetirla. Productos/perfiles y máquinas se prepararán por separado.

El 28 de octubre el usuario confirmó la carga de clientes y aportó capturas del catálogo poblado. Solicitó I06: búsqueda principal, modal centrado bajo demanda para alta/edición y tarjetas según estado diario/descanso. Ver docs/proyecto/INCREMENTO-06.md. No volver a pedir el alta inicial. La programación semanal es nueva y explícita; no inferir descansos a partir del Excel. I06 está implementado, validado y empaquetado: código 965dc65d82d2322b8c897a164c592bad6ed975a9, CI 40710267512 con cuatro jobs correctos, incluido ODBC real del SQL incremental 9→10. PRODIMT_Catalogos_I06.zip preparado y verificado; instrucciones/hashes en docs/validation/2026-10-28-customer-catalog.md. El usuario ejecutó I06 el 28 de octubre y compartió status updated, phase completed, health_verified true, historial 9→10, esquema migrado verificado y conteos existentes conservados. Ambos releases activos: catalog-965dc65d82d2. Snapshot y respaldo SQL verificados constan en docs/validation/2026-10-28-customer-catalog.md. No repetir instalación/importación. Siguiente paso: Ctrl+F5 y aceptación de búsqueda, modal, tarjetas y descansos con datos reales.

El usuario solicita importar también los productos y su orden. Se recuperó la hoja privada I03 devuelta usada en I04: 36 relaciones de 23 clientes, 36 posiciones explícitas, 30 unidades kg, seis vacías, 19 máquinas positivas y 17 pendientes (16 vacías y un cero sin significado). Preparado PRODIMT_Configuracion_Productos_Clientes.zip con tres CSV y Excel de revisión, sin CSV de clientes: Productos (36 identidades provisionales separadas por relación), Máquinas (1, 2 y 3) y Productos frecuentes (36 filas/23 perfiles). El orden visual inicial sigue las posiciones que el usuario indicó, como dos campos independientes. No se fusionan variantes de clientes distintos ni se usan cantidades históricas. Se validó/aplicó localmente con los binarios I06 y almacenamiento temporal, comprobando los 23 perfiles completos y cero pedidos/aprobaciones automáticas. No se modificó VPS; el usuario debe Validar/Aplicar cada CSV en ese orden y revisar reemplazos. Detalle en docs/validation/2026-10-28-product-profiles-import.md.

El usuario confirma que cargó los catálogos sin error y consulta el siguiente paso. La carga real queda confirmada por su reporte, sin conteos/JSON ni conciliación individual adjuntos. No repetir importaciones. Se recomienda iniciar captura administrativa asistida de 3–5 pedidos reales usando la funcionalidad existente: /admin/clientes-pendientes, fecha de servicio explícita, Capturar pedido, frecuentes/cantidades y notas internas; revisar /admin/pedidos, autorizar pendientes y consultar /admin/operacion. Distinguir entrega mañana de hoy: las tarjetas I06 siguen mostrando hoy. Las seis unidades y 17 asignaciones pendientes del paquete no se dan por resueltas por una importación exitosa. Empezar con líneas de unidad confirmada y máquina conocida para revisar demanda; mantener pendientes separados. Posteriormente el usuario autorizó I07 de captura rápida por posiciones explícitas y alias configurables; su implementación ya está verificada, ver el cierre siguiente.

## Al cerrar cada sesión

- Actualizar `ESTADO.md`: terminado, en curso, siguiente paso, bloqueos y rama/PR.
- Agregar una entrada a `BITACORA.md` con comandos, resultados y límites.
- Registrar decisiones nuevas en `DECISIONES.md` y actualizar el plan si cambia el alcance.
- Guardar código y documentación en un commit y comprobar su persistencia remota.
- No afirmar que el piloto está operando si sólo se ejecutaron pruebas locales.

I07 fue autorizado el 28 de octubre: leer `docs/proyecto/INCREMENTO-07.md`. Código `46fed604732eddca0c7ac199b58bb0b429545b89`, CI 40765239145: cuatro jobs correctos (267 .NET, 29 SQL, 68 E2E, 162 Python y Production). Captura por posiciones y alias explícitos, entrega informativa, revisión/guardado y manual listos. Paquete PRODIMT_Captura_Rapida_I07.zip íntegro y evidencia en docs/validation/2026-10-28-quick-capture.md. Preservar catálogos importados; instalación y aceptación pendientes. El último estado remoto operativo confirmado sigue siendo I06.


El primer intento de instalación I07 reportado por el usuario el 28 de octubre se detuvo en sql-admin-authentication con sql_credentials_invalid. El preflight confirmó diez migraciones y todos los permisos DML previstos; changes_made, database_migrations_run y migrations_may_have_changed fueron false. La validación local de entrada ocurre antes de consultar SQL como administrador: exige usuario 1–128 caracteres, contraseña 1–512 y ausencia de NUL/CR/LF. El reporte no identifica cuál campo falló ni demuestra rechazo de autenticación por SQL Server. Reintentar el mismo instalador con el login SQL existente y su contraseña introducidos en los prompts ocultos; mantener I06 como último despliegue confirmado. No hubo cambios al código/paquete ni intento remoto desde esta sesión.


## I07 — Diagnóstico SQL pendiente (30 de octubre)

El usuario reintentó el instalador original. Pasó la validación local de entrada, pero terminó en sql-admin-authentication con sql_operation_failed y sql_error_numbers vacío. Diez migraciones y permisos DML de la aplicación verificados; changes_made, database_migrations_run y migrations_may_have_changed false. No se detuvo la API ni se migró. Un fallo de sqlcmd/ODBC puede no producir encabezados Msg; el reporte no determina aún contraseña, login, conexión o consulta fallida.

Se prepara PRODIMT_Diagnostico_SQL_I07.py, que verifica el SHA exacto del instalador entregado y ejecuta sólo preflight y authenticate_admin de lectura. Bloquea comandos mutadores/SQL ajeno a una lista SELECT, clasifica mensajes ODBC sin revelarlos y muestra etapa/permisos booleanos. No reinicia servicios, migra, respalda ni restablece credenciales. 173 Python locales y remotas correctas, incluidas 11 del diagnóstico. Código cd3ba9a034a5331a1437b95118b22ced382d8ccd; CI 41105959819 con cuatro jobs correctos: 267 .NET, 29 SQL, 68 E2E, 173 Python y smoke Production. Clasificación de login rechazado comprobada con ODBC real y cuenta ficticia inexistente. Pendiente ejecución del diagnóstico por el usuario; I06 sigue siendo el último despliegue confirmado. Paquete I07/código de aplicación intactos.


## I07 — Corrección del diagnóstico, revisión 2 (30 de octubre)

El usuario ejecutó la revisión 1: diagnostic_local_failure en application-read-only-preflight, antes de pedir SQL. Se reprodujo AttributeError por DEVNULL ausente en el adaptador subprocess del diagnóstico. Era un defecto de la herramienta; no aporta diagnóstico de la contraseña ni de SQL. Se preserva DEVNULL y se publica revisión 2 (11,005 bytes, SHA256 8e7f3b99e874adb6b97ee1958311c01f4b25ddd89ba49481280528c2043a03fc).

Código 2ecd87f649bfd0ec9e68c246ce51418b422fae0c, CI 41160327219 con cuatro jobs correctos: 174 Python, 267 .NET, 29 SQL, 68 E2E y smoke Production. Incluye prueba del recorrido real preflight→process/command→inspector/config→HTTP→authenticate_admin con límites de SO/transporte ficticios, para éxito y login rechazado. La nueva regresión falla sobre la revisión 1 y pasa en la 2. Reparación in situ del archivo de diagnóstico preparada con hashes v1/v2; se probó migración, repetición sin cambios y rechazo de archivo desconocido. No modifica instalador, paquete, aplicación ni base. Pendiente reejecutar diagnóstico con el usuario y resolver después el error SQL original. I06 sigue siendo el último despliegue confirmado.


## I07 — Diagnóstico en VPS correcto; aplicación pendiente

El usuario ejecutó la revisión 2 y compartió status inspected, phase completed, preflight_completed y administrator_checks_completed true. Cinco consultas administrativas correctas: permisos, historial, esquema de descansos, ausencia esperada de alias con historial 10 y datos de descansos. Instalador de hash exacto verificado; diez migraciones y DML de aplicación correctos. Cuenta actual con sysadmin=1, dbcreator=1, db_owner=1, backup_permission=1 y view_definition=1. verify_permission=null no bloqueó la condición de administrador porque sysadmin=1. No conceder permisos por ese null.

El diagnóstico no modificó aplicación/BD ni ejecutó migraciones. El error SQL anterior no se reprodujo en esta ejecución; no se demostró su causa histórica. Siguiente paso autorizado: ejecutar el instalador I07 original y el payload ya verificados con --apply y las mismas credenciales SQL usadas en el diagnóstico. No requiere nueva descarga, bootstrap, restauración ni opción resume (no hubo cambios y el esquema base 10 fue verificado). El instalador debe respaldar/verificar SQL antes de 10→11 y comprobar salud/conteos después. I06 continúa como último despliegue confirmado hasta recibir status updated y migrations_after 11.
