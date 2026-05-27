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
