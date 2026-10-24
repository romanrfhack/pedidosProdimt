# Bitácora de trabajo

Cada entrada distingue hechos verificados, decisiones, pendientes y siguiente paso. Los resultados sólo se marcan como exitosos después de ejecutar la comprobación correspondiente.

## 2026-10-24 — Análisis y arranque de I01

### Entrada y decisiones

- Revisados dos libros de octubre, mensajes de WhatsApp y repositorios. El análisis produjo un plan integral por etapas.
- Usuario autorizó continuar, indicó que no hay datos operativos que conservar y eligió una ruta para el piloto.
- Nueva evidencia: fotografía de ruta (23 candidatos) y ticket histórico de otro cliente.
- Confirmadas reglas de llave y documento; desconocidos se conservan como tales.
- Se eligió `pedidosProdimt`, base `492bbe9e1069d32ae22565889f149aaf529393fd`, rama `codex/pilot-foundation-2026-10-24`.

### Trabajo

- Creado registro de continuidad en `CONTINUAR.md` y `docs/proyecto/`.
- Preparación privada del piloto separada del código público.
- Implementación I01 y pruebas en curso; el cierre de esta entrada registrará resultados concretos.


### Cierre de implementación I01

- Implementados catálogos, perfiles, snapshots, validaciones y vista administrativa de demanda/preparación descritos en `INCREMENTO-01.md`.
- Conservados desconocidos y separados datos internos de la vista del cliente.
- Corregido intercambio de posiciones del perfil mediante reemplazo transaccional, con prueba de rollback.
- Corregidas longitudes de snapshots para coincidir con el catálogo y evitar truncamiento en SQL Server.
- Revisiones duplicadas/obsoletas devuelven 409 y no vuelven a aplicar cambios.
- Añadido workflow `.github/workflows/ci.yml`, sin despliegue ni secretos.
- Telemetría de Angular deshabilitada; se ejecutó una alternativa segura después del bloqueo inicial del control automático.
- Elevadas tres dependencias NuGet afectadas a parches; evidencia en `../validation/2026-10-24-preflight.md`.

### Validaciones ejecutadas

- Base limpia en worktree separado: 68 pruebas .NET correctas.
- Incremento: 113 pruebas .NET correctas; build Release con cero errores y advertencias.
- Angular: build correcto. E2E: 23/23 con API mock y Chromium preinstalado; prueba visual específica posterior correcta.
- Inspección móvil e impresión con datos ficticios; `git diff --check` correcto.
- Revisión independiente de privacidad, contratos, transacciones y agrupaciones sin bloqueos nuevos.
- `dotnet ef migrations has-pending-model-changes`: sin cambios pendientes. SQL idempotente generado y revisado sin backfill de unidades.
- `graphify update .` no pudo ejecutarse porque la herramienta no está instalada; no existe grafo versionado que actualizar.
- No se ejecutó SQL Server ni se aplicó una migración a una instancia real. Ese paso queda explícito para I02.

### Persistencia y continuidad

- Código y documentación en la rama `codex/pilot-foundation-2026-10-24`; revisar la solicitud de cambios y su último commit antes de continuar.
- El XLSX privado de revisión y el ZIP con fuentes depuradas se guardaron y sus contenidos se verificaron. No forman parte del repositorio público.
- Próximo paso de negocio: validar el paquete privado; siguiente entrega técnica: I02 según `ESTADO.md`.

### Publicación comprobada

Implementación guardada en GitHub como `fde65007cb03614db0b1dcca11395a7451a374a5`, mediante [PR #1 en borrador](https://github.com/romanrfhack/pedidosProdimt/pull/1). Rama remota recuperada y comparada con el trabajo local; sin diferencias después de sincronizar. Los workflows remotos arrancaron; consultar su resultado en la PR. Este registro documental posterior no modifica el código probado.
