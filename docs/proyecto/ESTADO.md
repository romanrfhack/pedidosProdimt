# Estado del proyecto

Actualizado: 24 de octubre de 2026.

## Ubicación del trabajo

- Repositorio: `romanrfhack/pedidosProdimt`.
- Base revisada: `492bbe9e1069d32ae22565889f149aaf529393fd`.
- Rama: `codex/pilot-foundation-2026-10-24`.
- Incremento: **I01 — Fundamentos del piloto, implementado y probado localmente**.
- Etapa general: **0 pendiente de validación operativa de datos; base de etapa 1 construida**.
- Piloto real: **no iniciado**. No hay despliegue nuevo ni datos reales cargados.

## Terminado

- Recuperación técnica y elección del repositorio existente.
- Unidades explícitas, desconocidos conservados y validación antes de capturar.
- Configuración de ruta/parada, llave, condición de cobro e instrucciones interiores, sólo para administración.
- Perfiles con versión, aprobación expresa, posiciones de interpretación y preferencia de máquina por producto.
- Invalidación de aprobación si cambia nombre, unidad o disponibilidad de un producto.
- Copia del contexto de producto/unidad, perfil, ruta y entrega en cada pedido nuevo.
- Fecha operativa CDMX, revisión administrativa con control de versión y secuencia única por cliente/fecha.
- Consulta e impresión de demanda por máquina y preparación por cliente/ruta, con pendientes separados.
- Compatibilidad de los cinco importadores de catálogos con CSV anteriores y campos nuevos opcionales.
- Migración EF aditiva, comprobaciones de código, pruebas y workflow de CI sin despliegue.
- Paquete privado persistido: hoja revisable de 23 clientes, 36 relaciones candidatas y evidencia de las fuentes. Aprobaciones inicialmente vacías.
- Registro de continuidad, plan, decisiones y bitácora en este repositorio.

## Verificado

- Base anterior: **68 pruebas .NET** correctas.
- Incremento integrado: **113 pruebas .NET** correctas (5 dominio + 108 aplicación); build Release con **0 errores y 0 advertencias**.
- Frontend: build correcto; **23 pruebas E2E** con API mock correctas. Última corrección visual verificada con su prueba específica y build posterior.
- Vista móvil de 390 px e impresión de una página inspeccionadas con datos ficticios.
- EF confirma que no hay cambios del modelo sin migración; SQL idempotente de las seis migraciones generado y revisado, no aplicado.
- Revisión estática independiente y comprobación de que no se agregaron datos reales al repositorio.
- Dependencias NuGet elevadas a parches sin hallazgos en la consulta puntual del catálogo de vulnerabilidades.

Ver detalles y límites en `../validation/2026-10-24-preflight.md` y `INCREMENTO-01.md`. Las pruebas SQLite y API mock no sustituyen la prueba contra SQL Server. El estado del workflow remoto se consulta en GitHub; un resultado local no acredita un resultado remoto.

## Siguiente incremento: I02

1. Abrir el XLSX privado y validar los campos amarillos: identidad/orden de clientes, ruta, productos, unidades, posiciones, máquinas, llave, instrucciones y cobro. No incorporar automáticamente cantidades manuscritas ni datos comerciales históricos.
2. Confirmar fecha de las hojas y vigencia del corte de 10:00 a.m.; `10:30 AM` de la fotografía no se convirtió en corte.
3. Preparar SQL Server de prueba, aplicar la migración y ejecutar smoke real; definir credenciales y acceso para un despliegue del piloto.
4. Cargar sólo catálogos revisados mediante vista previa y aplicación controlada.
5. Completar captura con fecha de servicio explícita, importación diaria con origen e identidad de operación, reintentos idempotentes y correcciones comerciales auditables.
6. Preparar una semana de ensayo de la ruta incluyendo todas sus máquinas/centros de preparación.

## Límites que no deben perderse al retomar

- Captura actual para la fecha de negocio de hoy. El plan permite consultar otras fechas; eso no habilita capturar pedidos históricos/futuros.
- No existe parser ni conexión real con WhatsApp.
- No hay importador diario de pedidos; los CSV actuales importan catálogos.
- La versión técnica protege revisión simultánea, pero no es historial completo de correcciones ni idempotencia de reintentos secuenciales.
- No hay división de una línea entre máquinas ni edición operativa de máquina por pedido.
- No hay registro físico de producido/preparado/cargado/entregado ni pagos.
- El indicador de ticket diario es una condición del cliente. No se generan tickets con precios/saldos ni se marca cobrado al imprimir.
- Se conservan desconocidos históricos; no se rellenan unidades o instrucciones desde suposiciones.
- Los datos privados se recuperan con el paquete de esta sesión; nunca desde este repositorio público.
