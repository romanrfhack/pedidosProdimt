# Estado del proyecto

Actualizado: 25 de octubre de 2026.

## Ubicación y etapa

- Repositorio: `romanrfhack/pedidosProdimt`.
- [PR #1](https://github.com/romanrfhack/pedidosProdimt/pull/1), rama `codex/pilot-foundation-2026-10-24`. Borrador, sin fusión ni despliegue.
- Base de I02: `a2eb7fb533982ba719ce74d0a73b4f631cd0d2f9` (I01 verificado).
- Código I02 verificado: `192c8d7871e2b2021b4d485dd71b5ebd7abbda4d`; [CI](https://github.com/romanrfhack/pedidosProdimt/actions/runs/40120275407). El commit documental posterior conserva el mismo código.
- **I02 implementado y validado en CI, incluido SQL Server real**: captura/importación diaria y correcciones. Contratos en `INCREMENTO-02.md`.
- **I03-A implementado y validado en CI**, código `26eb6d8d47786bb18f2fe8117596e74f4f277430`; [cuatro jobs correctos](https://github.com/romanrfhack/pedidosProdimt/actions/runs/40123481595). Instalación reproducible Linux/WSL, SQL aislado, acceso por túnel y credenciales propias. Hitos separados en `INCREMENTO-03.md`; instrucciones en `../../infra/pilot/README.md`.
- **I04 implementado y validado en CI**, código `b630072ce67a691c3757d0590cc7f7d45f423d52`; [cuatro jobs correctos](https://github.com/romanrfhack/pedidosProdimt/actions/runs/40130113089). Avisos persistentes de cambios de demanda, configuración enfocada por cliente y control de edición simultánea. Contratos y límites en `INCREMENTO-04.md`.
- **Etapa 1 en desarrollo; preparación operativa de etapa 0 aún incompleta.**
- I04 no está desplegado. El diagnóstico del usuario confirma una instalación PRODIMT previa activa; sus datos y uso operativo todavía requieren inspección. La ruta privada I04 no se ha cargado desde esta sesión.

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
- I03-B prepara un actualizador para ese despliegue exacto: payload verificado, credenciales administrativas ocultas sólo en la sesión, respaldo COPY_ONLY/CHECKSUM verificado antes de migrar y cambio/reversión de releases. No se ha ejecutado la fase de escritura en el VPS.
- Primer intento `--apply` del usuario detenido en `read-only-preflight` con `environment_file_format_unsupported`, `changes_made: false` y `migrations_may_have_changed: false`. El lector anterior sólo admitía un subconjunto de EnvironmentFile; se amplía siguiendo systemd 255, con diagnóstico de sintaxis sin claves ni valores. La configuración efectiva de reinicio sigue debiendo coincidir con la del proceso activo antes de autenticar SQL o preparar releases. No se conoce todavía la construcción exacta del archivo que disparó el bloqueo; no se solicita ni modifica su contenido privado. Evidencia en `../validation/2026-10-25-native-env.md`.

## Siguiente paso

1. Reemplazar únicamente el actualizador por la revisión `20261025-env2` y repetir la actualización nativa I03-B con el mismo ZIP, tras respaldo/verificación con una identidad SQL administrativa independiente. El primer intento no llegó a modificar configuración, releases ni esquema. El inspector del usuario ya confirmó configuración compatible, runtime, cinco migraciones y ausencia de pedidos/duplicados; la cuenta cotidiana carece de DDL y respaldo. El actualizador debe conservar configuración, base, Nginx y certificado, con reversión de releases ante fallo. El kit Docker queda para una instalación nueva.
2. Cargar mediante vista previa los catálogos privados ya preparados. No publicar ni sembrar clientes reales en CI.
3. Completar las seis unidades, máquinas faltantes/cero y orden de visita. Las posiciones de interpretación ya fueron llenadas, pero la aprobación del perfil sigue siendo explícita; cobros y saldos se completan para su etapa.
4. Ratificar corte de 10:00 a.m. y calendario del ensayo; conservar esa regla mientras no se cambie explícitamente.
5. **I03:** ensayo de una semana por ruta, comparando captura→demanda→preparación y documentando diferencias; después avanzar a registro físico.

## Límites vigentes

- No hay parser ni conexión real con WhatsApp, producción física, empaque/carga/entrega ni pagos.
- La importación diaria requiere CSV depurado con códigos y unidades explícitos; no interpreta `15 + 20` ni importa XLSM.
- Los avisos I04 y el historial conservan evidencia; el reconocimiento no registra producción/entrega física, ni garantiza atención si nadie consulta el tablero. No hay push fuera del navegador.
- La corrección conserva el estado del pedido. Un pendiente sigue excluido de demanda hasta su aceptación; un adicional sigue siendo otro pedido sujeto a revisión.
- El ticket es una condición operativa; no se generan tickets monetarios ni se marca pago al imprimir.
- Los campos vacíos y las fechas arrastradas del Excel no se convierten en valores operativos por suposición.
- Validación técnica, carga privada, despliegue y uso real son hitos distintos.
