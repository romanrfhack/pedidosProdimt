# Validación técnica — 24 de octubre de 2026

Este registro distingue la base recuperada, la corrección de dependencias y el incremento del piloto. No acredita una instalación productiva ni una migración aplicada a SQL Server.

## Base recuperada

Commit de referencia: `492bbe9e1069d32ae22565889f149aaf529393fd`.

- Instalación de dependencias desde los dos `package-lock.json`: correcta.
- Build Angular: correcto.
- Restore y build .NET: correctos; la base tenía nueve advertencias de dependencias NuGet, ninguna de compilación.
- Pruebas .NET de la base en un worktree separado: **68 correctas**, cero fallidas (5 de dominio y 63 de aplicación, incluidas las parametrizadas).
- Runtime usado: .NET SDK 10.0.402, runtime 10.0.13, Node 24.22.1, Ubuntu 24.04 x64.
- SQL Server y Docker no están disponibles en este entorno. Las pruebas de persistencia emplean SQLite; no equivalen a ejecutar SQL Server.

En este entorno MSBuild paralelo terminaba sin diagnóstico útil. Los comandos reproducibles usan `-m:1 -nr:false` y `-p:UseSharedCompilation=false` durante build. No cambia el comportamiento de la aplicación.

## Correcciones de dependencias

Se mantuvieron los frameworks y versiones mayores de las dependencias. Se añadieron referencias explícitas para elevar únicamente los paquetes transitivos afectados:

| Paquete | Antes | Ahora | Dónde |
| --- | --- | --- | --- |
| Microsoft.OpenApi | 2.0.0 | 2.7.5 | API |
| System.Security.Cryptography.Pkcs | 9.0.4 | 9.0.21 | Infrastructure, transitiva de SqlClient |
| SQLitePCLRaw.bundle_e_sqlite3 y binarios asociados | 2.1.11 | 2.1.14 | Pruebas de aplicación |

Verificación aislada de estos cambios sobre el código base: restore y build correctos, **cero advertencias**, las mismas **68 pruebas correctas**. La consulta `dotnet list src/Prodimt.Pedidos.sln package --vulnerable --include-transitive --no-restore` no reportó paquetes vulnerables en los seis proyectos, según el catálogo NuGet consultado en esta sesión. Es un resultado puntual de análisis de dependencias, no una auditoría completa de seguridad.

La biblioteca nativa incluida en SQLitePCLRaw.lib.e_sqlite3 2.1.14 respondió `3.54.0` a `sqlite3_libversion()` en Linux x64. La consulta comprobó el binario instalado, sin abrir datos del negocio.

Fuentes oficiales consultadas:

- [Advisory de Microsoft.OpenAPI.NET: corrección en 2.7.5](https://github.com/microsoft/OpenAPI.NET/security/advisories/GHSA-v5pm-xwqc-g5wc).
- [Advisory de .NET: Pkcs 9.0.21 corrige CVE-2026-84008](https://github.com/dotnet/runtime/security/advisories/GHSA-846f-f764-76mx).
- [Advisory de .NET: Pkcs 9.0.21 corrige CVE-2026-70162](https://github.com/advisories/GHSA-2fmv-rv2c-59x7).
- [NuGet: Pkcs 9.0.21](https://www.nuget.org/packages/System.Security.Cryptography.Pkcs/9.0.21).
- [NuGet: bundle SQLitePCLRaw 2.1.14 y sus dependencias](https://www.nuget.org/packages/SQLitePCLRaw.bundle_e_sqlite3/2.1.14).

## Verificación continua

`.github/workflows/ci.yml` contiene dos jobs independientes:

1. Restore, build y pruebas de .NET 10. Las pruebas usan datos sintéticos, SQLite y repositorios en memoria. También se verifica la correspondencia del modelo EF con sus migraciones y se genera SQL idempotente sin ejecutarlo contra una base.
2. Instalación con `npm ci`, build Angular y Playwright con Chromium y la API mock local. Un solo worker evita que los fixtures compartidos de la API mock se interfieran.

Node 24 satisface los engines del Angular CLI instalado (`^20.19.0 || ^22.12.0 || >=24.0.0`). El workflow tiene permisos `contents: read`, no recibe secretos y no despliega. Las acciones oficiales se fijaron a los commits que correspondían a checkout v7, setup-dotnet v6 y setup-node v7 al consultar sus referencias.

Fuentes: [checkout](https://github.com/actions/checkout), [setup-dotnet](https://github.com/actions/setup-dotnet), [setup-node](https://github.com/actions/setup-node), [Playwright CI](https://playwright.dev/docs/ci-intro).

La configuración anterior habilitaba telemetría de Angular CLI. Una primera ejecución E2E fue detenida por revisión automática al intentar conectar a Google Analytics. Se eliminó esa configuración (`cli.analytics: false`) y todas las verificaciones siguientes usan `NG_CLI_ANALYTICS=false`; .NET usa `DOTNET_CLI_TELEMETRY_OPTOUT=1`.

La descarga del Chromium solicitado por Playwright no produjo un ZIP válido en el entorno de trabajo. Para comprobación local se emplea el Chromium preinstalado mediante una configuración externa al repositorio. CI instala el Chromium correspondiente al lockfile; una ejecución local con el navegador alternativo no acredita por sí sola el resultado de CI.

## Incremento del piloto

Verificación local completada sobre el incremento:

| Comprobación | Resultado |
| --- | --- |
| Build .NET Release | Correcto, cero errores y cero advertencias |
| Pruebas .NET Release | **113 correctas**, cero fallidas (5 dominio + 108 aplicación) |
| Build Angular | Correcto, bundle inicial 417.35 kB |
| Playwright con API mock | **23 correctas**, cero fallidas |
| EF `has-pending-model-changes` | Sin cambios pendientes |
| SQL idempotente | Generado y revisado; **no aplicado** |
| API real, repositorios en memoria | `/health` y `/openapi/v1.json`: HTTP 200; documento OpenAPI 3.1.1 con 39 rutas |

El smoke OpenAPI comprueba que el paquete corregido mantiene la generación de documentación en ejecución. La API local se apagó después de comprobarla.

El script SQL generado contiene las seis migraciones, mide 21,417 bytes y su SHA-256 es `48a188ba0d4df0804e45866cb0d488e263d198c96175264f1700556260cb3d18`. La nueva migración es `20261024164610_AddPilotCatalogAndOrderSnapshots`. Se revisaron columnas, índices únicos, FK, longitudes 80/32/16 y condiciones de historial de migraciones. No convierte unidades desconocidas a kg ni borra datos históricos. El script se puede regenerar con:

```bash
dotnet tool restore
dotnet tool run dotnet-ef migrations has-pending-model-changes --project src/Prodimt.Pedidos.Infrastructure --startup-project src/Prodimt.Pedidos.Api --configuration Release --no-build
dotnet tool run dotnet-ef migrations script --idempotent --project src/Prodimt.Pedidos.Infrastructure --startup-project src/Prodimt.Pedidos.Api --configuration Release --no-build --output pilot-migrations.sql
```

La primera ejecución integrada detectó una incompatibilidad de mensaje de validación y un problema al intercambiar posiciones de interpretación bajo índice único. Se preservó el mensaje esperado y se hizo atómico el reemplazo del perfil, incluida su auditoría. La suite final incluye el intercambio y un caso de rollback de SQLite.

Este registro acredita comprobaciones locales. El estado de la primera ejecución del workflow en GitHub se registra por separado al publicar el incremento.
