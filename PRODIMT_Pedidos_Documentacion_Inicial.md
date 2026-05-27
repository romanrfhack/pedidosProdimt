# PRODIMT Pedidos — Documentación inicial consolidada

Versión documental: 0.2

Fecha: 2026-05-27


---


# PRODIMT Pedidos — Base documental inicial

Fecha: 2026-05-27  
Versión documental: 0.2

Este paquete contiene la documentación inicial para construir la aplicación de pedidos de PRODIMT con enfoque **mobile first**.

## Objetivo del proyecto

Sustituir gradualmente el flujo manual actual de pedidos por WhatsApp, llamadas, mensajes en grupo y captura en Excel por un sistema donde cada cliente pueda confirmar, editar o marcar que no pedirá desde el celular, y donde PRODIMT pueda consultar los pedidos sin depender de transcribir cientos de mensajes.

## Definiciones operativas confirmadas

- `x/X` en el Excel significa **no pidió**.
- La hora límite inicial es **10:00 a.m.**.
- Un pedido después de la hora límite se marca como **tardío** y queda sujeto a decisión administrativa.
- Si un cliente intenta hacer más de un pedido en el mismo día, el nuevo pedido o cambio debe quedar sujeto a decisión administrativa.
- `Mostrador` no es cliente externo; es un **canal de venta interno**.
- La columna E de las hojas diarias representa el **número de máquina** que atenderá el pedido.
- El cliente no debe ver la máquina asignada.
- El catálogo de cliente debe permitir registrar una **hora o ventana deseada de entrega** como dato opcional.
- La Fase 1 se concentra en capturar pedidos de clientes; las vistas de producción, embarques y repartidores quedan para fases posteriores.

## Documentos incluidos

- `docs/00-contexto-y-objetivo.md`: contexto operativo y objetivo del sistema.
- `docs/01-analisis-excel-actual.md`: hallazgos principales del archivo Excel actual.
- `docs/02-alcance-y-etapas.md`: alcance inicial, fuera de alcance y fases.
- `docs/03-requerimientos-funcionales.md`: requerimientos funcionales base.
- `docs/04-requerimientos-plus.md`: funcionalidades de valor agregado.
- `docs/05-arquitectura-propuesta.md`: arquitectura general propuesta.
- `docs/06-modelo-datos-inicial.md`: modelo conceptual inicial.
- `docs/07-backlog-mvp.md`: backlog inicial para la primera versión útil.
- `docs/08-contexto-para-codex.md`: instrucciones de continuidad para Codex.
- `docs/09-preguntas-abiertas.md`: dudas abiertas y decisiones ya resueltas.
- `docs/10-decisiones-operativas-confirmadas.md`: decisiones de negocio confirmadas por PRODIMT.
- `docs/11-reglas-de-negocio-fase-1.md`: reglas de negocio base para captura.
- `docs/12-flujos-fase-1.md`: flujos funcionales de la primera fase.
- `docs/adrs/`: decisiones arquitectónicas.
- `docs/reference/`: archivos de apoyo extraídos o derivados del Excel.

## Estado actual

- No hay código de aplicación generado todavía.
- Esta documentación define el alcance base para iniciar el repositorio.
- El Excel actual se usó como referencia para entender captura, moldes, clientes, máquinas y vistas derivadas.
- La primera meta de desarrollo debe ser capturar pedidos correctamente, no reemplazar todos los reportes de producción desde el día uno.

## Regla de continuidad para Codex

Antes de crear o modificar código, Codex debe leer:

1. `docs/08-contexto-para-codex.md`
2. `docs/10-decisiones-operativas-confirmadas.md`
3. `docs/11-reglas-de-negocio-fase-1.md`
4. `docs/12-flujos-fase-1.md`
5. `docs/02-alcance-y-etapas.md`
6. `docs/03-requerimientos-funcionales.md`
7. `docs/06-modelo-datos-inicial.md`
8. `docs/07-backlog-mvp.md`


---


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

Crear una aplicación mobile first para que los clientes capturen, confirmen, editen o indiquen que no harán pedido de forma sencilla.

El resultado principal esperado es que PRODIMT tenga los pedidos capturados en una base de datos central, listos para ser consultados por distintas áreas.

## Principio rector

La aplicación debe reducir fricción para el cliente. El cliente no debe llenar una tabla grande parecida al Excel. Debe ver primero los productos y moldes que normalmente pide, con la opción de agregar otros cuando sea necesario.

## Meta de la primera versión útil

Que el pedido llegue al sistema sin llamada y sin recaptura manual.

La Fase 1 debe resolver únicamente la captura y revisión administrativa básica del pedido. Las vistas de producción por máquina, embarques, repartidores, reportes avanzados, estadísticas para cliente y WhatsApp automático deben construirse después de estabilizar la captura.

## Condiciones operativas confirmadas

- El cliente debe poder enviar pedido desde celular.
- Si no pedirá, debe poder marcarlo explícitamente.
- En el Excel, `x/X` equivale a **no pidió**.
- Los pedidos después de las 10:00 a.m. no se aceptan automáticamente; quedan como tardíos y requieren decisión administrativa.
- Si el cliente hace un segundo pedido o cambio el mismo día, también requiere decisión administrativa.
- Algunos clientes tienen una hora o ventana deseada de entrega. Este dato debe guardarse en el catálogo de cliente como opcional.
- La asignación de máquina es información interna; el cliente no debe verla.


---


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

| Columna | Uso confirmado |
|---|---|
| A | Molde o categoría de producto |
| B | Maq / máquina / equipo, según formato de captura |
| C | Cliente |
| D | Pedido |
| E | Número de máquina que atenderá el pedido |
| F | Repartidor |

La columna D contiene la cantidad del pedido. Cuando aparece `x` o `X`, significa **no pidió**. En el sistema nuevo esto no debe tratarse como texto de pedido ni como cantidad cero sin contexto; debe representarse como un estado explícito de cliente sin pedido para esa fecha.

La columna E representa la máquina asignada para atender ese pedido. Esta asignación es interna y no debe mostrarse al cliente. Normalmente cada máquina tiene clientes asignados, pero un administrador puede cambiar la máquina en situaciones especiales.

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
- Estado de captura / no pedido / revisión / aceptación / rechazo
- Canal de captura o venta
- Máquina asignada de forma interna
- Hora o ventana deseada de entrega

## Mostrador

`Mostrador` no debe tratarse como cliente externo. Debe modelarse como un canal de venta interno o captura interna, para que no contamine las estadísticas de clientes ni las preferencias de moldes por cliente.

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
4. Mezcla captura, cálculo, logística, máquinas y reportes en el mismo archivo.
5. La marca `x/X` debe transformarse a un estado de negocio claro: no pidió.
6. Las hojas derivadas dependen de fórmulas y de nombres de hoja específicos.
7. La asignación de máquina no debe exponerse al cliente.

## Recomendación

Usar el Excel como fuente de descubrimiento y semilla inicial, no como base operativa permanente.

El sistema nuevo debe guardar pedidos en SQL Server y, si es necesario, exportar vistas con formato similar al Excel para transición operativa.


---


# 02 — Alcance y etapas

## Alcance general

Construir una aplicación web mobile first para capturar pedidos de clientes, reducir llamadas y eliminar la recaptura manual desde WhatsApp hacia Excel.

## Alcance de la primera etapa

La primera etapa debe enfocarse en capturar pedidos de clientes y permitir revisión administrativa básica.

Incluye:

1. Catálogo mínimo de clientes.
2. Catálogo mínimo de productos/moldes.
3. Preferencias de pedido por cliente.
4. Hora o ventana deseada de entrega por cliente, opcional.
5. Captura de pedido del día desde celular.
6. Opción explícita de **no pedir hoy**.
7. Sugerencia basada en pedidos anteriores.
8. Detección de pedido tardío después de las 10:00 a.m.
9. Detección de segundo pedido o cambio el mismo día.
10. Panel administrativo para revisar pedidos capturados.
11. Panel administrativo para aceptar, rechazar o ajustar pedidos tardíos o duplicados.
12. Exportación o vista simple para operación interna.
13. Registro de auditoría de decisiones administrativas.

## Fuera de alcance de la primera etapa

No se debe intentar resolver desde el inicio:

- Vista completa de producción por máquina.
- Vista completa de embarques.
- Vista completa de repartidores.
- Optimización avanzada de producción.
- Ruteo automático.
- Predicción avanzada de demanda.
- Integración completa con facturación.
- Sustitución total del Excel histórico.
- Automatización completa de WhatsApp con plantillas interactivas.
- Dashboard ejecutivo complejo.
- Estadísticas avanzadas para clientes.

La Fase 1 puede guardar campos que faciliten módulos futuros, como máquina asignada o hora deseada de entrega, pero no debe construir todavía los módulos completos de producción, embarques o repartidores.

## Etapas propuestas

### Etapa 0 — Preparación

Objetivo: dejar lista la base de trabajo.

Entregables:

- Documentación inicial.
- Repositorio con estructura base.
- Decisiones técnicas.
- Modelo de datos inicial.
- Importación o carga manual inicial de clientes y productos.
- Carga inicial opcional de preferencias cliente-producto-máquina.

### Etapa 1 — MVP de captura

Objetivo: que el cliente pueda enviar su pedido y que PRODIMT pueda verlo y revisarlo.

Entregables:

- Login simple para cliente.
- Pantalla "Mi pedido de hoy".
- Productos frecuentes del cliente.
- Cantidad por producto/molde.
- Repetir pedido sugerido.
- Marcar "no pedir hoy".
- Guardar pedido.
- Vista administrativa de pedidos por fecha.
- Estado de clientes pendientes.
- Bandeja de pedidos tardíos pendientes de revisión.
- Bandeja de segundos pedidos o cambios pendientes de revisión.
- Decisión administrativa: aceptar, rechazar o aceptar con cambio de hora de entrega.

### Etapa 2 — Operación interna

Objetivo: que áreas internas usen la información sin esperar recaptura.

Entregables:

- Vista de producción por máquina.
- Vista de producción por molde/producto.
- Vista de embarques.
- Vista de reparto por cliente/ruta/repartidor.
- Vista de pedidos pendientes o tardíos.
- Exportación a Excel si aún se requiere.
- Roles internos por departamento.

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
- Un cliente puede indicar claramente que no pedirá hoy.
- Administración puede ver quién pidió, quién no pidió y quién falta.
- Los pedidos tardíos quedan identificados y pendientes de decisión.
- Los segundos pedidos del día quedan identificados y pendientes de decisión.
- La información puede consultarse sin leer mensajes de WhatsApp.
- El proceso puede convivir con llamadas manuales solo para clientes rezagados.


---


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
- Hora deseada de entrega opcional.
- Ventana deseada de entrega opcional.
- Notas de entrega opcionales.
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
- Fecha de pedido.
- Fecha de entrega o producción.
- Estado.
- Líneas de pedido.
- Cantidad por producto/molde.
- Observaciones opcionales.
- Hora o ventana deseada de entrega copiada del perfil del cliente, editable por administración.

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
- Si fue enviado antes o después de la hora límite.

### FR-010 — Marcar no pedido

El cliente o un usuario interno debe poder registrar que el cliente **no pedirá hoy**.

Esta acción reemplaza el significado de `x/X` del Excel.

Criterios:

- Debe sacar al cliente de la lista de pendientes.
- Debe registrarse con fecha, hora, canal y usuario.
- No debe confundirse con una cantidad cero en una línea específica.
- Debe poder auditarse.

### FR-011 — Estado de clientes pendientes

El área administrativa debe ver qué clientes no han enviado pedido ni han indicado no pedido antes de la hora límite.

Esto reemplaza la lista mental/manual de llamadas.

### FR-012 — Captura interna en nombre del cliente

Un usuario interno debe poder capturar o editar el pedido por teléfono cuando el cliente no use la app.

El pedido debe quedar marcado con canal `CapturadoInternamente` o equivalente.

### FR-013 — Vista administrativa diaria

El sistema debe mostrar pedidos por fecha con filtros básicos:

- Cliente.
- Producto/molde.
- Estado.
- Canal de captura.
- Pendiente/confirmado/no pidió.
- Hora de captura.
- Pedido tardío.
- Requiere revisión administrativa.

### FR-014 — Exportación inicial

El sistema debe poder generar una salida operativa simple, idealmente en Excel o CSV, para transición.

La exportación debe permitir ordenar o agrupar por:

- Producto/molde.
- Cliente.
- Ruta/repartidor.
- Estado.
- Máquina asignada, solo para uso interno.

### FR-015 — Roles

Roles mínimos:

- Cliente.
- Administración.
- Producción.
- Embarques.
- Reparto.
- Consulta gerencial.

En MVP pueden implementarse primero Cliente y Administración, dejando Producción, Embarques y Reparto como vistas protegidas posteriores.

### FR-016 — Auditoría

El sistema debe registrar cambios importantes:

- Creación de pedido.
- Edición de pedido.
- Confirmación.
- Cancelación.
- Registro de no pedido.
- Captura interna.
- Cambio posterior a hora límite.
- Segundo pedido o cambio del día.
- Decisión administrativa.
- Cambio de hora o ventana de entrega.
- Cambio de máquina asignada.

### FR-017 — Hora límite

El sistema debe manejar una hora límite configurable, inicialmente 10:00 a.m.

Después de esa hora, el pedido no se rechaza automáticamente. Debe:

- Marcarse como tardío.
- Quedar con revisión administrativa pendiente.
- Permitir a administración aceptar, rechazar o aceptar con modificación de hora/condición de entrega.

### FR-018 — Segundo pedido o cambio del mismo día

Si un cliente ya tiene pedido confirmado o enviado para la fecha y quiere enviar otro, el sistema debe crear una solicitud pendiente de revisión administrativa.

La administración debe poder:

- Aceptar el pedido adicional.
- Rechazarlo.
- Integrarlo como cambio al pedido anterior.
- Aceptarlo con cambio de hora o condición de entrega.

### FR-019 — Catálogo de máquinas

El sistema debe permitir registrar máquinas de forma interna.

La máquina puede asignarse por defecto a clientes o pedidos, pero:

- El cliente no debe ver la máquina.
- La vista de producción por máquina queda fuera de la Fase 1.
- El dato se guarda desde el inicio para preparar fases posteriores.

### FR-020 — Asignación interna de máquina

El sistema debe permitir que un administrador cambie la máquina asignada a un pedido o línea de pedido en situaciones especiales.

El cambio debe quedar auditado.

### FR-021 — Canal de venta Mostrador

El sistema debe tratar `Mostrador` como canal de venta interno, no como cliente externo.

Los pedidos de mostrador deben poder registrarse internamente, pero no deben afectar estadísticas o preferencias de clientes externos.


---


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


# 06 — Modelo de datos inicial

Este modelo es conceptual. Debe refinarse durante el diseño técnico.

## Entidades principales

### Customer

Representa al cliente externo.

Campos candidatos:

- CustomerId
- DisplayName
- LegalName opcional
- PrimaryPhone
- SecondaryPhone opcional
- RouteId opcional
- PreferredDeliveryTime opcional
- PreferredDeliveryWindowStart opcional
- PreferredDeliveryWindowEnd opcional
- DeliveryNotes opcional
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
- DefaultMachineId opcional, interno

### Machine

Representa una máquina de producción o atención interna.

Campos candidatos:

- MachineId
- MachineNumber
- DisplayName
- IsActive

Notas:

- La máquina no debe mostrarse al cliente.
- Puede usarse después para vistas de producción.
- En Fase 1 basta con modelarla y permitir asignación básica.

### CustomerMachineAssignment

Asignación interna por defecto entre cliente, producto y máquina.

Campos candidatos:

- CustomerMachineAssignmentId
- CustomerId
- ProductId opcional
- Weekday opcional
- MachineId
- IsDefault
- IsActive

Esta entidad puede omitirse temporalmente si en MVP se decide guardar `DefaultMachineId` directamente en `CustomerProductPreference`.

### Order

Pedido del cliente para una fecha.

Campos candidatos:

- OrderId
- CustomerId nullable para ventas internas de mostrador
- OrderDate
- DeliveryDate opcional
- RequestedDeliveryTime opcional
- RequestedDeliveryWindowStart opcional
- RequestedDeliveryWindowEnd opcional
- Status
- CaptureChannel
- SalesChannel
- SubmittedAt
- SubmittedByUserId opcional
- Notes
- IsLate
- RequiresAdminReview
- AdminReviewReason opcional
- ReviewedByUserId opcional
- ReviewedAt opcional
- AdminDecision opcional
- RejectionReason opcional
- SequenceNumber
- CreatedAt
- UpdatedAt

Estados candidatos:

- Draft
- Submitted
- PendingAdminReview
- Accepted
- Rejected
- Cancelled
- NoOrder
- Superseded

Razones de revisión administrativa candidatas:

- LateSubmission
- AdditionalOrderSameDay
- PostConfirmationEdit
- ManualAdminReview

Decisiones administrativas candidatas:

- Pending
- Accepted
- Rejected
- AcceptedWithDeliveryTimeChange
- AcceptedWithChanges

Canales de captura candidatos:

- CustomerApp
- InternalCall
- WhatsAppConfirmation
- Import
- AdminEdit

Canales de venta candidatos:

- ExternalCustomer
- InternalCounter, equivalente a Mostrador

### OrderLine

Detalle del pedido.

Campos candidatos:

- OrderLineId
- OrderId
- ProductId
- Quantity
- Unit opcional
- Notes
- AssignedMachineId opcional, interno
- SourceSuggestionLineId opcional
- WasChangedFromSuggestion

### NoOrderRecord

Puede modelarse como una entidad separada o como un `Order` con estado `NoOrder` y sin líneas.

Recomendación inicial: modelarlo como `Order.Status = NoOrder` para que el cliente salga de pendientes y todo quede en la misma línea de tiempo.

Campos candidatos si se separa:

- NoOrderRecordId
- CustomerId
- OrderDate
- CaptureChannel
- SubmittedAt
- SubmittedByUserId opcional
- Notes opcional

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

1. Un cliente puede tener máximo un pedido activo aceptado o enviado por fecha sin revisión adicional.
2. Si un cliente intenta crear otro pedido el mismo día, debe crearse una solicitud pendiente de revisión administrativa.
3. Un pedido tardío después de las 10:00 a.m. debe marcarse como `IsLate = true` y `RequiresAdminReview = true`.
4. Un pedido puede tener muchas líneas.
5. Una línea de pedido debe tener producto y cantidad.
6. La cantidad debe ser mayor o igual a cero.
7. Cero significa cantidad cero en un producto; `x/X` del Excel significa no pidió y debe mapearse a estado `NoOrder`.
8. Todo cambio después de la hora límite debe marcarse como tardío o auditado.
9. Las sugerencias nunca deben enviarse como pedido confirmado sin acción del cliente o usuario interno.
10. La máquina asignada es interna y no debe exponerse en endpoints o vistas de cliente.
11. `Mostrador` debe manejarse como canal de venta interno, no como cliente externo.
12. La hora deseada de entrega vive en el perfil de cliente, pero debe copiarse al pedido para conservar el contexto histórico.

## Limpieza requerida

Antes de importar masivamente:

- Normalizar nombres de clientes.
- Unificar moldes equivalentes.
- Mapear `x/X` a no pedido.
- Mapear columna E a máquina asignada.
- Separar `Mostrador` como canal interno.
- Definir catálogo inicial de máquinas.


---


# 07 — Backlog MVP

## Épica 1 — Base del repositorio

- Crear solución .NET con Clean Architecture.
- Crear proyecto Angular mobile first.
- Configurar SQL Server local/dev.
- Configurar variables de entorno.
- Crear guía de ejecución local.
- Crear pruebas base.
- Configurar OpenAPI/Swagger.

## Épica 2 — Catálogos

- Crear entidad Customer.
- Agregar hora o ventana deseada de entrega a Customer.
- Crear entidad Product.
- Crear entidad Machine para uso interno.
- Crear relación CustomerProductPreference.
- Agregar asignación interna de máquina por preferencia o cliente-producto.
- Crear endpoints CRUD internos para catálogos.
- Cargar catálogo inicial manual o mediante seed.
- Tratar `Mostrador` como canal interno, no como cliente.

## Épica 3 — Pedido del cliente

- Crear pantalla "Mi pedido de hoy".
- Mostrar productos frecuentes.
- Mostrar sugerencia.
- Permitir editar cantidades.
- Permitir agregar producto no frecuente.
- Confirmar pedido.
- Permitir marcar "No pedir hoy".
- Mostrar estado de pedido enviado.
- Ocultar cualquier dato de máquina en la vista del cliente.

## Épica 4 — Administración

- Ver pedidos por fecha.
- Filtrar por cliente.
- Ver clientes pendientes.
- Ver clientes que marcaron no pedido.
- Capturar pedido en nombre de cliente.
- Editar pedido antes de cierre.
- Marcar pedido tardío.
- Revisar pedidos tardíos.
- Revisar segundos pedidos o cambios del día.
- Aceptar pedido.
- Rechazar pedido.
- Aceptar con modificación de hora o condición de entrega.
- Cambiar máquina asignada de forma interna cuando sea necesario.

## Épica 5 — Sugerencias

- Obtener último pedido del mismo día de la semana.
- Obtener últimos 3 pedidos del mismo día.
- Calcular sugerencia simple.
- Mostrar diferencia contra sugerencia.

## Épica 6 — Estados y reglas de revisión

- Configurar hora límite inicial: 10:00 a.m.
- Detectar pedido tardío.
- Detectar segundo pedido del mismo día.
- Crear estado `PendingAdminReview` o equivalente.
- Registrar razón de revisión administrativa.
- Registrar decisión administrativa.
- Registrar rechazo con motivo.

## Épica 7 — Exportación operativa

- Exportar pedidos del día a CSV o Excel.
- Agrupar por producto/molde.
- Agrupar por cliente.
- Incluir máquina asignada solo en exportación interna.
- Preparar transición con operación.

## Épica 8 — Seguridad y auditoría

- Roles mínimos.
- Autorización por cliente.
- Auditoría de cambios.
- Proteger endpoints internos.
- Validar que endpoints de cliente no expongan máquina, otros clientes o datos internos.

## Definición de terminado para MVP

- Cliente piloto puede enviar pedido desde celular.
- Cliente piloto puede marcar que no pedirá hoy.
- Administración puede ver pedido sin WhatsApp.
- Administración puede ver pendientes antes de llamadas.
- Sistema identifica pedidos tardíos.
- Sistema identifica segundos pedidos o cambios del mismo día.
- Administración puede aceptar o rechazar pedidos sujetos a revisión.
- Sistema evita duplicar pedido activo del mismo cliente y fecha sin revisión administrativa.
- Pedido queda guardado en SQL Server.
- Hay pruebas E2E del flujo principal.
- La documentación se actualiza con lo construido y lo pendiente.


---


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
- Las decisiones operativas confirmadas están documentadas en `docs/10-decisiones-operativas-confirmadas.md`.

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
11. La máquina asignada es dato interno y no debe exponerse al cliente.
12. Pedidos tardíos y segundos pedidos del día requieren decisión administrativa.
13. `Mostrador` es canal interno, no cliente externo.

## Flujo principal a implementar primero

1. Cliente entra desde celular.
2. Sistema identifica al cliente.
3. Sistema muestra sugerencia de pedido.
4. Cliente repite o edita cantidades.
5. Cliente confirma.
6. Si está dentro de horario y no existe pedido previo, el pedido queda enviado/aceptado según regla MVP.
7. Si está fuera de horario, queda pendiente de revisión administrativa.
8. Si ya existía pedido del día, queda pendiente de revisión administrativa.
9. Administración ve el pedido.
10. Administración ve quién falta de pedir.
11. Administración acepta, rechaza o ajusta pedidos sujetos a revisión.

## Flujo alterno: no pedido

1. Cliente entra desde celular o administración registra llamada.
2. Se marca "No pedir hoy".
3. El cliente sale de pendientes.
4. Se guarda registro auditable.

## No implementar primero

- Dashboard ejecutivo complejo.
- Inteligencia artificial avanzada.
- Optimización de rutas.
- Integración completa de WhatsApp.
- Reemplazo total del Excel.
- Facturación.
- App nativa iOS/Android.
- Vista completa de producción por máquina.
- Vista completa de embarques.
- Vista completa de repartidores.

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
- Machine
- CustomerProductPreference
- Order
- OrderLine
- AuditLog
- User

## Criterio para futuras sesiones

Al iniciar una nueva sesión, leer siempre:

- `README.md`
- `docs/08-contexto-para-codex.md`
- `docs/10-decisiones-operativas-confirmadas.md`
- `docs/11-reglas-de-negocio-fase-1.md`
- `docs/12-flujos-fase-1.md`
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


# 09 — Preguntas abiertas

Este documento separa decisiones ya resueltas de preguntas todavía abiertas.

## Decisiones ya resueltas

1. `x/X` en el Excel significa **no pidió**.
2. La hora límite inicial es **10:00 a.m.**.
3. Después de la hora límite, el pedido debe marcarse como tardío y quedar sujeto a decisión administrativa.
4. Si un cliente hace más de un pedido al día, el administrador debe decidir si lo acepta o no.
5. Algunos clientes tienen horario específico o deseado de entrega; debe registrarse en el catálogo de cliente como opcional.
6. `Mostrador` es canal de venta interno, no cliente externo.
7. La columna E del Excel es el número de máquina que atenderá el pedido.
8. La máquina asignada no debe mostrarse al cliente.
9. La Fase 1 se centrará en obtener la información del pedido del cliente.
10. Las vistas de producción, embarques y repartidores se definirán como módulos posteriores.

## Preguntas abiertas para cerrar MVP

### Operación

1. Cuando un pedido tardío se acepta, ¿debe quedar marcado permanentemente como tardío para reportes?
2. ¿Qué motivos de rechazo se usarán inicialmente?
3. ¿El cliente debe recibir aviso dentro de la app cuando un pedido tardío sea rechazado o aceptado con cambio?
4. ¿La administración puede modificar cantidades al aceptar un pedido tardío o duplicado?
5. ¿La hora límite aplica igual todos los días o puede cambiar por día de la semana?
6. ¿Qué pasa si el cliente marca "No pedir hoy" y luego intenta hacer pedido después?

### Entrega

7. ¿La hora deseada de entrega se guarda como una hora exacta o como ventana, por ejemplo 08:00-09:00?
8. ¿La hora deseada de entrega puede cambiar por día de la semana?
9. ¿Quién puede modificar la hora de entrega solicitada?
10. ¿Debe existir una hora prometida por PRODIMT diferente a la hora deseada por el cliente?

### Catálogo

11. ¿Cuál es la lista oficial de moldes/productos?
12. ¿Cómo deben normalizarse equivalencias como `# 10 1/ 2`, `#10.5` y `#10½`?
13. ¿Cuál es el catálogo oficial de máquinas?
14. ¿La máquina por defecto se asigna por cliente, por cliente-producto o por ruta?

### Clientes

15. ¿Cada cliente tiene un teléfono único?
16. ¿Hay clientes con varios encargados?
17. ¿Hay clientes que compran en más de una ruta?
18. ¿Todos los clientes deben poder ver estadísticas en etapas posteriores?

### WhatsApp

19. ¿PRODIMT ya tiene WhatsApp Business Platform o solo WhatsApp normal?
20. ¿Se cuenta con consentimiento de clientes para mensajes automáticos?
21. ¿El mensaje debe permitir confirmar sin abrir la app, o basta con liga a la app?

### Departamentos posteriores

22. ¿Cuál será la primera vista interna posterior a captura: producción por máquina, embarques o repartidores?
23. ¿Qué campos necesita cada departamento?
24. ¿Deben conservarse formatos parecidos al Excel para facilitar adopción?

### Piloto

25. ¿Con cuántos clientes se probará primero?
26. ¿Qué clientes son ideales para piloto?
27. ¿Cuánto tiempo convivirá el sistema con WhatsApp manual?


---


# 10 — Decisiones operativas confirmadas

Fecha: 2026-05-27

Este documento registra decisiones de negocio ya confirmadas para evitar que se vuelvan a discutir en cada sesión de desarrollo.

## D-001 — Significado de `x/X`

En el Excel actual, `x` o `X` significa **no pidió**.

Implicación para el sistema:

- No debe guardarse como texto de pedido.
- No debe interpretarse simplemente como cantidad cero.
- Debe representarse como estado explícito de cliente sin pedido para la fecha.

## D-002 — Pedidos tardíos

La hora límite inicial es 10:00 a.m.

Si un cliente hace pedido después de la hora límite:

- El pedido debe marcarse como tardío.
- El pedido debe quedar pendiente de revisión administrativa.
- El administrador puede aceptarlo, rechazarlo o aceptarlo con cambio de hora/condición de entrega.

## D-003 — Más de un pedido por cliente en el mismo día

Si un cliente ya tenía pedido confirmado o enviado y solicita otro pedido el mismo día:

- El sistema no debe aceptarlo automáticamente.
- Debe quedar pendiente de revisión administrativa.
- El administrador decide si lo acepta, rechaza, fusiona con el pedido anterior o ajusta la entrega.

## D-004 — Hora deseada de entrega

Algunos clientes tienen un horario específico o deseado para recibir su pedido.

Implicación para el sistema:

- El catálogo de clientes debe tener hora o ventana deseada de entrega como campo opcional.
- El pedido debe copiar esa información al momento de capturarse para mantener histórico.
- Administración debe poder modificar la hora/condición de entrega al aceptar un pedido tardío o duplicado.

## D-005 — Mostrador

`Mostrador` es un canal de venta interno.

Implicación para el sistema:

- No debe tratarse como cliente externo.
- No debe afectar estadísticas de clientes.
- Puede existir como canal de captura/venta interna.

## D-006 — Columna E del Excel

La columna E de las hojas diarias representa el número de máquina que atenderá el pedido.

Implicación para el sistema:

- Se debe modelar máquina como dato interno.
- Normalmente cada máquina tiene clientes asignados.
- Un administrador puede cambiar la máquina en situaciones especiales.
- El cliente no debe saber qué máquina atenderá su pedido.

## D-007 — Enfoque de Fase 1

La Fase 1 se concentrará en obtener la información del pedido del cliente.

Quedan para fases posteriores:

- Vista de producción por máquina.
- Vista de embarques.
- Vista de repartidores.
- Estadísticas avanzadas para clientes.
- WhatsApp automático completo.


---


# 11 — Reglas de negocio Fase 1

## BR-001 — Cliente pendiente

Un cliente está pendiente para una fecha cuando:

- Está activo.
- Se espera que pueda pedir ese día.
- No tiene pedido enviado/aceptado.
- No tiene registro de no pedido.

## BR-002 — No pedido

Cuando un cliente indica que no pedirá:

- Se registra un pedido o evento con estado `NoOrder`.
- El cliente sale de pendientes.
- La acción se audita.

## BR-003 — Pedido dentro de horario

Si el cliente envía pedido antes de la hora límite y no tiene pedido previo activo para la fecha:

- El pedido puede quedar como `Submitted` o `Accepted` según regla operativa del MVP.
- No requiere revisión administrativa automática.

Recomendación inicial: usar `Submitted` para indicar que el cliente lo envió y permitir que administración lo procese.

## BR-004 — Pedido tardío

Si el cliente envía pedido después de la hora límite:

- `IsLate = true`.
- `RequiresAdminReview = true`.
- `AdminReviewReason = LateSubmission`.
- Estado sugerido: `PendingAdminReview`.

El administrador puede:

- Aceptar.
- Rechazar.
- Aceptar con cambios.
- Aceptar con cambio de hora o condición de entrega.

## BR-005 — Segundo pedido del día

Si un cliente ya tiene pedido activo y envía otro pedido o cambio para la misma fecha:

- El sistema no debe reemplazar el pedido anterior automáticamente.
- Debe crear una solicitud o pedido pendiente de revisión.
- `AdminReviewReason = AdditionalOrderSameDay` o `PostConfirmationEdit`.

## BR-006 — Hora deseada de entrega

El perfil del cliente puede tener hora o ventana deseada de entrega.

Al crear un pedido:

- La hora o ventana deseada se copia al pedido.
- Administración puede ajustarla si acepta un pedido tardío, duplicado o especial.

## BR-007 — Máquina asignada

La máquina asignada es dato interno.

Reglas:

- Puede venir de la preferencia cliente-producto.
- Puede cambiarla un administrador.
- No debe mostrarse al cliente.
- Todo cambio debe auditarse.

## BR-008 — Mostrador

Los pedidos de mostrador se registran como canal interno.

Reglas:

- No se consideran pedidos de cliente externo.
- No afectan sugerencias personalizadas de clientes.
- Pueden aparecer en vistas internas y exportaciones.

## BR-009 — Sugerencia de pedido

La sugerencia no confirma pedido por sí sola.

Reglas:

- Debe requerir acción del cliente o usuario interno.
- Debe basarse primero en últimos pedidos del mismo día de la semana.
- Debe mostrar productos frecuentes antes que productos no frecuentes.

## BR-010 — Auditoría mínima

Deben auditarse:

- Pedido creado.
- Pedido confirmado.
- Pedido editado.
- No pedido.
- Pedido tardío.
- Segundo pedido del día.
- Decisión administrativa.
- Cambio de entrega.
- Cambio de máquina.


---


# 12 — Flujos Fase 1

## Flujo A — Cliente confirma pedido sugerido

1. Cliente entra a la app desde celular.
2. Sistema identifica al cliente.
3. Sistema carga productos frecuentes.
4. Sistema muestra sugerencia basada en historial.
5. Cliente confirma o edita cantidades.
6. Sistema valida hora límite.
7. Sistema valida si ya existe pedido del día.
8. Si no hay condición especial, guarda pedido.
9. Administración puede verlo en el panel diario.

## Flujo B — Cliente indica no pedido

1. Cliente entra a la app.
2. Selecciona "No pedir hoy".
3. Sistema registra estado `NoOrder`.
4. Cliente sale de pendientes.
5. Administración puede verlo como cliente que no pidió.

## Flujo C — Pedido tardío

1. Cliente entra después de la hora límite.
2. Sistema permite capturar el pedido para no perder la información.
3. Sistema marca el pedido como tardío.
4. Sistema lo envía a revisión administrativa.
5. Administración decide:
   - aceptar,
   - rechazar,
   - aceptar con cambios,
   - aceptar con cambio de hora/condición de entrega.
6. La decisión queda auditada.

## Flujo D — Segundo pedido o cambio del día

1. Cliente ya tiene pedido registrado para la fecha.
2. Cliente intenta enviar otro pedido o modificar el anterior.
3. Sistema no reemplaza automáticamente el pedido confirmado.
4. Sistema crea solicitud pendiente de revisión.
5. Administración decide si acepta, rechaza, fusiona o ajusta entrega.
6. La decisión queda auditada.

## Flujo E — Captura interna por llamada

1. Administración ve clientes pendientes.
2. Administración llama al cliente.
3. Si el cliente pide, administración captura el pedido en su nombre.
4. Si el cliente no pide, administración marca `NoOrder`.
5. El registro queda con canal interno y usuario que capturó.

## Flujo F — Cambio interno de máquina

1. Un pedido tiene máquina asignada por defecto.
2. Administración detecta situación especial.
3. Administración cambia la máquina asignada.
4. El cliente no ve el cambio.
5. El cambio queda auditado.

## Flujo G — Pedido de mostrador

1. Un usuario interno registra venta o pedido de mostrador.
2. El sistema guarda el pedido con canal `InternalCounter` o equivalente.
3. No se asocia a cliente externo salvo que operación lo requiera.
4. No afecta sugerencias ni estadísticas de clientes externos.


---


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


---


# ADR-0003 — Decisiones operativas para Fase 1

Fecha: 2026-05-27

## Estado

Aceptada.

## Contexto

El sistema debe sustituir gradualmente la captura manual de pedidos en Excel. Durante el análisis se confirmaron reglas operativas que afectan el diseño del dominio y el alcance del MVP.

## Decisión

Se adoptan estas reglas para Fase 1:

1. `x/X` significa no pidió.
2. Pedidos después de las 10:00 a.m. son tardíos y requieren revisión administrativa.
3. Un segundo pedido o cambio del mismo día requiere revisión administrativa.
4. El cliente puede tener hora o ventana deseada de entrega.
5. `Mostrador` es canal interno, no cliente externo.
6. La columna E del Excel es máquina asignada.
7. La máquina asignada es información interna y no se expone al cliente.
8. Fase 1 se concentra en capturar pedidos; producción, embarques y repartidores quedan para módulos posteriores.

## Consecuencias

- El modelo debe incluir estados y razones de revisión administrativa.
- El perfil de cliente debe incluir datos opcionales de entrega.
- El sistema debe distinguir cliente externo, canal de captura y canal de venta.
- El backend debe proteger endpoints para que datos internos como máquina no salgan en DTOs de cliente.
- La UI de cliente debe enfocarse en pedido rápido, no en operación interna.
