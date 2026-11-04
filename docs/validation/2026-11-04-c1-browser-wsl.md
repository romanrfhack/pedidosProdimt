# C1 · Segunda revisión después de ejecutar navegador en WSL

Fecha: 4 de noviembre de 2026, Ciudad de México. C1 sigue **sin desplegar**.
El usuario instaló las dependencias Linux de Chromium y compartió el resultado
de la segunda ejecución del candidato `e7693ab67a656aeb61f05e0f87f4f148b5026d1b`.

## Resultado recibido

- Las bibliotecas de Chromium, incluida `libnspr4`, quedaron instaladas en WSL.
- Compilación Angular, componentes y tipado terminaron correctamente.
- Chromium pudo iniciar y ejecutar assertions: **79 pruebas correctas, una
  fallida y 160 no ejecutadas**, sobre 240 casos. La ejecución se detuvo por
  `--max-failures=1`; no significa que las otras 160 hayan fallado o pasado.
- Falló `customer-next-order.spec.ts`, caso «pedido existente muestra detalle
  y contacto, con portal adaptable a 360px», línea 218 del candidato anterior.
- El script terminó antes de `scp` y SSH. No se aplicaron migraciones, no se
  reinició la API y no se instaló C1 en el VPS.

La evidencia aportada es `Texto pegado(2).txt`; su hash y los resultados
estructurados se conservan en [c1-native-validation.json](c1-native-validation.json).
El intento local previo con Chromium bloqueado por EPERM permanece como
antecedente de otro entorno; no sustituye la ejecución WSL posterior.

## Corrección de la expectativa

La prueba buscaba «Si necesitas cambiarlo, comunícate con PRODIMT». El portal
vigente muestra «Ya recibimos tu captura para esta fecha. Si necesitas cambiarla,
comunícate con PRODIMT». Este texto ya estaba en I23; C1 no modificó ese aviso.

La segunda revisión ajusta exclusivamente la expectativa al texto vigente y
la acota al panel `customer-current-order`. Conserva la comprobación de
visibilidad del contacto, los 14 kg, la ausencia del formulario de captura,
cero envíos de pedido y la ausencia de desbordamiento a 360 px. No cambia
producto, UI, API, SQL, fecha, permisos ni comportamiento del cliente.

## Ejecución completa y diagnóstico

El runner pasa a `--max-failures=0` para recoger los resultados de toda la
suite en cada intento. No añade omisiones, tolerancia a fallos ni flakiness.
La instalación sigue exigiendo **240 correctas, cero fallidas, cero omitidas y
cero flaky**, desde el commit exacto y limpio del bundle.

Si falla, `summarize-c1-browser.mjs` muestra todos los casos inesperados o
flaky y errores globales, con archivo, línea, título y mensaje. Guarda
`diagnostico-resumen.json` separado del gate y del reporte original; dicho
diagnóstico siempre tiene `authorizesInstall: false`. No puede habilitar
el despliegue ni sustituir la evidencia requerida por el instalador.

La segunda revisión reutiliza los mismos binarios de aplicación y validación,
assets y SQL ya probados: no hay cambios en `src/`, `apps/` ni en las pruebas
C# portátiles. La identidad de los binarios respecto del candidato anterior
se comprueba al preparar el paquete. El commit nuevo identifica las pruebas,
herramientas de diagnóstico y documentación corregidas.

## Validación y pendiente

- Tipado estricto E2E: correcto después del ajuste de selector.
- Sintaxis Bash y colección: correctas; siguen siendo 240 casos en 24 archivos.
- Parser de diagnóstico: tres pruebas Node correctas, con reports anidados,
  casos fallidos/flaky, errores globales, eliminación de ANSI, límite de
  mensajes y conservación del hash del reporte original.
- Sin cambios de aplicación o SQL; siguen vigentes las 101 pruebas SQL,
  siete portátiles y el gate exacto descritos en la [preparación nativa](2026-11-04-c1-native.md).
- **Pendiente:** ejecutar el candidato corregido en WSL y obtener 240/240.
  Sólo después el wrapper puede conectar al VPS y repetir las validaciones,
  respaldo y migración 18→19.

F14 permanece parcial: navegador con API simulada, SQL y aceptación humana
se registran por separado. No se acredita instalación por corregir un test.
