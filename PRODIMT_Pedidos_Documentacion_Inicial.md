# PRODIMT Pedidos — Documentación inicial consolidada

<!-- docs/00-contexto-y-objetivo.md -->

# 00 — Contexto y objetivo

## Empresa

PRODIMT.

## Problema actual

El proceso actual depende de WhatsApp, llamadas telefónicas, mensajes en grupo y captura manual en Excel.

Flujo actual observado por negocio:

1. El cliente envía su pedido por WhatsApp.
2. Si no lo manda temprano, antes de las 10:00 a.m., alguien le llama para confirmar.
3. La persona que confirma escribe el pedido en un grupo de WhatsApp.
4. Otra persona lee los mensajes del grupo y los pasa al Excel.
5. Al manejar aproximadamente 300 clientes, existe riesgo alto de omitir mensajes, duplicar pedidos o capturar cantidades en el molde incorrecto.

## Objetivo del sistema

Crear una aplicación mobile first para que los clientes capturen, confirmen o repitan su pedido de forma sencilla.

El resultado principal esperado es que PRODIMT tenga los pedidos capturados en una base de datos central, listos para ser consultados por distintas áreas.

## Principio rector

La aplicación debe reducir fricción para el cliente. El cliente no debe llenar una tabla grande parecida al Excel. Debe ver solo los productos y moldes que normalmente pide, con la opción de agregar otros cuando sea necesario.

## Meta de la primera versión útil

Que el pedido llegue al sistema sin llamada y sin recaptura manual.

Todo lo demás —estadísticas, proyecciones, reportes avanzados, vistas por departamento y WhatsApp automático— debe construirse alrededor de ese objetivo, no antes de resolverlo.


---

<!-- docs/01-analisis-excel-actual.md -->

# 01 — Análisis del Excel actual

Archivo revisado: `05 EMBARQUES Mayo-04.xlsm`.

## Hojas principales

El archivo contiene hojas por día:

- LUNES
- MARTES
- MIÉRCOLES
- JUEVES
- VIERNES
- SABADO
- DOMINGO

También contiene vistas o auxiliares:

- `Maq. #2 Lunes`
- `Hoja1`
- `FORMATO`
- `GABY (3)`
- `Metricas Produccion`

## Estructura observada en las hojas por día

En las hojas por día, la captura principal está en las columnas:

| Columna | Uso observado |
|---|---|
| A | Molde o categoría de producto |
| B | Maq / máquina / equipo |
| C | Cliente |
| D | Pedido |
| E | Campo operativo asociado a línea, máquina o clasificación; requiere confirmación |
| F | Repartidor |

La columna D contiene la cantidad del pedido. Cuando aparece `x` o `X`, esa marca no suma al total numérico. Debe confirmarse si significa "no pidió", "contactado sin pedido", "pendiente" u otra cosa.

## Hallazgos cuantitativos

Estos conteos son aproximados y deben tratarse como candidatos, porque hay diferencias de escritura en nombres de clientes y moldes.

| Dato | Observación |
|---|---:|
| Líneas candidatas de cliente en LUNES | 306 |
| Líneas candidatas por hoja diaria | 296 a 312 |
| Clientes únicos estimados, sin `Mostrador` | 236 |
| Clientes que aparecen en los 7 días de plantilla | 213 |
| Moldes/categorías observados en hojas diarias | 23 |
| Clientes con un solo molde/categoría candidato | 172 |
| Clientes con dos moldes/categorías candidatos | 45 |
| Clientes con tres moldes/categorías candidatos | 19 |
| LUNES con cantidad numérica capturada | 159 líneas |
| LUNES con marca `x/X` | 130 líneas |
| Suma numérica observada en LUNES | 2400 |

## Interpretación funcional

El Excel funciona como una plantilla operativa por día. El usuario humano localiza el cliente y escribe el pedido en la fila del molde correspondiente.

Para la app, esta estructura debe transformarse a un modelo normalizado:

- Cliente
- Producto / molde
- Preferencias de cliente por producto/moldes
- Pedido
- Detalle de pedido
- Estado de captura / confirmación / entrega

## Vistas derivadas

`Hoja1` y `Maq. #2 Lunes` no parecen ser hojas de captura primaria. Son vistas derivadas con fórmulas `XLOOKUP` que consultan datos de hojas por día.

Ejemplos observados:

- `Hoja1` consulta principalmente la hoja `MIÉRCOLES`.
- `Maq. #2 Lunes` consulta principalmente la hoja `LUNES`.

Esto confirma que no conviene dar acceso al Excel completo a terceros. La app debe publicar vistas filtradas desde base de datos.

## Hoja `GABY (3)`

La hoja `GABY (3)` parece una matriz o catálogo operativo de clientes contra moldes/categorías por ruta. Puede servir para enriquecer el catálogo inicial, pero requiere limpieza antes de usarla como fuente automática.

## Riesgos del Excel actual

1. No está normalizado.
2. Depende de filas fijas y ubicación visual.
3. Tiene nombres escritos con variaciones.
4. Mezcla captura, cálculo, logística y reportes en el mismo archivo.
5. Requiere criterio humano para interpretar marcas como `x/X`.
6. Las hojas derivadas dependen de fórmulas y de nombres de hoja específicos.

## Recomendación

Usar el Excel como fuente de descubrimiento y semilla inicial, no como base operativa permanente.

El sistema nuevo debe guardar pedidos en SQL Server y, si es necesario, exportar vistas con formato similar al Excel para transición operativa.


---

<!-- docs/02-alcance-y-etapas.md -->

# 02 — Alcance y etapas

## Alcance general

Construir una aplicación web mobile first para capturar pedidos de clientes, reducir llamadas y eliminar la recaptura manual desde WhatsApp hacia Excel.

## Alcance de la primera etapa

La primera etapa debe enfocarse en:

1. Catálogo mínimo de clientes.
2. Catálogo mínimo de productos/moldes.
3. Preferencias de pedido por cliente.
4. Captura de pedido del día desde celular.
5. Sugerencia basada en pedidos anteriores.
6. Panel administrativo para revisar pedidos capturados.
7. Exportación o vista simple para operación interna.

## Fuera de alcance de la primera etapa

No se debe intentar resolver desde el inicio:

- Optimización avanzada de producción.
- Ruteo automático.
- Predicción avanzada de demanda.
- Integración completa con facturación.
- Sustitución total del Excel histórico.
- Automatización completa de WhatsApp con plantillas interactivas.
- Dashboard ejecutivo complejo.

## Etapas propuestas

### Etapa 0 — Preparación

Objetivo: dejar lista la base de trabajo.

Entregables:

- Documentación inicial.
- Repositorio con estructura base.
- Decisiones técnicas.
- Modelo de datos inicial.
- Importación o carga manual inicial de clientes y productos.

### Etapa 1 — MVP de captura

Objetivo: que el cliente pueda enviar su pedido y que PRODIMT pueda verlo.

Entregables:

- Login simple para cliente.
- Pantalla "Mi pedido de hoy".
- Productos frecuentes del cliente.
- Cantidad por producto/molde.
- Repetir pedido sugerido.
- Guardar pedido.
- Vista administrativa de pedidos por fecha.
- Estado de clientes pendientes.

### Etapa 2 — Operación interna

Objetivo: que áreas internas usen la información sin esperar recaptura.

Entregables:

- Vista de producción por molde/producto.
- Vista de reparto por cliente/ruta/repartidor.
- Vista de pedidos pendientes o tardíos.
- Exportación a Excel si aún se requiere.
- Roles internos.

### Etapa 3 — WhatsApp automatizado

Objetivo: usar WhatsApp para reducir fricción, no como base de datos.

Entregables:

- Mensaje diario con último pedido sugerido.
- Botón o liga para confirmar repetición.
- Liga para editar en app.
- Registro de confirmaciones.
- Webhook de respuestas cuando aplique.

### Etapa 4 — Valor agregado para clientes

Objetivo: aumentar adopción.

Entregables:

- Histórico visual de pedidos.
- Comparativos por semana.
- Tendencia de crecimiento.
- Sugerencias por fechas especiales.
- Avisos de pedido recurrente.
- Recomendaciones personalizadas.

## Criterio de éxito del MVP

El MVP se considera exitoso cuando:

- Un cliente puede confirmar o capturar su pedido desde celular sin ayuda.
- Administración puede ver quién pidió y quién falta.
- La información puede consultarse sin leer mensajes de WhatsApp.
- El proceso puede convivir con llamadas manuales solo para clientes rezagados.


---

<!-- docs/03-requerimientos-funcionales.md -->

# 03 — Requerimientos funcionales

## Prioridad MVP

### FR-001 — Autenticación de cliente

El cliente debe poder entrar a la aplicación desde celular.

Criterio inicial aceptable:

- Acceso por número telefónico y código.
- Alternativa temporal: liga segura por cliente para piloto controlado.

### FR-002 — Perfil de cliente

El sistema debe guardar datos básicos del cliente:

- Nombre comercial.
- Teléfono principal.
- Contactos adicionales opcionales.
- Ruta o zona opcional.
- Estado activo/inactivo.

### FR-003 — Catálogo de productos/moldes

El sistema debe tener un catálogo de productos/moldes basado inicialmente en el Excel.

Ejemplos:

- #9.5
- #10
- #10.5
- #11
- #11.5
- #12
- #13
- #14
- #15
- #16
- Flauta
- Vapor
- Sancochado
- Grueso
- Especialidades

Los nombres deben normalizarse. El sistema no debe depender de textos duplicados como `# 10 1/ 2`, `#10.5` o `#10½`.

### FR-004 — Preferencias de cliente por producto

El sistema debe guardar qué productos/moldes suele pedir cada cliente.

Esto evita mostrar al cliente una lista completa de todos los moldes.

### FR-005 — Pedido del día

El cliente debe poder crear o confirmar su pedido para una fecha específica.

El pedido debe tener:

- Cliente.
- Fecha de entrega o producción.
- Estado.
- Líneas de pedido.
- Cantidad por producto/molde.
- Observaciones opcionales.

### FR-006 — Sugerencia de pedido

Al entrar, el cliente debe ver una sugerencia basada en su comportamiento histórico.

Primera regla del MVP:

- Mostrar el último pedido del mismo día de la semana.
- Mostrar también los últimos 3 pedidos del mismo día de la semana cuando existan.

Ejemplo: si hoy es lunes, mostrar los últimos 3 lunes del cliente.

### FR-007 — Repetir pedido

El cliente debe poder repetir el pedido sugerido con una acción simple.

Después puede editar cantidades antes de enviar.

### FR-008 — Editar cantidades

El cliente debe poder modificar cantidades por producto/molde frecuente.

Debe poder:

- Subir cantidad.
- Bajar cantidad.
- Dejar cantidad en cero.
- Agregar nota.
- Agregar producto no frecuente desde una opción secundaria.

### FR-009 — Enviar pedido

El cliente debe confirmar el pedido.

Al confirmar, el sistema debe guardar:

- Fecha y hora de confirmación.
- Usuario o canal de captura.
- Detalle del pedido.
- Cambios contra la sugerencia, si aplica.

### FR-010 — Estado de clientes pendientes

El área administrativa debe ver qué clientes no han enviado pedido antes de la hora límite.

Esto reemplaza la lista mental/manual de llamadas.

### FR-011 — Captura interna en nombre del cliente

Un usuario interno debe poder capturar o editar el pedido por teléfono cuando el cliente no use la app.

El pedido debe quedar marcado con canal `CapturadoInternamente`.

### FR-012 — Vista administrativa diaria

El sistema debe mostrar pedidos por fecha con filtros básicos:

- Cliente.
- Producto/molde.
- Estado.
- Canal de captura.
- Pendiente/confirmado.
- Hora de captura.

### FR-013 — Exportación inicial

El sistema debe poder generar una salida operativa simple, idealmente en Excel o CSV, para transición.

La exportación debe permitir ordenar o agrupar por:

- Producto/molde.
- Cliente.
- Ruta/repartidor.
- Estado.

### FR-014 — Roles

Roles mínimos:

- Cliente.
- Administración.
- Producción.
- Reparto.
- Consulta gerencial.

En MVP pueden implementarse primero Cliente y Administración, dejando Producción/Reparto como vistas protegidas posteriores.

### FR-015 — Auditoría

El sistema debe registrar cambios importantes:

- Creación de pedido.
- Edición de pedido.
- Confirmación.
- Cancelación.
- Captura interna.
- Cambio posterior a hora límite.

### FR-016 — Hora límite

El sistema debe manejar una hora límite configurable, inicialmente 10:00 a.m.

Después de esa hora, el pedido puede:

- Bloquearse.
- Aceptarse como tardío.
- Requerir autorización interna.

La regla exacta debe definirse con operación.


---

<!-- docs/04-requerimientos-plus.md -->

# 04 — Requerimientos plus

Estas funcionalidades aumentan adopción, eficiencia o valor percibido, pero no deben bloquear el MVP.

## PLUS-001 — WhatsApp de confirmación rápida

El sistema puede enviar un mensaje con el último pedido sugerido:

> Hola, este fue tu último pedido para este día: ...
> ¿Quieres repetirlo?

Opciones:

- Confirmar repetición.
- Abrir app para editar.
- Avisar que no hará pedido.

## PLUS-002 — WhatsApp con webhooks

Cuando el cliente responda por WhatsApp, el sistema puede recibir la respuesta y actualizar el estado del pedido.

Esto requiere integración con WhatsApp Business Platform, plantillas y webhooks.

## PLUS-003 — Estadísticas para el cliente

Mostrar al cliente información útil:

- Histórico de pedidos.
- Promedio semanal.
- Comparativo contra semanas anteriores.
- Productos más pedidos.
- Tendencia de crecimiento.

## PLUS-004 — Fechas especiales

El sistema puede mostrar avisos como:

- Día festivo.
- 10 de mayo.
- Puentes.
- Eventos locales.
- Temporadas con mayor demanda.

La intención es ayudar al cliente a anticipar ventas.

## PLUS-005 — Proyección simple

El sistema puede sugerir cantidades con base en:

- Últimos 3 pedidos del mismo día.
- Variación contra semana anterior.
- Eventos especiales.
- Estacionalidad simple.

## PLUS-006 — Vista de producción por área

Crear vistas internas especializadas:

- Producción por molde.
- Producción por máquina.
- Producción por línea.
- Faltantes o cambios tardíos.

## PLUS-007 — Vista de reparto

Crear vista para logística:

- Pedidos por ruta.
- Pedidos por repartidor.
- Clientes pendientes.
- Cambios de último momento.

## PLUS-008 — Notificaciones internas

Alertas para administración:

- Cliente importante no pidió.
- Pedido atípico.
- Pedido tardío.
- Cantidad mucho mayor o menor que lo normal.

## PLUS-009 — Modo PWA

Permitir instalar la aplicación en el celular como aplicación web progresiva.

Esto puede reducir fricción sin crear una app nativa.

## PLUS-010 — Importador avanzado desde Excel

Crear un importador que lea versiones futuras del Excel para migrar historia.

No debe ser dependencia permanente del flujo.


---

<!-- docs/05-arquitectura-propuesta.md -->

# 05 — Arquitectura propuesta

## Enfoque general

La base del sistema debe ser SQL Server, no Excel.

El Excel se usará como fuente inicial de conocimiento, plantilla histórica y posible formato de exportación temporal.

## Arquitectura lógica

```text
Cliente móvil / navegador
        ↓
Angular mobile first
        ↓
API .NET
        ↓
Application Layer
        ↓
Domain Layer
        ↓
Infrastructure
        ↓
SQL Server
```

## Backend

Se propone .NET 10 con Clean Architecture.

Capas sugeridas:

```text
src/
  Prodimt.Pedidos.Api
  Prodimt.Pedidos.Application
  Prodimt.Pedidos.Domain
  Prodimt.Pedidos.Infrastructure
tests/
  Prodimt.Pedidos.UnitTests
  Prodimt.Pedidos.IntegrationTests
```

Responsabilidades:

- `Domain`: entidades, reglas de negocio, invariantes.
- `Application`: casos de uso, DTOs, validaciones de aplicación.
- `Infrastructure`: SQL Server, repositorios, integraciones externas.
- `Api`: endpoints, autenticación, configuración, OpenAPI.

## Frontend

Se propone Angular con enfoque mobile first.

Principios:

- Pantallas simples.
- Formularios cortos.
- Botones grandes.
- Carga rápida.
- Diseñado para uso en celular.
- Primero pedido; después estadísticas.

Estructura sugerida:

```text
apps/
  prodimt-pedidos-web/
    src/app/features/customer-order
    src/app/features/admin-orders
    src/app/features/auth
    src/app/core
    src/app/shared
```

## Tailwind

Tailwind es opcional, pero recomendable si se quiere velocidad para un diseño mobile first altamente personalizado.

Alternativa: Angular Material/CDK para componentes accesibles y patrones estándar.

Decisión sugerida: Tailwind + componentes propios simples, usando CDK cuando aporte accesibilidad o comportamiento robusto.

## Pruebas

Niveles mínimos:

- Unit tests de reglas de dominio.
- Integration tests de API y persistencia.
- Playwright para flujo E2E principal:
  - Cliente entra.
  - Repite pedido sugerido.
  - Edita cantidad.
  - Envía pedido.
  - Administración ve el pedido.

## Seguridad

Principios:

- El cliente solo ve su información.
- Las áreas internas ven solo lo que necesitan por rol.
- No se expone el Excel maestro.
- Tokens y credenciales nunca van en frontend.
- Auditoría obligatoria para cambios de pedido.

## Integraciones futuras

- WhatsApp Business Platform para mensajes y webhooks.
- Exportación a Excel.
- Importación controlada de catálogos.
- Calendario de fechas especiales.


---

<!-- docs/06-modelo-datos-inicial.md -->

# 06 — Modelo de datos inicial

Este modelo es conceptual. Debe refinarse durante el diseño técnico.

## Entidades principales

### Customer

Representa al cliente.

Campos candidatos:

- CustomerId
- DisplayName
- LegalName opcional
- PrimaryPhone
- SecondaryPhone opcional
- RouteId opcional
- IsActive
- CreatedAt
- UpdatedAt

### Product

Representa producto, molde o categoría vendible.

Campos candidatos:

- ProductId
- Name
- CanonicalCode
- ProductType
- IsActive

Ejemplos:

- #9.5
- #10
- #10.5
- Flauta
- Vapor
- Sancochado
- Grueso

### CustomerProductPreference

Relación entre cliente y producto que normalmente pide.

Campos candidatos:

- CustomerId
- ProductId
- DefaultQuantity opcional
- PreferredWeekdays opcional
- DisplayOrder
- IsFrequent

### Order

Pedido del cliente para una fecha.

Campos candidatos:

- OrderId
- CustomerId
- OrderDate
- DeliveryDate opcional
- Status
- CaptureChannel
- SubmittedAt
- SubmittedByUserId opcional
- Notes
- IsLate
- CreatedAt
- UpdatedAt

Estados candidatos:

- Draft
- Suggested
- Submitted
- Confirmed
- Cancelled
- Processed

Canales candidatos:

- CustomerApp
- InternalCall
- WhatsAppConfirmation
- Import
- AdminEdit

### OrderLine

Detalle del pedido.

Campos candidatos:

- OrderLineId
- OrderId
- ProductId
- Quantity
- Unit opcional
- Notes
- SourceSuggestionLineId opcional
- WasChangedFromSuggestion

### OrderSuggestion

Sugerencia calculada para un cliente y fecha.

Campos candidatos:

- CustomerId
- SuggestedForDate
- BasedOnOrderIds
- CreatedAt

Puede calcularse bajo demanda al inicio, sin persistirse.

### AuditLog

Registro de cambios.

Campos candidatos:

- AuditLogId
- EntityName
- EntityId
- Action
- OldValue
- NewValue
- ActorUserId
- OccurredAt

### User

Usuario interno o cliente.

Campos candidatos:

- UserId
- CustomerId nullable
- Name
- Phone
- Email nullable
- Role
- IsActive

### Route

Ruta opcional para reparto.

Campos candidatos:

- RouteId
- Name
- DefaultDriverUserId opcional
- IsActive

## Reglas de negocio iniciales

1. Un cliente puede tener máximo un pedido activo por fecha.
2. Un pedido puede tener muchas líneas.
3. Una línea de pedido debe tener producto y cantidad.
4. La cantidad debe ser mayor o igual a cero.
5. Cero debe significar cantidad cero; no debe mezclarse con la marca `x/X` del Excel sin definirla.
6. Todo cambio después de la hora límite debe marcarse como tardío o auditado.
7. Las sugerencias nunca deben enviarse como pedido confirmado sin acción del cliente o usuario interno.

## Limpieza requerida

Antes de importar masivamente:

- Normalizar nombres de clientes.
- Unificar moldes equivalentes.
- Confirmar significado de `x/X`.
- Confirmar uso de columna E del Excel.
- Definir si `Mostrador` es cliente, canal o categoría interna.


---

<!-- docs/07-backlog-mvp.md -->

# 07 — Backlog MVP

## Épica 1 — Base del repositorio

- Crear solución .NET con Clean Architecture.
- Crear proyecto Angular mobile first.
- Configurar SQL Server local/dev.
- Configurar variables de entorno.
- Crear guía de ejecución local.
- Crear pruebas base.

## Épica 2 — Catálogos

- Crear entidad Customer.
- Crear entidad Product.
- Crear relación CustomerProductPreference.
- Crear endpoints CRUD internos para catálogos.
- Cargar catálogo inicial manual o mediante seed.

## Épica 3 — Pedido del cliente

- Crear pantalla "Mi pedido de hoy".
- Mostrar productos frecuentes.
- Mostrar sugerencia.
- Permitir editar cantidades.
- Permitir agregar producto no frecuente.
- Confirmar pedido.
- Mostrar estado de pedido enviado.

## Épica 4 — Administración

- Ver pedidos por fecha.
- Filtrar por cliente.
- Ver clientes pendientes.
- Capturar pedido en nombre de cliente.
- Editar pedido antes de cierre.
- Marcar pedido tardío.

## Épica 5 — Sugerencias

- Obtener último pedido del mismo día de la semana.
- Obtener últimos 3 pedidos del mismo día.
- Calcular sugerencia simple.
- Mostrar diferencia contra sugerencia.

## Épica 6 — Exportación operativa

- Exportar pedidos del día a CSV o Excel.
- Agrupar por producto/molde.
- Agrupar por cliente.
- Preparar transición con operación.

## Épica 7 — Seguridad y auditoría

- Roles mínimos.
- Autorización por cliente.
- Auditoría de cambios.
- Proteger endpoints internos.

## Definición de terminado para MVP

- Cliente piloto puede enviar pedido desde celular.
- Administración puede ver pedido sin WhatsApp.
- Administración puede ver pendientes antes de llamadas.
- Sistema evita duplicar pedido del mismo cliente y fecha.
- Pedido queda guardado en SQL Server.
- Hay pruebas E2E del flujo principal.


---

<!-- docs/08-contexto-para-codex.md -->

# 08 — Contexto para Codex

## Proyecto

PRODIMT Pedidos.

## Propósito

Construir una aplicación web mobile first para capturar pedidos de clientes y reemplazar gradualmente el proceso manual basado en WhatsApp, llamadas y captura en Excel.

## Estado actual

- Solo existe documentación inicial.
- No se ha generado código todavía.
- El Excel `05 EMBARQUES Mayo-04.xlsm` fue analizado como referencia operativa.
- La prioridad es crear primero una versión útil para captura de pedidos.

## Stack deseado

- Backend: .NET 10.
- Arquitectura backend: Clean Architecture.
- Base de datos: SQL Server.
- Frontend: Angular, objetivo Angular 21 salvo que al iniciar el repo exista una versión estable más conveniente.
- Estilo: mobile first.
- CSS: Tailwind opcional.
- Pruebas E2E: Playwright.
- API documentada con OpenAPI/Swagger.

## Principios de implementación

1. El sistema debe ser mobile first.
2. Excel no debe ser la base de datos principal.
3. WhatsApp debe ser canal de comunicación, no fuente de verdad.
4. SQL Server debe ser la fuente de verdad.
5. El cliente solo ve sus propios pedidos.
6. La app debe mostrar productos frecuentes primero.
7. La captura rápida es más importante que reportes avanzados.
8. Todo cambio de pedido debe ser auditable.
9. Las decisiones técnicas importantes deben documentarse como ADR.
10. Cada sesión de Codex debe actualizar este documento o un archivo de estado equivalente si cambia el alcance o avance.

## Flujo principal a implementar primero

1. Cliente entra desde celular.
2. Sistema identifica al cliente.
3. Sistema muestra sugerencia de pedido.
4. Cliente repite o edita cantidades.
5. Cliente confirma.
6. Administración ve el pedido.
7. Administración ve quién falta de pedir.

## No implementar primero

- Dashboard ejecutivo complejo.
- Inteligencia artificial avanzada.
- Optimización de rutas.
- Integración completa de WhatsApp.
- Reemplazo total del Excel.
- Facturación.
- App nativa iOS/Android.

## Siguiente tarea sugerida para Codex

Crear la estructura inicial del repositorio:

```text
/
  README.md
  docs/
  src/
    Prodimt.Pedidos.Api/
    Prodimt.Pedidos.Application/
    Prodimt.Pedidos.Domain/
    Prodimt.Pedidos.Infrastructure/
  tests/
    Prodimt.Pedidos.UnitTests/
    Prodimt.Pedidos.IntegrationTests/
  apps/
    prodimt-pedidos-web/
```

Luego implementar el modelo base:

- Customer
- Product
- CustomerProductPreference
- Order
- OrderLine
- AuditLog

## Criterio para futuras sesiones

Al iniciar una nueva sesión, leer siempre:

- `README.md`
- `docs/08-contexto-para-codex.md`
- `docs/02-alcance-y-etapas.md`
- `docs/03-requerimientos-funcionales.md`
- `docs/07-backlog-mvp.md`

Al terminar una sesión, dejar documentado:

- Qué se creó.
- Qué falta.
- Qué decisiones se tomaron.
- Qué comandos se usaron.
- Qué pruebas pasan.


---

<!-- docs/09-preguntas-abiertas.md -->

# 09 — Preguntas abiertas

Estas preguntas no bloquean la documentación inicial, pero sí deben resolverse para cerrar el MVP.

## Operación

1. ¿Qué significa exactamente `x/X` en la columna PEDIDO del Excel?
2. ¿La hora límite de 10:00 a.m. bloquea pedidos o solo los marca como tardíos?
3. ¿Un cliente puede hacer más de un pedido al día?
4. ¿Un pedido puede editarse después de confirmado?
5. ¿Quién puede editar pedidos internamente?
6. ¿Qué pasa si un cliente no quiere usar la app?

## Catálogo

7. ¿Cuál es la lista oficial de moldes/productos?
8. ¿Cómo deben normalizarse equivalencias como `# 10 1/ 2`, `#10.5` y `#10½`?
9. ¿`Mostrador` debe tratarse como cliente, canal de venta o categoría interna?
10. ¿La columna E de las hojas diarias representa línea, máquina, clasificación o confirmación?

## Clientes

11. ¿Cada cliente tiene un teléfono único?
12. ¿Hay clientes con varios encargados?
13. ¿Hay clientes que compran en más de una ruta?
14. ¿Todos los clientes deben poder ver estadísticas?

## WhatsApp

15. ¿PRODIMT ya tiene WhatsApp Business Platform o solo WhatsApp normal?
16. ¿Se cuenta con consentimiento de clientes para mensajes automáticos?
17. ¿El mensaje debe permitir confirmar sin abrir la app, o basta con liga a la app?

## Departamentos

18. ¿Cuál es la primera vista interna más urgente después de captura: producción, reparto o administración?
19. ¿Qué campos necesita cada departamento?
20. ¿Deben conservarse formatos parecidos al Excel para facilitar adopción?

## Piloto

21. ¿Con cuántos clientes se probará primero?
22. ¿Qué clientes son ideales para piloto?
23. ¿Cuánto tiempo convivirá el sistema con WhatsApp manual?


---

<!-- docs/adrs/ADR-0001-stack-tecnologico.md -->

# ADR-0001 — Stack tecnológico inicial

## Estado

Propuesto.

## Contexto

Se necesita una aplicación web mobile first para pedidos de clientes, con backend robusto, base de datos relacional y capacidad de crecer hacia integraciones con WhatsApp, reportes y vistas por departamento.

## Decisión

Usar:

- .NET 10 para backend.
- Clean Architecture para separación de responsabilidades.
- SQL Server como base de datos principal.
- Angular para frontend.
- Tailwind opcional para estilos.
- Playwright para pruebas E2E.

## Consecuencias positivas

- Stack sólido para aplicaciones empresariales.
- Buen soporte para APIs, autenticación, pruebas e integración con SQL Server.
- Angular funciona bien para aplicaciones front-end estructuradas.
- Playwright permite validar el flujo real de usuario desde navegador móvil.
- SQL Server permite consultas operativas y reportes internos.

## Riesgos

- Clean Architecture puede generar más archivos al inicio; se debe evitar sobreingeniería.
- WhatsApp oficial puede requerir configuración, aprobación de plantillas y costos.
- Angular cambia de versión cada 6 meses; se debe crear el repo con la última versión estable conveniente al momento de iniciar.

## Referencias

- .NET support: https://learn.microsoft.com/en-us/dotnet/core/releases-and-support
- Angular releases: https://angular.dev/reference/releases
- Angular version compatibility: https://angular.dev/reference/versions
- Playwright .NET: https://playwright.dev/dotnet/
- Tailwind with Angular: https://tailwindcss.com/docs/installation/framework-guides/angular


---

<!-- docs/adrs/ADR-0002-excel-no-sera-fuente-principal.md -->

# ADR-0002 — Excel no será fuente principal de verdad

## Estado

Propuesto.

## Contexto

El proceso actual depende de un archivo Excel con hojas diarias, fórmulas, vistas derivadas y captura manual.

El archivo es útil operativamente, pero mezcla:

- Catálogo.
- Captura.
- Cálculo.
- Producción.
- Reparto.
- Reportes.
- Vistas derivadas.

## Decisión

SQL Server será la fuente principal de verdad para pedidos.

Excel podrá usarse como:

- Fuente inicial para descubrir clientes y moldes.
- Semilla de datos.
- Exportación temporal para operación.
- Referencia histórica.

Excel no debe usarse como base en tiempo real de la aplicación.

## Consecuencias positivas

- Menos riesgo de errores por filas o fórmulas.
- Mejor control de permisos.
- Auditoría real.
- Consultas por departamento.
- Menos recaptura manual.
- Mejor base para WhatsApp y estadísticas.

## Consecuencias negativas

- Requiere migración o carga inicial.
- Requiere definir catálogo canónico.
- Al inicio puede convivir con Excel hasta reemplazar procesos.
