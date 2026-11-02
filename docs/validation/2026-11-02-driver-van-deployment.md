# I22 revisión 2 — Despliegue con validación previa en bases temporales

## Autorización y estado real

El 2 de noviembre de 2026 el usuario autoriza expresamente desplegar I22 para validar la interfaz y el recorrido. La autorización permite preparar y aplicar el actualizador; no se solicita otra confirmación por las restricciones históricas del alcance. La publicación de fuentes en GitHub conserva su autorización pendiente independiente.

Se comprobó el endpoint HTTPS actual `/health/db`: status ok, database reachable. No fue posible abrir SSH desde el ejecutor porque el cliente rechaza la configuración del sistema por propietario/permisos inválidos, antes de conectar al VPS. No se modificó esa configuración ni se evitó su validación. Por tanto, el usuario ejecutará el comando desde su WSL habitual; preparar el ZIP no acredita despliegue.

## Validación automática antes de cambios operativos

El instalador `20261102-delivery-install2` exige dos comprobaciones antes de crear snapshot, preparar releases, escribir el marcador o detener PRODIMT:

1. Un ejecutable portátil .NET10, sin SDK ni Docker, llama las tres pruebas reales de `DeliveryManagementSqlTests`. Usa un nombre `ProdimtI02Test_<uuid>` generado por el fixture; la conexión inicial se fuerza a master, y las operaciones de negocio sólo usan esa base temporal.
2. Se prepara una segunda base `ProdimtI22Validation_<uuid>` desde las 16 migraciones de I19. Allí se ejecuta el SQL incremental exacto y sus permisos: rollback forzado antes del COMMIT, esquema/permisos bajo administrador y usuario DML, estado de seguridad conservado, repetición y limpieza verificada.

Los nombres de bases temporales se generan internamente y se validan por una expresión estricta. El helper no acepta nombres de base configurados por el usuario para ejecutar CREATE/DROP/ALTER. Las credenciales administrativas sólo pasan al hijo por entorno privado; no aparecen en argumentos, archivos o diagnóstico público. La salida se limita a marcadores conocidos y JSON sanitario.

Se verifica la desaparición de las bases temporales incluso tras respuesta incierta de CREATE, error o timeout. El supervisor mata y espera al hijo antes de limpiar únicamente el nombre UUID que éste había anunciado. Un marcador desconocido nunca se convierte en un DROP. Si falla la limpieza, el reporte identifica sólo esa base temporal y el despliegue no continúa.

Las cuatro DLL API/Application/Domain/Infrastructure deben ser idénticas byte por byte en backend y validador; el instalador y el constructor comprueban esa igualdad. Así, las pruebas no usan una compilación anterior distinta de la que se activará.

Sólo si las validaciones pasan se continúa con el respaldo COPY_ONLY/CHECKSUM y RESTORE VERIFYONLY de la base operativa, seguido de migración 17, activación y verificación. Las guardas de releases, configuración, claves, permisos y recuperación siguen vigentes.

## Compatibilidad del paquete

Se conserva la migración `20261103020000_AddDeliveryOperations` y el código funcional de d1ebb39. Esta revisión agrega el validador y su integración; no cambia pedidos, cantidades, roles publicados o contratos de reparto.

La base auxiliar 0→I19 se genera con EF idempotente y sólo se añaden las dos separaciones de lote ya revisadas para I12/I18, conservando 16 transacciones e identidades. Nunca se ejecuta ese baseline sobre la base operativa. La migración I22 operativa no cambia por esta revisión.

El validador publicado añade recursos de MVC.Testing/xUnit. El límite de entradas del payload aumenta de 250 a 700, conservando 100MiB descomprimidos, paths seguros, hashes y rechazo de archivos privados/debug. El paquete usa los runtimes .NET10 y sqlcmd ya instalados; no instala SDK, Docker, Node o dependencias del sistema.

## Verificaciones locales

- 33 pruebas Python focalizadas correctas: 19 de paquete y 14 nuevas de transporte, aislamiento, limpieza, secretos, rollback, repetición y orden anterior a escrituras.
- El runner se publicó fuera del repositorio y se ejecutó con un host .NET10 sin SDK (`--list-sdks` vacío). `--self-test` levantó la API mediante TestServer y obtuvo 200 de `/health`, sin abrir SQL.
- Tres rechazos seguros adicionales: conexión ausente, cadena inválida con canario secreto y AttachDbFilename. No se filtró el canario.
- El baseline real EF normalizado conserva 16 BEGIN/COMMIT y añade exactamente dos GO.
- Pasaron las 436 pruebas de la solución .NET (5 Domain y 431 Application). El paquete final se comprueba al cerrar. Los resultados anteriores no se presentan como ejecución SQL real: las tres pruebas y el transporte SQL se ejecutarán obligatoriamente en el destino antes de cambios operativos.
- Las 11 pruebas Playwright siguen pendientes; la revisión de interfaz y aceptación del piloto fue solicitada por el usuario después del despliegue.

## Comprobación y aplicación

El preflight predeterminado es de sólo lectura. `--validate-only` ejecuta ambas comprobaciones aisladas y devuelve validated/completed sin modificar el despliegue. `--apply` las ejecuta igualmente antes de continuar; no hace falta ejecutar ambos modos y repetirlas manualmente.

La cuenta SQL debe poder crear y eliminar las bases temporales, además de los permisos ya exigidos para migrar y respaldar PRODIMT. Se utilizan las credenciales administrativas existentes, solicitadas ocultamente en la terminal del VPS; no la contraseña de acceso web ni valores enviados por chat.

Aplicación correcta: updated/completed, isolated_validation_verified=true, validation_databases_cleaned=true, migrations_after=17, health_verified=true, frontend_verified=true y recovery_pending=false. Un resultado failed no se registra como instalación; se conserva su código para diagnosticar antes de reintentar el mismo paquete.

## Aceptación posterior

Con Ctrl+F5, Administración abre Reparto y Seguridad. Selecciona las cuentas que deben recibir Repartidor - Camioneta, configura rutas con sus códigos existentes, ubicaciones confirmadas y primera vuelta. No se crean usuarios, contraseñas, rutas o pedidos de demostración en la base operativa automáticamente. El piloto comprobará las cantidades reales, observaciones, segunda vuelta y los remanentes bajo custodia.
