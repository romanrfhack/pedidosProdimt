# Evidencia I21 — Pedidos como catálogo responsivo

Fecha: 2 de noviembre de 2026, America/Mexico_City. Datos ficticios; sin VPS/SQL real.

## Base recuperada

Repositorio `romanrfhack/pedidosProdimt`; ramas remotas consultadas confirmaron que I19 no estaba publicado. Continuidad del proyecto confirma I19 instalado por el usuario con fuente `d3a11420b2025fa0a19f84e1796d5a8b252259a3`, backend/frontend `production-d3a11420b202`, migraciones 13→16 y salud correcta. Se preserva esa unión I16/I18/I19.

Recuperado `PRODIMT_Produccion_I19_Cambios.zip`, 290550 bytes, SHA256 `6ef35f965ed03d2fdebbaf1022f9081f67afc167b35b6f6b1951f8f914f5cce9`. CRC y SHA256SUMS correctos. Bundle verificado tras recuperar prerrequisitos I16 `3fb11fa3925283885fbab2a6d4aba5f8d850ddd7` e I18 `862e9d7ca7756933bc05ff2995c6a648948ae9b0`; checkpoint exacto `77457633f5a510aca48855b5a9078adf0a66d271`.

Se respeta el rechazo automático anterior a publicar I19 por falta de autorización explícita. No se intentó publicarlo por otro transporte o API; no hay PR/CI remoto nuevo. Cambios conservados en rama local y entrega incremental.

## Verificado

- `npm ci` desde los lockfiles en frontend/E2E, sin cambiar dependencias. Build production correcto, inicial 487.60 kB. Avisos CSS existentes y nueva hoja Pedidos 4.83 kB por encima del umbral de aviso 4 kB, bajo el máximo 8 kB; no se elevan presupuestos.
- Las funciones existentes desde clearDate hasta final de AdminTodayComponent permanecen idénticas: carga, refresco, detalle, corrección, auditoría y permisos. La lógica nueva es presentación/búsqueda/contadores y botón Hoy; no cambia DTO/endpoint/backend.
-15 pruebas nuevas del paquete correctas: fuente/payload/manifiesto, guardas de base I19, preflight aislado, salud/autorización, sin cambios de backend, idempotencia y rollback/interrupción. Guardas de propietario en producción conservadas.
- Python ampliado:409 casos,405 correctos,1 omitido y3 fallos en fixtures históricos I19 que crean archivos/directorios esperando UID 0. Este host ejecuta UID 1000 y no dispone de sudo. Se conserva la guarda real; no se presenta como suite completa correcta ni se cambia código I19 para acomodar el entorno.
- Chromium de escritorio155 no pudo arrancar por socket de singleton no permitido en el entorno; esos14 errores ocurrieron antes de abrir páginas. Se preparó Chromium headless153 aislado y configuración externa al repositorio. No se cambian permisos del sistema ni dependencias del proyecto para probar.
- Primer ensayo funcional de 14 casos: 13 correctos; uno apuntaba a «Cancelar» y se ajustó al botón existente «Cancelar corrección». Suite completa posterior: 204/205 correctas. La prueba de configuración a 1366px falla con altura 782px frente al límite 769px; se repitió sobre I19 sin cambios con idéntico resultado. Es un fallo previo ajeno a esta vista; se conserva el test y no se modifica Configuración.
- Siete anchos 320/390/768/1024/1366/1920/2560px dan 1/1/2/3/4/6/8 columnas, sin desbordar; botones de 44px y acciones alineadas. Textos largos, estados desconocidos, colores/etiquetas y avisos con fecha comprobados. PC/móvil revisados visualmente con datos ficticios.
- Se mejora sólo dentro de Pedidos la distribución de producto, cantidad y nota en corrección: la nota conserva ancho útil en PC. El formulario y sus validaciones no cambian.
- Graphify no disponible; comando ausente. Sin grafo modificado.

## Cierre de la validación local

Ejecución final de las 14 E2E nuevas de Pedidos: 14/14 correctas, 15.8s, después de mejorar el ancho de notas/producto/cantidad en corrección. Incluye los siete anchos, todos los filtros/colores, consulta cada 15 s y error/recuperación, CDMX, permisos sólo lectura, enlace directo y mismo borrador con cantidad/motivo/nota al redimensionar. El intento ampliado posterior de 39 casos quedó interrumpido sin resultado global y no se cuenta como suite correcta.

La suite completa anterior conserva 204/205 correctas; el único fallo es la altura de Configuración a 1366px, reproducido exactamente en una copia limpia de I19 (782px frente a 769px). No se relaja el test ni se modifica ese módulo.

## Entrega verificada

Entrega I21 generada y verificada: `PRODIMT_Pedidos_I21.zip`, 246583 bytes, SHA256 `0ad53450486e8db91b3687453e9a23cb7df8ec0a1d509b88576ff6c2ddf8f625`. Aplicación/payload `867df56de3bb2ec3c0b7a28fe24a275d48c6a894`; payload SHA256 `2513b853ed9a9fe96db26fa579ad2623a676126092041a488f437dc389ad4c77`. Veintisiete archivos frontend/licencias de la clausura activa, CRC/hashes/manifiesto/imports y cargador exportado correctos; sin residuos, fuente, backend o SQL dentro del payload. Compilado desde árbol limpio y fijado a ese commit. Preservados I14/mensaje, I16/productos adicionales, I18/seguridad e I19/producción.

Requiere frontend y backend `production-d3a11420b202`; activa sólo frontend `orders-catalog-867df56de3bb`. Fuentes revisables en `PRODIMT_Pedidos_I21_Cambios.zip`, bundle/parche incremental; publicación GitHub pendiente por el rechazo previo de I19, sin PR/CI remoto nuevo. Instalación/aceptación I21 pendientes: preflight, aplicar, verificar salud/frontend true y Ctrl+F5 → Pedidos. No se operó VPS/SQL real.

## Pendiente remoto

Instalación y aceptación visual/funcional I21. La entrega no acredita despliegue; no se operó VPS/SQL desde este entorno.
