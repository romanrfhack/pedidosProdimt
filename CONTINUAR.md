# Continuar PRODIMT

Última sesión: 26 de octubre de 2026. Zona operativa: `America/Mexico_City`.

Este archivo es el punto de entrada para retomar el proyecto en otro chat o equipo. El repositorio conserva código, decisiones y verificaciones; los datos reales del piloto se mantienen privados.

## Leer primero

1. `AGENTS.md`.
2. `docs/proyecto/ESTADO.md` (avance y límites verificados).
3. `docs/proyecto/PLAN.md` (objetivo integral y etapas).
4. `docs/proyecto/DECISIONES.md` (decisiones vigentes y pendientes).
5. `docs/proyecto/BITACORA.md` (cambios y pruebas por sesión).
6. `docs/proyecto/INCREMENTO-05.md` (cambio de contraseña propia y sesiones administrativas).
7. `docs/proyecto/INCREMENTO-04.md` (avisos operativos y configuración por cliente).
8. `docs/proyecto/INCREMENTO-03.md` (instalación y preparación del ensayo).
9. `docs/proyecto/INCREMENTO-02.md` (captura diaria y correcciones).
10. `docs/proyecto/INCREMENTO-01.md` (fundamentos ya construidos).

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

I05 está implementado, validado y empaquetado para el I04 nativo vigente. Código final `886bee7b2202b399ee063fe65d524b7f444e205c`; CI 40411669397 con cuatro jobs correctos (212 .NET, 24 SQL, 55 E2E, 135 Python y smoke Production). `PRODIMT_Cambio_Contrasena_I05.zip` contiene binarios y el instalador sin migraciones; hash y límites en `docs/validation/2026-10-26-password-change.md`. No está instalado todavía en el VPS. La conexión SSH directa sigue sin ruta: el usuario ejecuta los comandos desde WSL y comparte sólo el JSON de instalación, sin credenciales.

Siguiente paso: instalar I05, confirmar `status: updated`/`health_verified: true`, recargar el navegador e iniciar sesión con su acceso actual. En «Cambiar contraseña» introduce actual, nueva y confirmación; el éxito invalida las sesiones anteriores y pide iniciar con la nueva. Después, retomar conciliación de catálogos y vista previa privada del Excel. No afirmar que la actualización o el cambio de contraseña ya ocurrieron sólo porque pasó CI.

## Al cerrar cada sesión

- Actualizar `ESTADO.md`: terminado, en curso, siguiente paso, bloqueos y rama/PR.
- Agregar una entrada a `BITACORA.md` con comandos, resultados y límites.
- Registrar decisiones nuevas en `DECISIONES.md` y actualizar el plan si cambia el alcance.
- Guardar código y documentación en un commit y comprobar su persistencia remota.
- No afirmar que el piloto está operando si sólo se ejecutaron pruebas locales.
