# I09 — Acceso de clientes y próximo pedido

## Código y comportamiento

Código `6cfdc7ff366a2caa7dac399b9842b3630a30328a`, [PR #2](https://github.com/romanrfhack/pedidosProdimt/pull/2), rama `codex/i09-customer-order-links`, base I08 actualizada `8977c5f`. Revisión independiente de autorización, privacidad, recepción/corte, reintentos y empaquetado completada.

- Enlaces nuevos `/cliente#token=...`, compatibilidad `?token=...`; token retirado de la URL antes de autenticar, incluso ante error. La identidad anterior se limpia al abrir un enlace nuevo.
- Descripción precargada editable, teléfono obligatorio guardado mediante PATCH específico, vigencia obligatoria para recordatorios, copiar enlace/mensaje y WhatsApp individual. Abrir WhatsApp no registra enviado/entregado. Teléfonos reales no forman parte de esta evidencia.
- Fecha automática antes de las 18:00 = mismo día; desde las 18:00 = siguiente día, zona America/Mexico_City. Un instante de recepción del servidor determina fecha, SubmittedAt y revisión tardía. Esperar SQL no altera ese instante. Cambio respecto de fecha observada devuelve 409 y conserva borrador para confirmar de nuevo.
- Rápida reutiliza parser I07, requiere perfil aprobado y conserva desglose/huella. Manual usa sólo productos configurados y unidades observadas confirmadas. Modalidades preservan cantidades y notas; cero no genera un pedido sugerido.
- Pedido vigente se muestra sin crear otro ni sobrescribirlo. NoOrder explícito para fecha visible. Recibos transaccionales y referencias persistentes evitan duplicados ante respuesta perdida; hash de recibos I07 heredados conservado.
- Tokens continúan hasheados; valor completo sólo al crear. JWT limita vigencia y se vincula al token origen, revalidado cada solicitud. Sesiones cliente anteriores requieren abrir enlace o ingresar token nuevamente; no se revocan los tokens existentes.
- No migraciones, cambios históricos, cambio de contraseñas ni envío automático. Fecha explícita e importaciones conservan contratos. Se conserva corrección I08 de descansos en Pendientes.

## Verificación local

- 300 pruebas Application y 5 Domain correctas (305 total), incluidas HTTP/SQLite de sesión revocada/expirada, aislamiento, rápida/manual, rollback, perfil/unidad, duplicado, replay tras corte y reloj que cruza las 18:00 durante SQL.
- 94 E2E correctas. Primera corrida detectó una expectativa con el título antiguo del portal; se actualizó la expectativa y la corrida completa final pasó. Casos nuevos cubren teléfono antes de crear, error sin token parcial, zonaCDMX en dispositivoUTC, WhatsApp sin envío real, fecha fresca, ambas modalidades, respuesta perdida, corte 409 y 360 px.
- 217 Python correctas, 11 nuevas del paquete/instalador. Hashes/rutas, permisos, idempotencia, ausencia de mutaciones SQL, extracción atómica y reversión de ambos enlaces verificados.
- Publicación linux-x64/framework-dependent y Angular Production correctas. AdvertenciaCSS de configuración heredada de 7.58 kB supera aviso de 4 kB y queda bajo error de 8 kB; presupuestos/dependencias intactos.
- [CI 41218846462](https://github.com/romanrfhack/pedidosProdimt/actions/runs/41218846462) con cuatro trabajos correctos: 305 .NET, 94 E2E, 217 Python, 33 SQL Server y smoke Production. Los dos nuevos casos SQL verifican recibos entre hosts y captura concurrente sin duplicados.

## Paquete verificado

- PRODIMT_Acceso_Clientes_I09.zip: 5,860,114 bytes; SHA256 `a6c4bd85be72f5a904064e200e60feb6e177ff6947b7f1a9ef4405026136ba25`.
- Payload SHA256 `90c0e6505212f5165df56107a70d19085bb920ebdbf63c9bfc083fb7e0723d5c`; 67 archivos.
- El instalador incluido contiene los pines del código y payload; SHA256SUMS y load_payload verificaron el ZIP final. Incluye avisos de terceros del build; sin PDB, mapas, secretos, datos operativos o paquetes de importación.
- Previos admitidos: backend `capture-46fed604732e`; frontend igual, `client-config-42f6eb4c225a` o `client-config-be3995fdce69`. Ambos builds I08 son ancestros del código I09. Once migraciones y canales Customer/AdminManualCapture deben existir.
- Sin --apply sólo comprobaciones de lectura. Aplicación explícita crea snapshot privado de configuración/enlaces y reinicia sólo PRODIMT. Ante fallo revierte aplicación; no restaura SQL. Repetición verifica already_current sin reinicio.
- Se compiló/empaquetó fuera del VPS. No se ejecutó SSH, despliegue, modificación SQL, registro de pedido real o envío WhatsApp desde esta sesión. Instalación y aceptación de negocio pendientes.

Después de extraer y verificar SHA256SUMS:

```bash
scp PRODIMT_Instalar_I09.py prodimt-customer-access-payload.zip root@194.238.26.70:/root/
ssh root@194.238.26.70 'python3 /root/PRODIMT_Instalar_I09.py --package /root/prodimt-customer-access-payload.zip --apply'
```

Resolver cualquier captura incierta antes de reiniciar. Tras éxito entrar en Configuración → Tokens de acceso, guardar teléfono y crear un enlace para probar el flujo con un pedido autorizado. No reinstalar I07/I08 ni repetir importaciones para aplicar I09.

## Intento remoto y diagnóstico de preflight

El usuario ejecutó el paquete original y reportó `failed/read-only-preflight`, issue `database_permissions_schema_or_channels_unexpected`; cambios de aplicación, escrituras/migraciones SQL, contraseñas y reinicio false. El guard rechazó los valores de una consulta interpretada, pero el instalador original no conservaba esas diferencias. No se ha demostrado si falta un permiso, canal o condición del historial; no conceder permisos ni ejecutar bootstrap por este reporte.

Diagnóstico independiente de sólo lectura: `PRODIMT_Diagnostico_Acceso_I09.py`, revisión `20261031-customer-access-diagnosis1`, 9,528 bytes, SHA256 `0b1c9cf84cdf58dd36e34ac5650ec8a784eef2433e0097923a6387da3872fd0e`. Verifica instalador original SHA256 `d9623be63603d0448b532828f0c2063e0da703a27ea84d303f072507f1bc68c2`, payload original `90c0e6505212f5165df56107a70d19085bb920ebdbf63c9bfc083fb7e0723d5c` y código 6cfdc7f. Selecciona sólo preflight, allowlist exacta de procesos y SQL, conserva DEVNULL, no crea lock ni acepta apply. Captura metadata antes de la guarda: booleans, contadores y campos faltantes/inválidos/diferentes; sin nombres ajenos, credenciales ni streams crudos.

225 pruebas Python correctas. Carga del instalador/payload originales y regresión completa de lectura verificadas; casos de éxito, canal ausente, permiso0 y NULL omitido. El template install2 añade reporte sanitizado previo al rechazo, sin relajar la igualdad original; no reemplaza el ZIP entregado ni se aplica al VPS. Código diagnóstico `64e3680874b1eff54a70b1253a6819fac72092cf`. La salida del VPS sigue pendiente para decidir la corrección mínima.

La consulta original completa fue verificada con ODBC real en [CI 41220905526](https://github.com/romanrfhack/pedidosProdimt/actions/runs/41220905526): identifica ambos canales ausentes, acepta esquema de once migraciones con canales válidos y distingue UPDATE denegado en Customers. Son pruebas de infraestructura efímera; no identifican por sí mismas la diferencia del VPS.
