# Validación de la auditoría

Fecha de corte: 3 de noviembre de 2026, Ciudad de México. Fuente de aplicación: `aa802e210d15c70784f79dea2580709166b843c7`. Las modificaciones de esta entrega son documentales; no se corrigieron defectos de aplicación ni se desplegó un paquete.

## Resultados ejecutados en este entorno

| Comprobación | Resultado | Qué acredita / límite |
|---|---|---|
| Bundle I23 y siete archivos internos | CRC y SHA256 correctos; checkout aa802 exacto | Identidad de fuente, no despliegue |
| Build .NET Release | Correcto, cero avisos/errores en solución | SDK local 10.0.403; compilación sin servidores y un nodo |
| Domain/Application | 455 correctas: 5 Domain y 450 Application, cero omitidas | Incluye pruebas unitarias/HTTP con persistencia de pruebas; no sustituye SQL Server |
| Proyecto SqlServer.Tests | Compila; tres avisos EF1002/EF1003 en fixtures | No se ejecuta SQL real: no hay instancia aislada/sqlcmd/Docker disponibles aquí |
| Modelo EF | Sin cambios pendientes respecto de la última migración | No aplica la migración 18 ni accede al SQL productivo |
| Python de herramientas | 528 total; 524 correctas y cuatro omitidas | Tres fixtures requieren ownership root y una requiere parser systemd255; las omisiones no se cuentan como éxitos |
| Angular producción | Correcto; inicial 496.72 kB, transferencia estimada 129.05 kB | Seis avisos CSS de 4 kB; no se elevaron presupuestos |
| TypeScript estricto de Reparto/Horarios | Correcto | Sólo specs de integración/reparto/horarios seleccionados |
| TypeScript estricto de todos los specs | Falla: Buffer sin tipos Node en daily-orders.spec.ts:95 | Deuda real de configuración de tipos de la suite; no falla el build Angular |
| Playwright colección | 231 casos en 23 archivos | Enumerados, no ejecutados en navegador |
| Intento Playwright Horarios | Un fallo de arranque, tres sin ejecutar; cero assertions funcionales | Chromium aborta por socket EPERM/SIGABRT en process_singleton_posix.cc |
| Reproducción de historia A/B | Caso incorrecto reproducido | Método TypeScript real + transporte sintético; no navegador |
| Reproducción de borradores A/B de Horarios | Pérdida de B al guardar A reproducida | Métodos TypeScript reales + PUT/GET sintéticos; sin cambios reales |
| Contraste de claves de permisos | 4.21:1 y 3.89:1 | Cálculo de colores CSS; no auditoría WCAG completa |
| Tamaño de auditoría de perfil | Ejemplo sintético de nueve productos: 2131 caracteres frente a máximo 2000 | Incompatibilidad de contrato/esquema; error SQL no ejecutado |
| npm audit completo | 16 entradas moderadas, cero high/critical | Tooling transitivo; dos avisos raíz glob/minimatch; no afirmar 16 exploits distintos |
| npm audit --omit=dev | Cero reportadas | Dependencias runtime npm al momento del escaneo, no garantía general de seguridad |
| NuGet vulnerable/transitivo | Ninguna reportada en los seis proyectos de la solución | Fuente api.nuget.org; no incluye proyectos fuera de la solución ni pentest |
| HTTPS público | Índice RF1 exacto; HTTP 200 y BD reachable | Publicación y salud, sin consultas autenticadas ni verificación de roles/rutas reales |

Índice RF1 observado: SHA256 `79611c8dc193f8e99940521bd6ee114a6cb4f68eb73edea102e95f5f70d93d7e`. La inspección de esa respuesta no encontró CSP/HSTS/nosniff/frame/referrer-policy; no se inspeccionó la configuración viva del proxy. Ningún escaneo envió intentos de contraseña ni explotó producción.

## Comandos y evidencia de ejecución

La primera invocación .NET con procesos MSBuild paralelos falló por NamedPipe/Unix socket bloqueado. La compilación y las 455 pruebas se completaron después con un nodo y sin servidores de compilación; no se modificó código para lograrlo.

```bash
dotnet build src/Prodimt.Pedidos.sln --configuration Release --disable-build-servers -m:1 -p:UseSharedCompilation=false -nr:false
dotnet test src/Prodimt.Pedidos.sln --configuration Release --no-build --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false -nr:false
python3 -m unittest discover -s tests/pilot -p 'test_*.py'
```

En `apps/prodimt-pedidos-web`: `npm ci`, `npm run build`, `npm audit --json`, `npm audit --omit=dev --json`. NuGet: `dotnet list src/Prodimt.Pedidos.sln package --vulnerable --include-transitive --format json --no-restore`.

En `tests/e2e`: `npx playwright test --list` y un intento de `ordering-schedules.spec.ts` con Chromium del sistema, un worker y parada tras el primer fallo. No se reintentó el navegador después de confirmar la restricción. El typecheck usa el compilador de Angular con `--noEmit --strict --target es2022 --module commonjs --moduleResolution node --lib es2022,dom --skipLibCheck`; la comprobación global requiere incorporar tipos Node declarados.

EF: restauración de herramienta y `dotnet-ef migrations has-pending-model-changes --configuration Release --no-build` con Infrastructure/Api como proyecto y startup. No se cambió el manifest de herramienta ni se actualizó una dependencia para silenciar un aviso.

## Evidencia previa, distinta de esta ejecución

El usuario confirmó RF1 `updated/completed` con salud/frontend correctos y sin migración/reinicio. El registro WSL conservado de I23 aa802 muestra compilación/publicación, Application 450/450, modelo EF sin diferencias y un fallo posterior de SCP. La continuación disponible reporta validaciones portátiles 3/3 y bloqueo en SQL aislado antes de cambios; esas tres pruebas son de reparto, no cobertura funcional propia de Horarios.

No se afirma historia 18 instalada, aceptación de negocio, test SQL real ejecutado aquí ni recorrido de navegador correcto. El estado vigente está en [ESTADO](../../proyecto/ESTADO.md).

## Controles documentales

Se comprueban referencias de archivos/líneas de los hallazgos, enlaces Markdown locales, correspondencia de los 24 IDs con fases y cambios limitados a documentación. Se conservan los textos históricos y el bundle completo para recuperación. La auditoría no genera un instalador nuevo ni modifica artefactos operativos I23/RF1.
