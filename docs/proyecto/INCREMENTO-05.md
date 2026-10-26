# I05 — Cambiar contraseña propia

Autorizado por el usuario después de recuperar acceso; el alta ejecutada confirma login y consulta administrativa HTTP 200.

- Administración autenticada cambia sólo la cuenta identificada por su JWT. El cuerpo no selecciona otro usuario.
- Requiere contraseña actual, nueva de 16–128 caracteres y confirmación. Rechaza contraseña actual errónea, reutilización, confirmación distinta o caracteres de control.
- Guarda hash y auditoría sin secretos de manera atómica; compara el hash observado para evitar que dos cambios concurrentes se sobreescriban. No requiere nuevas tablas, columnas ni migraciones.
- Cada JWT administrativo incorpora un sello HMAC del hash. Se compara con la cuenta activa en cada solicitud; cambiar contraseña o desactivar invalida sesiones anteriores. Tokens administrativos de I04 sin sello requieren login nuevo al instalar. El token de cliente no cambia.
- Pantalla `/admin/cambiar-contrasena` enlazada en navegación administrativa. Tras éxito borra campos/sesión y dirige al login con mensaje; solicitudes antiguas no cierran una sesión nueva.
- Validar autorización, contraseña errónea, confirmación, auditoría, login antiguo/nuevo, sesiones anteriores, concurrencia y rollback real de SQL.

El paquete nativo preparado preserva configuración y esquema existentes; CI final y hashes constan en `../validation/2026-10-26-password-change.md`. La instalación en el VPS sigue pendiente. Instalar el código no cambia automáticamente contraseñas: el usuario ejecuta la operación desde su cuenta. No incluye recuperación pública, cambio de otros usuarios, MFA ni roles nuevos.
