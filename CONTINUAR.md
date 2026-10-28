# Continuar PRODIMT

Última sesión: 28 de octubre de 2026. Zona operativa: `America/Mexico_City`.

Este archivo es el punto de entrada para retomar el proyecto en otro chat o equipo. El repositorio conserva código, decisiones y verificaciones; los datos reales del piloto se mantienen privados.

## Leer primero

1. `AGENTS.md`.
2. `docs/proyecto/ESTADO.md` (avance y límites verificados).
3. `docs/proyecto/PLAN.md` (objetivo integral y etapas).
4. `docs/proyecto/DECISIONES.md` (decisiones vigentes y pendientes).
5. `docs/proyecto/BITACORA.md` (cambios y pruebas por sesión).
6. `docs/proyecto/INCREMENTO-07.md` (captura rápida y actualización nativa I06→I07).
7. `docs/proyecto/INCREMENTO-06.md` (catálogo y descansos diarios).
8. `docs/proyecto/INCREMENTO-05.md` (cambio de contraseña propia y sesiones administrativas).
9. `docs/proyecto/INCREMENTO-04.md` (avisos operativos y configuración por cliente).
10. `docs/proyecto/INCREMENTO-03.md` (instalación y preparación del ensayo).
11. `docs/proyecto/INCREMENTO-02.md` (captura diaria y correcciones).
12. `docs/proyecto/INCREMENTO-01.md` (fundamentos ya construidos).

La documentación de mayo de 2026 es antecedente. Cuando difiera, las decisiones de octubre en `docs/proyecto/` prevalecen. No confundir capacidad implementada, prueba técnica, dato validado y operación real.

## Cómo retomar

```text
Continúa el sistema PRODIMT en romanrfhack/pedidosProdimt.
Lee CONTINUAR.md y los documentos que enlaza. Revisa la rama y PR
indicados en ESTADO.md antes de cambiar código. Ejecuta las comprobaciones
pendientes y continúa el siguiente incremento, sin repetir el análisis inicial.
No publiques datos reales de clientes ni instrucciones de acceso a locales.
```

El paquete privado de preparación del piloto se adjunta al chat de esta sesión. El usuario devolvió una revisión del XLSX y autorizó I02. Su normalización distingue valores confirmados, fórmulas resueltas y campos todavía ausentes. Consultar el paquete privado I02 más reciente antes de preparar una carga; no asumir que todas las celdas o perfiles quedaron completos. No recuperar pedidos, precios o saldos actuales a partir de fotografías históricas.

I03 agrega un paquete instalable y una hoja breve de pendientes que conserva esas confirmaciones. Antes de instalar, leer `infra/pilot/README.md`. No regenerar `.env` si ya existe un volumen del piloto; recuperar las credenciales privadas. CI usa exclusivamente datos ficticios y no prueba que el VPS del usuario esté instalado.

El usuario devolvió la hoja I03 con 30 unidades y 36 posiciones completadas. La revisión privada I04 es ahora la fuente más reciente; aún contiene seis unidades vacías, máquinas desconocidas y un valor cero sin interpretación confirmada. No reutilizar la hoja vacía anterior para una carga. Los números etiquetados como «Adicional» proceden de una columna original sin encabezado: no interpretarlos como cantidades, máquina ni posición. La explicación de pedidos aumentados por teléfono es una hipótesis del usuario pendiente de confirmar sobre ese archivo, no una reclasificación de los datos históricos.

I04 quedó desplegado en el VPS el 25 de octubre: el usuario compartió `status: updated`, nueve migraciones, permisos DML y salud/proceso/frontend HTTPS verificados con `20261025-resume5`. Se conservan el respaldo previo y uno nuevo verificado; no repetir el actualizador ni el bootstrap. El siguiente paso es comprobar login/catálogos desde el navegador y preparar la vista previa de la carga privada contra los datos existentes. La instalación no importó la ruta del Excel ni completó el ensayo operativo. Ver `ESTADO.md` y la evidencia de reanudación para las rutas de releases/respaldo.

El 26 de octubre el usuario confirmó la ruta correcta del API y ejecutó el alta de `roman.prodimt`: cuenta/auditoría verificadas, login y consulta administrativa HTTPS 200. La contraseña no se copia a git ni a esta documentación. No repetir el alta: la referencia inicial deja de coincidir tras cambiar la contraseña.

I05 está implementado, validado y empaquetado para el I04 nativo vigente. Código final `886bee7b2202b399ee063fe65d524b7f444e205c`; CI 40411669397 con cuatro jobs correctos (212 .NET, 24 SQL, 55 E2E, 135 Python y smoke Production). `PRODIMT_Cambio_Contrasena_I05.zip` contiene binarios y el instalador sin migraciones; hash y límites en `docs/validation/2026-10-26-password-change.md`. El usuario ejecutó el instalador el 26 de octubre y compartió `status: updated`, `phase: completed`, `health_verified: true`; ambos releases activos son `password-886bee7b2202`. Conserva nueve migraciones y no cambió la contraseña. La conexión SSH directa sigue sin ruta: el usuario ejecuta los comandos desde WSL y comparte sólo el JSON de instalación, sin credenciales.

El 27 de octubre el usuario confirmó login en navegador y solicitó la primera revisión funcional. Se comienza por I01: Catálogos → Clientes → Configurar, con la pantalla enfocada de I04. Comparar un cliente conocido con su información confirmada: productos, unidad de venta, orden visual, posiciones por cliente, máquina y datos internos de entrega/cobro. Guardar/recargar debe conservar la configuración; cantidad sugerida no es un pedido de hoy. La instalación no importó automáticamente el Excel: conciliar lo existente antes de preparar la vista previa privada. El cambio de contraseña no está confirmado por el simple hecho de haber podido iniciar sesión.

Para comenzar esa revisión el usuario informa que no ve clientes. Se preparan los 23 clientes confirmados de I04 en `PRODIMT_Clientes_Iniciales.xlsx` y `.csv`, guardados de forma privada. Solo datos básicos: códigos, nombres, ruta, llave, cobro e instrucciones confirmadas; teléfonos, orden de visita y otros desconocidos siguen vacíos. Importar el CSV en `/admin/importacion`, Tipo Clientes, mediante Validar y después Aplicar. Con catálogo vacío se esperan 23 altas y 0 errores; los teléfonos vacíos generan advertencias. Si se edita el XLSX, exportar su hoja Clientes como CSV UTF-8. La carga de clientes fue confirmada por el usuario el 28 de octubre; no repetirla. Productos/perfiles y máquinas se prepararán por separado.

El 28 de octubre el usuario confirmó la carga de clientes y aportó capturas del catálogo poblado. Solicitó I06: búsqueda principal, modal centrado bajo demanda para alta/edición y tarjetas según estado diario/descanso. Ver docs/proyecto/INCREMENTO-06.md. No volver a pedir el alta inicial. La programación semanal es nueva y explícita; no inferir descansos a partir del Excel. I06 está implementado, validado y empaquetado: código 965dc65d82d2322b8c897a164c592bad6ed975a9, CI 40710267512 con cuatro jobs correctos, incluido ODBC real del SQL incremental 9→10. PRODIMT_Catalogos_I06.zip preparado y verificado; instrucciones/hashes en docs/validation/2026-10-28-customer-catalog.md. El usuario ejecutó I06 el 28 de octubre y compartió status updated, phase completed, health_verified true, historial 9→10, esquema migrado verificado y conteos existentes conservados. Ambos releases activos: catalog-965dc65d82d2. Snapshot y respaldo SQL verificados constan en docs/validation/2026-10-28-customer-catalog.md. No repetir instalación/importación. Siguiente paso: Ctrl+F5 y aceptación de búsqueda, modal, tarjetas y descansos con datos reales.

El usuario solicita importar también los productos y su orden. Se recuperó la hoja privada I03 devuelta usada en I04: 36 relaciones de 23 clientes, 36 posiciones explícitas, 30 unidades kg, seis vacías, 19 máquinas positivas y 17 pendientes (16 vacías y un cero sin significado). Preparado PRODIMT_Configuracion_Productos_Clientes.zip con tres CSV y Excel de revisión, sin CSV de clientes: Productos (36 identidades provisionales separadas por relación), Máquinas (1, 2 y 3) y Productos frecuentes (36 filas/23 perfiles). El orden visual inicial sigue las posiciones que el usuario indicó, como dos campos independientes. No se fusionan variantes de clientes distintos ni se usan cantidades históricas. Se validó/aplicó localmente con los binarios I06 y almacenamiento temporal, comprobando los 23 perfiles completos y cero pedidos/aprobaciones automáticas. No se modificó VPS; el usuario debe Validar/Aplicar cada CSV en ese orden y revisar reemplazos. Detalle en docs/validation/2026-10-28-product-profiles-import.md.

El usuario confirma que cargó los catálogos sin error y consulta el siguiente paso. La carga real queda confirmada por su reporte, sin conteos/JSON ni conciliación individual adjuntos. No repetir importaciones. Se recomienda iniciar captura administrativa asistida de 3–5 pedidos reales usando la funcionalidad existente: /admin/clientes-pendientes, fecha de servicio explícita, Capturar pedido, frecuentes/cantidades y notas internas; revisar /admin/pedidos, autorizar pendientes y consultar /admin/operacion. Distinguir entrega mañana de hoy: las tarjetas I06 siguen mostrando hoy. Las seis unidades y 17 asignaciones pendientes del paquete no se dan por resueltas por una importación exitosa. Empezar con líneas de unidad confirmada y máquina conocida para revisar demanda; mantener pendientes separados. Posteriormente el usuario autorizó I07 de captura rápida por posiciones explícitas y alias configurables; su implementación ya está verificada, ver el cierre siguiente.

## Al cerrar cada sesión

- Actualizar `ESTADO.md`: terminado, en curso, siguiente paso, bloqueos y rama/PR.
- Agregar una entrada a `BITACORA.md` con comandos, resultados y límites.
- Registrar decisiones nuevas en `DECISIONES.md` y actualizar el plan si cambia el alcance.
- Guardar código y documentación en un commit y comprobar su persistencia remota.
- No afirmar que el piloto está operando si sólo se ejecutaron pruebas locales.

I07 fue autorizado el 28 de octubre: leer `docs/proyecto/INCREMENTO-07.md`. Código `46fed604732eddca0c7ac199b58bb0b429545b89`, CI 40765239145: cuatro jobs correctos (267 .NET, 29 SQL, 68 E2E, 162 Python y Production). Captura por posiciones y alias explícitos, entrega informativa, revisión/guardado y manual listos. Paquete PRODIMT_Captura_Rapida_I07.zip íntegro y evidencia en docs/validation/2026-10-28-quick-capture.md. Preservar catálogos importados; instalación y aceptación pendientes. El último estado remoto operativo confirmado sigue siendo I06.
