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
