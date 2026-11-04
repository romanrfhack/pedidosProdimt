# Auditoría profunda PRODIMT y reconciliación documental

> Corte original sobre aa802 y la evidencia disponible al auditar. El reporte posterior confirma I23 instalado y cierra F03. Para correcciones y estado actual consultar [C1](../../corrections/C1.md) y [ESTADO](../../proyecto/ESTADO.md); las condiciones originales se conservan como trazabilidad.

Fecha de corte: 3 de noviembre de 2026, Ciudad de México. Base de fuentes: `aa802e210d15c70784f79dea2580709166b843c7` (I23 revisión 3, incluye RF1).

## Dictamen

La base es recuperable y apropiada para un monolito modular. Hay separación real de capas, DI, RBAC vigente, cifrado de accesos y ledgers transaccionales. No se recomienda una reescritura ni incorporar un framework sólo para etiquetar la arquitectura como SOLID. Antes de nuevas funciones conviene cerrar pérdida de trabajo, ambigüedad de contexto y validación de entrega; después mejorar coherencia visual, lecturas y cohesión.

Se registran 24 hallazgos: tres P1 de aplicación/entrega abiertos, quince P2 abiertos y cuatro P3 de mejora. Dos contradicciones documentales se corrigen en esta entrega (un P1 y un P2). No se confirmó un P0 ni un compromiso de seguridad. Una prioridad no equivale a un incidente ocurrido.

## Base, alcance y límites

- Instalado confirmado por reporte del usuario: backendI22 `delivery-0cfe3ef511d8`, frontendRF1 `delivery-flow-426c360e1365`; historia 17 en el último control reportado. Comprobación pública actual del índice RF1 y salud 200/BD reachable. No se consultó SQL productivo ni cuentas/clientes autenticados.
- Código más reciente disponible: I23 aa802, verificado desde el paquete de fuentes conservado, SHA del bundle `79676396b887e3c0d9f70690ca8c2dedb395b496d4837fc5f0970b1b87c832e1`. Propone migración 18, no se declara instalada.
- Evidencia WSL de aa802:450 pruebas Application correctas, compilación/publicación y EF sin diferencias; un intento SCP falló. Una continuación posterior reportó bloqueo SQL aislado `validation_expected_rollback_not_observed`, antes de cambios operativos. La causa SQL exacta sigue pendiente.
- Auditoría estática y reproducciones sintéticas, sin explotar el servidor ni crear pedidos/rutas/entregas. Navegador y SQL real tienen límites explícitos en [validación](VALIDACION.md). No se verificó aceptación humana de todo el recorrido.
- No se añaden cobranza, firma/foto, inventario de envases, offline o mensajería automática. Notas de impago/cajas no constituyen esos módulos.

## Hallazgos con evidencia

Las rutas y líneas corresponden a la baseaa802, antes de la reconciliación documental. Para lo instalado se verificó aplicabilidad respecto de RF1. “Pendiente de SQL/navegador” no se presenta como prueba ejecutada.

### F01 | P1 | Reintento incierto de producción pierde su identidad al navegar

**Ámbito:** RF1 e I23. **Estado:** Pendiente. **Evidencia:** Código confirmado; impacto condicionado.

**Referencias:** `apps/prodimt-pedidos-web/src/app/admin-operations.component.ts:181`, `apps/prodimt-pedidos-web/src/app/admin-operations.component.ts:204`, `apps/prodimt-pedidos-web/src/app/app.routes.ts:31`.

El POST puede confirmar en servidor y perder su respuesta. El comando y UUID quedan sólo en editor.submitted; cambiar de pantalla o recargar destruye la referencia. Volver a capturar usa otro UUID y puede duplicar un lote. No se constató un incidente real.

**Acción mínima:** Persistir un DTO pendiente por usuario antes de enviar y recuperar/repetir el mismo UUID. Reutilizar el patrón de Reparto; añadir protección de navegación sin bloquear el logout.

**Aceptación:** Commit con respuesta perdida + navegación/recarga + reintento produce exactamente un lote o una asignación, según el comando; la referencia se conserva y no se reutiliza entre usuarios.

**Fase:** 1.

### F02 | P1 | Login administrativo sin límite de intentos en aplicación

**Ámbito:** RF1 e I23. **Estado:** Pendiente. **Evidencia:** Código confirmado; impacto condicionado.

**Referencias:** `src/Prodimt.Pedidos.Api/Program.cs:71`, `src/Prodimt.Pedidos.Api/Program.cs:298`, `src/Prodimt.Pedidos.Application/Auth/PilotAuthenticationService.cs:61`.

El endpoint anónimo de administración no aplica la policy de rate limiting que sí usa el acceso de clientes. No hay contador/limitación equivalente en el servicio. El perímetro real no fue inspeccionado; no se intentó explotar el servidor.

**Acción mínima:** Policy configurable de límites administrativos, validación de longitudes antes del hash y eventos sanitizados. Evitar un bloqueo permanente de cuenta que permita denegar servicio a terceros.

**Aceptación:** HTTP aislado devuelve 429 al exceder la ventana, permite recuperación y login legítimo; no registra contraseñas ni tokens. Revocación y RBAC siguen correctos.

**Fase:** 1.

### F03 | P1 | Gate SQL de I23 detenido sin causa SQL suficientemente visible

**Ámbito:** Entrega I23, sin aplicar a producción. **Estado:** Pendiente. **Evidencia:** Código confirmado; impacto condicionado.

**Referencias:** `scripts/pilot/validate-ordering-schedules-sql.py:63`, `scripts/pilot/validate-ordering-schedules-sql.py:135`, `scripts/pilot/install-ordering-schedules-on-delivery-update.py`.

El último reporte disponible señala isolated-exact-sql-validation / validation_expected_rollback_not_observed antes de migrar. El validador colapsa un retorno/marker inesperados en el mismo error. La causa exacta no quedó demostrada y no debe atribuirse a una sentencia por conjetura.

**Acción mínima:** Diagnóstico acotado de exit code, presencia de marker y números SQL saneados; reproducir el SQL empaquetado exacto en una base temporal. Conservar el requisito de rollback, repetición, limpieza y respaldo.

**Aceptación:** CI SQL nativo demuestra fallo intencionado y rollback, aplicación, replay, permisos y limpieza. Una falla previa explica su fase sin secretos. Sólo entonces puede prepararse otra entrega.

**Fase:** 0.

### F04 | P2 | Auditoría de un pedido puede mostrarse bajo otro

**Ámbito:** RF1 e I23. **Estado:** Pendiente. **Evidencia:** Reproducido sin navegador.

**Referencias:** `apps/prodimt-pedidos-web/src/app/admin-pending.component.ts:306`.

Dos lecturas A y B terminan B y A; la respuesta de A sobreescribe auditEvents cuando selectedAuditOrderId ya es B. Reproducción del método TypeScript real con transporte sintético: selected=B / events=A.

**Acción mínima:** Cancelar lectura previa o usar generación/switchMap, verificar ID y fecha antes de publicar y limpiar al destruir la vista.

**Aceptación:** Respuestas invertidas, cambio de fecha y salida de vista no muestran historia ajena ni actualizan una vista destruida.

**Fase:** 1.

### F05 | P2 | Guardar un horario descarta borradores de otras tarjetas

**Ámbito:** Sólo I23 preparado. **Estado:** Pendiente. **Evidencia:** Reproducido sin navegador.

**Referencias:** `apps/prodimt-pedidos-web/src/app/admin-ordering-schedules.component.ts:23`, `apps/prodimt-pedidos-web/src/app/admin-ordering-schedules.component.ts:51`.

Los inputs modifican data directamente; guardar A vuelve a cargar todo y sustituye un borrador B. Reproducción con métodos reales: B editado a 08:00 vuelve a 10:00 al guardar A. Durante el GET saving ya es false.

**Acción mínima:** Separar snapshot y borradores por ID o usar editor único. Reconciliar sólo la entidad guardada, controlar dirty/loading y preservar el borrador ante 409.

**Aceptación:** Editar A+B y guardar A conserva B; GET lento no pisa cambios ni permite enviar una versión obsoleta.

**Fase:** 1.

### F06 | P2 | Auditoría de perfiles puede superar el límite SQL de 2000 caracteres

**Ámbito:** RF1 e I23. **Estado:** Pendiente. **Evidencia:** Contrato incompatible confirmado; SQL pendiente.

**Referencias:** `src/Prodimt.Pedidos.Application/AdminCatalogs/AdminCustomerCatalogService.cs:273`, `src/Prodimt.Pedidos.Infrastructure/Persistence/Configurations/AuditLogConfiguration.cs:35`, `src/Prodimt.Pedidos.Infrastructure/Repositories/EfAuditLogRepository.cs:9`.

Se serializan productIds, captureAliases y machineInheritance. Un ejemplo sintético sin aliases con 9 productos genera 2131 caracteres frente a nvarchar(2000). Incompatibilidad estructural confirmada; error SQL no reproducido aquí y tamaños reales no inspeccionados.

**Acción mínima:** Definir evidencia completa con tamaño coherente: resumen acotado con referencia versionada o ampliación SQL aditiva con límites deliberados. Revisar también auditoría de seguridad. No truncar JSON silenciosamente.

**Aceptación:** SQL Server guarda perfiles de 9/20 productos y Unicode con perfil, versión y auditoría atómicos; un fallo deliberado revierte todo.

**Fase:** 1.

### F07 | P2 | Revisión no valida notas según los límites persistidos

**Ámbito:** RF1 e I23. **Estado:** Pendiente. **Evidencia:** Código confirmado; impacto condicionado.

**Referencias:** `src/Prodimt.Pedidos.Application/AdminOrders/AdminOrderService.cs:238`, `src/Prodimt.Pedidos.Application/AdminOrders/AdminOrderService.cs:345`, `src/Prodimt.Pedidos.Infrastructure/Persistence/Configurations/OrderConfiguration.cs:25`.

InternalNotes y DeliveryNotes se asignan sin validar 500/1000 caracteres aunque SQL establece esos máximos. El formulario de revisión tampoco limita la entrada. Un error SQL/500 es inferido, no ejecutado.

**Acción mínima:** Validar el comando completo antes de mutar, con límites compartidos por captura/revisión/corrección y errores de campo consistentes.

**Aceptación:** Valores en límite guardan, límite+1 retorna 400 sin cambios parciales; el mensaje identifica el campo y conserva el borrador.

**Fase:** 1.

### F08 | P2 | NoOrder heredado y moderno operan con fechas distintas

**Ámbito:** RF1; semántica de martes nueva en I23. **Estado:** Pendiente. **Evidencia:** Código confirmado; impacto condicionado.

**Referencias:** `src/Prodimt.Pedidos.Application/CustomerOrders/CustomerOrderService.cs:90`, `src/Prodimt.Pedidos.Application/CustomerOrders/CustomerOrderCaptureService.cs:122`, `src/Prodimt.Pedidos.Application/Auth/CustomerLinkAccessPolicy.cs:18`.

La ruta heredada usa Today repetidamente y no un recibo durable; la moderna exige fecha esperada y aplica corte. Después de 18:00 pueden operar sobre días distintos; I23 añade martes inhábil. La UI moderna no usa esa ruta y request-only no puede invocarla: no es un bypass de demanda.

**Acción mínima:** Decidir compatibilidad del endpoint legacy y unificar el caso de uso o devolver un conflicto orientativo antes de escribir. Deprecar gradualmente sin retirar consumidores a ciegas.

**Aceptación:** HTTP antes/después de 18:00, lunes/martes, enlace activo/restringido y replay preservan la fecha confirmada y no reescriben historia.

**Fase:** 1.

### F09 | P2 | Operación pierde la fecha al abrir Revisión

**Ámbito:** RF1 e I23. **Estado:** Pendiente. **Evidencia:** Código confirmado; impacto condicionado.

**Referencias:** `apps/prodimt-pedidos-web/src/app/admin-operations.component.html:100`, `apps/prodimt-pedidos-web/src/app/admin-pending.component.ts:195`.

Operación permite una fecha histórica pero su enlace a Revisión no pasa ?date. Revisión toma hoy si falta y puede abrir otro día.

**Acción mínima:** Pasar current.date del plan ya cargado y conservar contexto al volver; no usar una fecha editada todavía no consultada.

**Aceptación:** Consultar un día distinto y navegar a Revisión conserva exactamente el día del plan.

**Fase:** 1.

### F10 | P2 | Pedido capturado sin ruta carece de una resolución operativa explícita

**Ámbito:** RF1 e I23. **Estado:** Pendiente. **Evidencia:** Código confirmado; impacto condicionado.

**Referencias:** `src/Prodimt.Pedidos.Domain/Entities/Order.cs:100`, `src/Prodimt.Pedidos.Application/Delivery/DeliveryManagementService.cs:142`, `src/Prodimt.Pedidos.Application/AdminOrders/DailyOrderDtos.cs:4`.

La ruta se fija como snapshot. Asignar exige coincidencia con la vuelta; editar al cliente sólo afecta pedidos futuros y la corrección actual admite partidas, no ruta. Un pedido sin ruta o con una equivocada puede quedar bloqueado.

**Acción mínima:** Primero prevenir/detectar ruta ausente antes de capturar y explicar la salida soportada. Diseñar una resolución auditada mínima de un pedido no comprometido sólo si se confirma esa necesidad; no aplicar fallback silencioso al cliente.

**Aceptación:** Caso sin ruta y ruta equivocada tiene una salida documentada sin borrar/recrear historia ni afectar reservas/custodia. Una corrección eventual exige motivo/versión y rechaza pedidos comprometidos.

**Fase:** 3.

### F11 | P2 | Maps e instrucciones de parada proceden sólo de la primera partida

**Ámbito:** RF1 e I23. **Estado:** Pendiente. **Evidencia:** Código confirmado; impacto condicionado.

**Referencias:** `apps/prodimt-pedidos-web/src/app/delivery-workspace.component.html:59`, `src/Prodimt.Pedidos.Application/Delivery/DeliveryManagementService.cs:271`.

La cabecera usa assignments[0], pero actualizar indicaciones modifica una asignación. Con dos productos, cambiar la segunda puede dejar la cabecera antigua u ocultar snapshots divergentes.

**Acción mínima:** Detectar diferencias y mostrar su alcance por partida, o aplicar una actualización explícita de parada a todas las partidas elegidas. Conservar snapshots históricos y reglas de permisos.

**Aceptación:** Dos productos con indicaciones diferentes no ocultan información ni usan un Maps ambiguo; actualizar no cambia historia sin evento.

**Fase:** 3.

### F12 | P2 | Asignar producción se interpreta como cambio comercial en reparto

**Ámbito:** RF1 e I23. **Estado:** Pendiente. **Evidencia:** Código confirmado; impacto condicionado.

**Referencias:** `src/Prodimt.Pedidos.Application/AdminOperations/ProductionManagementService.cs:134`, `src/Prodimt.Pedidos.Application/Delivery/DeliveryManagementService.cs:186`, `src/Prodimt.Pedidos.Application/Delivery/DeliveryManagementService.cs:427`.

Producción incrementa Order.Version; reparto compara esa versión como cambio del pedido. Sin cambiar cantidad ni destino, una asignación/reversión técnica puede exigir reconocer una supuesta modificación comercial.

**Acción mínima:** Separar revisión técnica de contenido operativo mediante una revisión/fingerprint semántico, conservando el token de concurrencia y el reconocimiento de cambios reales.

**Aceptación:** Asignar producción sin cambiar demanda no avisa cambio comercial; corregir cantidad, unidad o destino sí lo avisa y conserva control concurrente.

**Fase:** 4.

### F13 | P2 | Las tres pruebas portátiles de SQL no prueban funcionalmente Horarios

**Ámbito:** Validación I23. **Estado:** Pendiente. **Evidencia:** Brecha de cobertura confirmada.

**Referencias:** `tests/Prodimt.Pedidos.DeploymentValidation/ValidationRunner.cs:62`, `tests/Prodimt.Pedidos.Application.Tests/OrderingScheduleTests.cs`, `.github/workflows/ci.yml:129`.

El runner selecciona DeliveryManagementSqlTests. Es una regresión valiosa de reparto; OrderingScheduleTests usa SQLite y no se identifican pruebas funcionales propias de horarios en SqlServer.Tests. El gate SQL exacto comprueba otro tipo de riesgo.

**Acción mínima:** Añadir pruebas HTTP/SQL de horarios y solicitudes, conservando las de reparto: enlace vencido/revocado, corte/fecha/aceptación y replay/concurrencia. Extender el gate nativo CI aI22/I23.

**Aceptación:** Una base UUID SQL Server valida nuevos flujos, permisos, rollback/replay y limpieza en CI antes del despliegue.

**Fase:** 2.

### F14 | P2 | La comprobación completa UI-API-SQL y los comandos de test están incompletos

**Ámbito:** Pruebas de interfaz y contratos. **Estado:** Pendiente. **Evidencia:** Ejecutado: typecheck global falla; navegador bloqueado.

**Referencias:** `tests/e2e/playwright.config.js:17`, `apps/prodimt-pedidos-web/package.json:8`, `apps/prodimt-pedidos-web/angular.json`, `tests/e2e/specs/daily-orders.spec.ts:95`.

Playwright usa una API simulada. ng test figura en scripts sin target test. El typecheck estricto de todos los specs falla por Buffer sin tipos Node; el subconjunto Reparto/Horarios sí pasa. El navegador local aborta al iniciar por socket EPERM.

**Acción mínima:** Mantener UI mock rápida, añadir un recorrido pequeño con API y SQL reales, alinear comandos y dependencias de tipos. Incorporar lint/analyzers/formato incremental y cobertura de cambios críticos, sin ocultar deuda anterior.

**Aceptación:** Un comando documentado por nivel funciona en CI; typecheck global pasa; recorrido real cubre captura, revisión y parcial 50/25/20; SQL y navegador no se sustituyen por mocks.

**Fase:** 2.

### F15 | P2 | Dependencias transitivas tienen avisos moderados sin control de mantenimiento completo

**Ámbito:** Herramientas de desarrollo web. **Estado:** Pendiente. **Evidencia:** npm audit ejecutado; sin explotación demostrada.

**Referencias:** `apps/prodimt-pedidos-web/package-lock.json:4942`, `apps/prodimt-pedidos-web/package-lock.json:5695`, `.github/workflows/ci.yml:158`.

npm audit devuelve 16 entradas moderadas, dos avisos raíz glob/minimatch en la cadena de Angular CLI; --omit=dev devuelve 0. El arreglo automático propone salto mayor aAngular 22: no es un parche que deba forzarse. El gate actual sólo bloquea high/critical.

**Acción mínima:** Resolver con versiones compatibles verificadas o registrar una excepción temporal con vencimiento mientras se prepara la actualización. Añadir revisión de NuGet/transitivas y lock/pines reproducibles; no usar audit fix --force.

**Aceptación:** Escaneo fechado distingue runtime/tooling, cada aviso tiene tratamiento y no quedan high/critical sin decisión. Build, UI y contratos pasan antes/después.

**Fase:** 2.

### F16 | P2 | Dashboard de reparto multiplica consultas por pedido y asignación

**Ámbito:** Lecturas RF1 e I23. **Estado:** Pendiente. **Evidencia:** Patrón N+1 confirmado; latencia pendiente de medir.

**Referencias:** `src/Prodimt.Pedidos.Application/Delivery/DeliveryManagementService.cs:22`, `src/Prodimt.Pedidos.Application/Delivery/DeliveryManagementService.cs:404`, `src/Prodimt.Pedidos.Infrastructure/Repositories/EfDeliveryRepository.cs`.

El servicio lee por candidata y vuelve a consultar pedido/reservas por asignación; own se filtra tras obtener vueltas del día. Son consultas SQL repetidas demostrables; no se midió lentitud actual en la ruta piloto. La UI también recalcula agrupaciones en getters.

**Acción mínima:** Medir 25/100/500 pedidos, construir proyecciones por lotes y filtros en repositorio, luego memorizar viewmodels/índices de UI por snapshot. No debilitar transacciones del ledger ni paralelizar un DbContext compartido.

**Aceptación:** Consultas de lectura no crecen linealmente por partida; métricas p50/p95 y payload antes/después, cero acceso ajeno y resultados equivalentes. Virtualizar sólo si la medición lo exige.

**Fase:** 4.

### F17 | P2 | Contraste y semántica de tabs requieren ajuste accesible

**Ámbito:** RF1 e I23. **Estado:** Pendiente. **Evidencia:** Contraste calculado desde CSS; inspección visual pendiente.

**Referencias:** `apps/prodimt-pedidos-web/src/app/admin-security.component.css:1`, `apps/prodimt-pedidos-web/src/app/admin-security.component.html:12`.

El texto .75 rem #658087 da 4.21:1 sobre blanco y 3.89:1 sobre seleccionado #eff8f5, por debajo del mínimo 4.5:1 de texto normal. role=tab sólo implementa click, sin navegación de flechas propia de ese patrón.

**Acción mínima:** Token de texto secundario con contraste suficiente y semántica/teclado coherentes: implementar tabs o usar botones de filtro. Conservar foco y etiquetas junto al color.

**Aceptación:** Contraste>=4.5 sin redondear, teclado completo y zoom 200%; revisión de 320/390/768/1440/1920 px sin pérdida de captura. No se afirma certificación WCAG completa.

**Fase:** 3.

### F18 | P2 | La revisión no presenta cantidades en una consulta de sólo lectura

**Ámbito:** RF1 e I23. **Estado:** Pendiente. **Evidencia:** Código confirmado; impacto condicionado.

**Referencias:** `apps/prodimt-pedidos-web/src/app/admin-pending.component.ts:53`, `apps/prodimt-pedidos-web/src/app/admin-pending.component.ts:231`.

Antes de Aceptar/Rechazar, la tarjeta muestra metadatos pero no las partidas; para verlas debe entrar a Cambios o ir a Pedidos y regresar. Es una fricción verificable del diseño, no una prueba de que el usuario aceptara algo incorrecto.

**Acción mínima:** Detalle readonly con producto/cantidad/unidad/ruta/horario reutilizando GET existente. Separar consultar de editar y conservar fecha/selección al decidir.

**Aceptación:** Puede revisar todas las partidas sin activar edición, decidir y avanzar conservando fecha/posición en móvil y PC.

**Fase:** 3.

### F19 | P1 | Una instrucción vigente aún afirmaba que no existía BD operativa

**Ámbito:** Documentación. **Estado:** Resuelto documentalmente. **Evidencia:** Contradicción documental confirmada.

**Referencias:** `docs/08-contexto-para-codex.md:5`, `docs/proyecto/DECISIONES.md:13`.

La premisa histórica deOCT-002 seguía presentada como vigente pese al inventarioOCT-032 y al sistema instalado. Es un riesgo de ejecución de mantenimiento, no un defecto de persistencia.

**Acción mínima:** Marcar la premisa sustituida, preservar historia y exigir lectura del estado actual con infraestructura/datos existentes.

**Aceptación:** Las entradas vigentes describen instalación existente y enlazan una única fuente de estado. La historia queda identificada como tal.

**Fase:** 0.

### F20 | P2 | Estados históricos se mezclaban con entregas actuales y evidencia de pruebas

**Ámbito:** Documentación. **Estado:** Resuelto documentalmente. **Evidencia:** Contradicción documental confirmada.

**Referencias:** `README.md:15`, `README.md:19`, `CONTINUAR.md`, `docs/proyecto/ESTADO.md`.

I18/I19 aparecían como pendientes mientras I22 ya los integraba. Continuidad/estado/bitácora repetían 1687 líneas de intentos y la baseI23 no tenía el registro de RF1 instalado. Implementado, probado, instalado y aceptado no estaban separados uniformemente.

**Acción mínima:** README y handoff breves, estado y matriz vigentes únicos, archivos históricos conservados, arquitectura real y evidencia de pruebas por entorno. Registrar I23 preparado/bloqueado sin declararlo instalado.

**Aceptación:** Lectura inicial no exige reconstruir intentos; todo estado tiene base y evidencia; no se equipara mock/SQLite/build con validación SQL/navegador real.

**Fase:** 0.

### F21 | P3 | Responsabilidades distintas se concentran en clases y componentes grandes

**Ámbito:** Arquitectura y mantenibilidad. **Estado:** Pendiente. **Evidencia:** Código confirmado; impacto condicionado.

**Referencias:** `src/Prodimt.Pedidos.Application/AdminImports/AdminImportService.cs:1`, `src/Prodimt.Pedidos.Api/Program.cs:1`, `apps/prodimt-pedidos-web/src/app/admin-catalogs.component.ts:48`.

Importaciones 2265 líneas coordina parsing/validación/aplicación; Program 1164 mezcla composición y muchos endpoints; catálogo 953 coordina clientes/perfiles/precios/máquinas/tokens. El problema es la mezcla concreta de razones de cambio, no un límite arbitrario de líneas.

**Acción mínima:** Extraer por caso de uso, empezar por endpoints de un módulo y parsing puro, luego configuración/catálogo. Interfaces sólo en límites que se necesiten y editores como uniones discriminadas donde ayuden.

**Aceptación:** Contratos HTTP/DTO y permisos no cambian; cada extracción pasa regresión; precios y calendario no obligan a tocar máquinas. No se impone MediatR, NgRx ni microservicios.

**Fase:** 5.

### F22 | P3 | Estados y traducción de errores se repiten en varios bordes

**Ámbito:** Arquitectura y contratos internos. **Estado:** Pendiente. **Evidencia:** Código confirmado; impacto condicionado.

**Referencias:** `src/Prodimt.Pedidos.Api/Program.cs`, `src/Prodimt.Pedidos.Api/DeliveryEndpoints.cs`, `apps/prodimt-pedidos-web/src/app/operational-labels.ts`.

Hay estados/políticas con strings y traducciones de excepción repetidas; usar InvalidOperationException como no encontrado hace frágil separar 404/409. Son puntos de extensión/error inconsistentes, sin demostrar un incidente.

**Acción mínima:** Catálogos internos tipados de estados y resultados/errores de aplicación acotados, con adaptadores HTTP consistentes por módulo. Preservar strings públicos y fixtures.

**Aceptación:** Matriz 400/401/403/404/409 consistente y contrato externo sin cambios; estados desconocidos siguen neutrales.

**Fase:** 5.

### F23 | P3 | Shell y estilos específicos producen anchos y jerarquías incoherentes

**Ámbito:** Experiencia visual transversal. **Estado:** Pendiente. **Evidencia:** Código confirmado; impacto condicionado.

**Referencias:** `apps/prodimt-pedidos-web/src/styles.css:82`, `apps/prodimt-pedidos-web/src/styles.css:549`, `apps/prodimt-pedidos-web/src/app/admin-ordering-schedules.component.ts:36`.

main limita 860 px y varias páginas se amplían con :has de clases internas; Revisión y Horarios no comparten esa ampliación. Seis CSS exceden el aviso de 4 kB y hay reglas de navegación repetidas. No equivale a un defecto visual observado en un navegador.

**Acción mínima:** Tokens CSS y shell con variantes explícitas de ancho; encabezado, filtros, métricas, vacío/error/carga y acciones consistentes. Mantener paleta sobria, densidad útil y tarjetas existentes.

**Aceptación:** Mismos recorridos tienen misma jerarquía y comportamiento responsive; sin overflow, teclado/zoom y foco/borrador preservados. Reducir duplicación sin aumentar presupuestos para ocultarla.

**Fase:** 3.

### F24 | P3 | Auditoría inmutable y cabeceras no están protegidas de forma homogénea

**Ámbito:** Defensa por capas. **Estado:** Pendiente. **Evidencia:** Controles no homogéneos confirmados; impacto de hardening.

**Referencias:** `src/Prodimt.Pedidos.Infrastructure/Persistence/PedidosDbContext.cs:63`, `src/Prodimt.Pedidos.Infrastructure/Persistence/Configurations/OrderAuditLogConfiguration.cs:48`, `scripts/pilot/validate-ordering-schedules-sql.py:125`.

DeliveryEvent tiene protección EF/SQL contra cambios; auditorías generales/recibos no comparten toda esa política y existe una cascada en historial de pedido. En la respuesta pública observada no aparecieron CSP/HSTS/nosniff/frame/referrer-policy. No se demostró endpoint que borre auditoría ni vulnerabilidad explotada.

**Acción mínima:** Definir retención y permisos mínimos antes de cambiar cascadas; extender protección deliberada y pruebas de actor/recibo. Preparar cabeceras primero en staging/report-only compatible con Angular. Añadir correlación de errores y medidas operativas sin datos sensibles.

**Aceptación:** Roles de aplicación no alteran evidencia histórica fuera de mantenimiento explícito; políticas de cabeceras no rompen Maps/portal; un error puede correlacionarse sin exponer tokens o instrucciones privadas.

**Fase:** 5.

## Controles que deben conservarse

- Pedido original separado del ledger físico; cantidades por partida y unidad, sin mezclar kilos/paquetes.
- Caso 50 pedido /25 cargado /20 entregado:30 pendientes al cliente,25 libres para otra asignación y 5 bajo custodia hasta resolución explícita. No se suman 30+25 como dos pendientes diferentes.
- Transacciones, recibos durables, comprobación de versión y reservas protegen producción/reparto; corregir no elimina compromisos físicos.
- Permisos resueltos en servidor por petición, protección del último administrador y tokens cifrados con AES-GCM/clave externa.
- Asignación y consulta no confirman físicamente producción, conteo, carga ni entrega; abrir WhatsApp no acredita envío.
- Pines de fuente/payload/binarios, respaldos verificados, activación atómica y recuperación; bases UUID y limpieza de pruebas.
- Angular strict/strictTemplates, DTOs por área, track por ID, componentes standalone y reglas de edición/refresco ya existentes.

## Fuentes externas primarias consultadas

- Microsoft, dependencias y separación de capas: https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures
- W3C, contraste mínimo de texto normal 4.5:1: https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html
- Mantenedor de glob, aviso GHSA-3g3m-2fjr-6pw4: https://github.com/isaacs/node-glob/security/advisories/GHSA-3g3m-2fjr-6pw4
- Mantenedor de minimatch, aviso GHSA-g3hv-75wg-m249: https://github.com/isaacs/minimatch/security/advisories/GHSA-g3hv-75wg-m249

Las recomendaciones son inferencias de esta revisión sobre el código; no una certificación formal SOLID, WCAG, pentest o cobertura total.
