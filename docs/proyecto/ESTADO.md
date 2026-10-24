# Estado del proyecto

Actualizado: 24 de octubre de 2026.

## Ubicación y etapa

- Repositorio: `romanrfhack/pedidosProdimt`.
- [PR #1](https://github.com/romanrfhack/pedidosProdimt/pull/1), rama `codex/pilot-foundation-2026-10-24`. Borrador, sin fusión ni despliegue.
- Base de I02: `a2eb7fb533982ba719ce74d0a73b4f631cd0d2f9` (I01 verificado).
- Código I02 verificado: `192c8d7871e2b2021b4d485dd71b5ebd7abbda4d`; [CI](https://github.com/romanrfhack/pedidosProdimt/actions/runs/40120275407). El commit documental posterior conserva el mismo código.
- **I02 implementado y validado en CI, incluido SQL Server real**: captura/importación diaria y correcciones. Contratos en `INCREMENTO-02.md`.
- **Etapa 1 en desarrollo; preparación operativa de etapa 0 aún incompleta.**
- Piloto real: no iniciado. No se ha cargado la ruta en una base operativa.

## Terminado

- I01: catálogos, unidades, perfiles versionados, máquina por producto, snapshots y demanda por máquina/ruta.
- I02: fecha de servicio explícita para administración y consulta/revisión por fecha.
- CSV diario revisable, origen y referencia externa persistentes, protección de reintentos dentro y entre lotes.
- Aplicación transaccional: lote inválido o fallo de base no deja pedidos ni recibos parciales.
- Corrección con motivo, versión esperada y clave de operación; reemplazo completo de partidas con historial antes/después.
- Preservación del nombre/unidad/máquina históricos al ajustar una partida existente; productos agregados usan el catálogo vigente.
- Interfaz de importación separada de catálogos, reintentos con el mismo contenido e historial legible.
- CSV de catálogos compatible con la marca UTF-8 de Excel.
- Migraciones aditivas `20261024181206_AddDailyOrderOperations` y `20261024181934_ExpandOrderAuditSnapshots`; historial completo sin el límite anterior de 2,000 caracteres.
- Revisión del XLSX devuelto: 23 identidades/ruta, acceso explícito, cinco condiciones de contraentrega y dos instrucciones internas. 52 fórmulas simples verificadas. CSV privado preparado; productos incompletos separados.
- Código/documentación públicos separados de los datos privados.

## Validación

- **157/157 pruebas .NET** correctas: 5 dominio y 152 aplicación; cero omitidas.
- **37/37 E2E** con API mock correctas, build Angular correcto y revisión móvil a 390 px sin desbordamiento.
- Revisión estática independiente de contratos, privacidad, snapshots, fechas y transacciones completada; hallazgos corregidos.
- **9/9 pruebas HTTP/SQL Server reales correctas**: migraciones, persistencia entre hosts, concurrencia con deadlocks reales y respuesta 409, rollback, correcciones, auditoría extensa y perfiles. Los fallos encontrados en la primera ejecución quedaron corregidos.
- SQL Server se ejecutó en CI contra una instancia desechable, eliminada al terminar. No se usaron datos reales ni una base operativa. Evidencia en `../validation/2026-10-24-i02.md`.
- Vista previa HTTP del CSV privado: **23/23 clientes válidos, cero errores**, 23 advertencias por teléfono vacío. No aplicado.
- InMemory conserva las funciones de I01 y la vista previa diaria. Aplicar lotes o corregir pedidos exige persistencia relacional; no se finge éxito durable.

## Siguiente paso

1. Definir instalación de prueba del piloto con SQL Server y acceso administrativo; aplicar migraciones allí.
2. Cargar mediante vista previa los catálogos privados ya preparados. No publicar ni sembrar clientes reales en CI.
3. Completar unidades, perfiles de producto, orden de visita, políticas de cobro faltantes e instrucciones de entrega. El XLSX no contiene esos valores en todas las filas.
4. Ratificar corte de 10:00 a.m. y calendario del ensayo; conservar esa regla mientras no se cambie explícitamente.
5. **I03:** ensayo de una semana por ruta, comparando captura→demanda→preparación y documentando diferencias; después avanzar a registro físico.

## Límites vigentes

- No hay parser ni conexión real con WhatsApp, producción física, empaque/carga/entrega ni pagos.
- La importación diaria requiere CSV depurado con códigos y unidades explícitos; no interpreta `15 + 20` ni importa XLSM.
- El historial de correcciones conserva evidencia, pero no representa ajustes de producción/entrega ya ejecutadas; ese control debe añadirse al incorporar etapa 2.
- La corrección conserva el estado del pedido. Un pendiente sigue excluido de demanda hasta su aceptación; un adicional sigue siendo otro pedido sujeto a revisión.
- El ticket es una condición operativa; no se generan tickets monetarios ni se marca pago al imprimir.
- Los campos vacíos y las fechas arrastradas del Excel no se convierten en valores operativos por suposición.
- Validación técnica, carga privada, despliegue y uso real son hitos distintos.
