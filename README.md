# PRODIMT - Pedidos y operación

Sistema de captura de pedidos, catálogos y precios, revisión, producción por máquina y reparto por rutas/vueltas. Angular 21, ASP.NET Core/.NET 10 y EF Core/SQL Server. Se mantiene como monolito modular; la separación lógica no exige desplegar microservicios.

## Entradas vigentes

- [Continuar](CONTINUAR.md): siguiente sesión y restricciones.
- [Estado](docs/proyecto/ESTADO.md): código, pruebas, instalado y aceptación separados.
- [Matriz funcional](docs/proyecto/MATRIZ-FUNCIONAL.md): requisito, implementación y evidencia.
- [Arquitectura actual y SOLID](docs/proyecto/ARQUITECTURA-ACTUAL.md).
- [Auditoría profunda](docs/audits/2026-11-03/AUDITORIA.md) y [plan de consolidación](docs/proyecto/PLAN-CONSOLIDACION.md).
- [Índice documental](docs/README.md), [decisiones](docs/proyecto/DECISIONES.md) y [bitácora](docs/proyecto/BITACORA.md).
- [Candidato nativo C1 y validación del 4 de noviembre](docs/validation/2026-11-04-c1-native.md): gates WSL/SQL y límites de evidencia.

## Estado de entrega conocido

La aplicación instalada confirmada es `aa802e210d15c70784f79dea2580709166b843c7` (I23 integrado con RF1), con backend y frontend `ordering-schedules-i23-aa802e210d15` y 18 migraciones. [Evidencia del reporte del VPS](docs/validation/2026-11-03-schedules-installed.md). La aceptación visual/operativa sigue pendiente.

Primera entrega de [correcciones C1](docs/corrections/C1.md) sobre esa base, con regresión y seguimiento por hallazgo. **C1 no está desplegado.** El candidato integrado está preparado con instalador propio: exige navegador completo del mismo commit en WSL y validaciones EF/HTTP más SQL exacto en bases temporales antes de respaldo y migración 18→19. La [evidencia nativa](docs/validation/2026-11-04-c1-native.md) separa lo ejecutado de los gates todavía pendientes. No reutilizar el instalador I23 ni aplicar SQL manualmente. La auditoría original se conserva como corte histórico.

I16/productos, I18/seguridad, I19/producción e I21/Pedidos están incorporados en la base integrada I22. No se deben encadenar sus instaladores históricos.

## Módulos existentes

| Área | Función | Acceso principal |
|---|---|---|
| Cliente | Pedido rápido/manual, otros productos, NoOrder, fecha confirmada | `/cliente` |
| Pedidos y revisión | Consulta, cambios auditados, aceptar/rechazar | `/admin/pedidos`, `/admin/pendientes` |
| Catálogos | Clientes, productos, perfiles, precios y tokens | `/admin/catalogos` |
| Operación | Lotes físicos, máquina y asignación al pedido | `/admin/operacion` |
| Reparto | Ruta/vuelta, reservas por partida, conteo/carga/entrega y custodia | `/reparto`, `/admin/reparto` |
| Seguridad | Usuarios, roles y permisos aditivos | `/admin/seguridad` |
| Horarios | Herencia cliente/ruta y excepciones; I23 instalado | `/admin/horarios` |

La visibilidad del menú no concede permisos: el servidor valida acceso vigente por petición. El rol canónico del chofer es `Repartidor - Camioneta (sistema)`; un rol personalizado de nombre similar conserva sus permisos y no recibe otros por coincidencia de nombre.

## Desarrollo y validación

```bash
dotnet restore src/Prodimt.Pedidos.sln
dotnet build src/Prodimt.Pedidos.sln --configuration Release
dotnet test src/Prodimt.Pedidos.sln --configuration Release
```

Esa solución ejecuta Domain/Application; **SQL Server se ejecuta aparte**:

```bash
dotnet test tests/Prodimt.Pedidos.SqlServer.Tests/Prodimt.Pedidos.SqlServer.Tests.csproj --configuration Release
```

Requiere una instancia aislada mediante `PRODIMT_SQL_TEST_CONNECTION`; no usar una base operativa. La suite falla explícitamente si no tiene SQL y no lo sustituye por SQLite.

```bash
cd apps/prodimt-pedidos-web
npm ci
npm run build
```

Para UI con API simulada: `npm ci` y `npm test` dentro de `tests/e2e`, con Chromium/Playwright disponibles. El `npm test` de la carpeta Angular todavía no tiene target configurado: es el hallazgo F14. Los detalles del entorno y las comprobaciones realmente ejecutadas están en [VALIDACION](docs/audits/2026-11-03/VALIDACION.md).

## Reglas que se preservan

- Cantidades por partida y unidad; precio faltante es desconocido, no cero.
- Pedido, producción y reparto son registros distintos. Una aceptación no confirma fabricación ni entrega.
- Pedido 50 / carga 25 / entrega 20 deja 30 pendientes al cliente y 5 bajo custodia; el pedido original se conserva.
- Hora operativa en Ciudad de México; timestamp de auditoría y fecha de servicio no se confunden.
- Infraestructura, SQL, claves y cuentas existentes deben preservarse. No cargar datos reales en fixtures, logs o repositorio.
- Cobranza, firma/foto, cajas, offline y envío automático de WhatsApp son futuros alcances. Abrir un mensaje preparado no acredita envío.

La documentación inicial de mayo y los intentos anteriores se conservan como antecedentes. [README histórico](docs/proyecto/historico/README-hasta-aa802.md).
