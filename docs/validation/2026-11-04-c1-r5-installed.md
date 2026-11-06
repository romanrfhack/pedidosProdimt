# C1 R5 instalado: evidencia posterior del usuario

El usuario ejecutó la instalación desde su terminal WSL después de la captura
anterior de Codex. El 4 de noviembre comunicó `updated/completed`, `mode=apply`,
fuente `fdb81ea2f92011a74f2089642b5d3a9e46b26351`, ambos releases
`consolidation-c1-fdb81ea2f920`, 19 migraciones, última
`20261104041928_ExpandAuditMetadata`.

Se cotejó el log local `resultados-c1/20261104T211143Z-62458/servidor-despliegue.log`
del paquete R5 con el resultado comunicado. Contiene primero un preflight
`planned/read-only` y después el resultado **`updated/apply/completed`**:
`health_verified`, `frontend_verified`, `migrated_schema_verified`,
`audit_permissions_preserved`, `tokens_preserved`, `env_preserved`,
`existing_delivery_preserved`, respaldo verificado y `recovery_pending=false`.
No se realizó otra conexión SSH ni SQL productiva desde C2.

El reporte acredita navegador **240/240 con API mock**, portátil **7/7 EF/HTTP
SQL Server**, SQL exacto 18→19, rollback/replay, Unicode, preservación y limpieza.
AuditLogs conserva SELECT/INSERT/UPDATE/DELETE **1/1/1/1**. No es append-only;
F24 permanece separado. Aceptación humana completa: **pendiente**.

Los siete casos de `ValidationRunner --validate-c1` son:

1. `concurrent_reservations`: reservas simultáneas de reparto entre hosts.
2. `own_scope_durable_replay`: autorización propia y replay durable de entrega.
3. `revoked_management_replay`: permiso retirado impide replay fuera de alcance.
4. `large_profile_audit_9`: perfil de nueve productos y rollback por auditoría.
5. `large_profile_audit_20`: perfil de veinte productos y rollback por auditoría.
6. `unicode_audit_roundtrip`: conservación Unicode en metadata expandida.
7. `audit_migration_preservation`: preservación de filas y downgrade con pérdida rechazado.

Estos casos no prueban funcionalmente Horarios. C2 amplía esa cobertura sobre
la base actual 19; no reinstala I22/I23/C1 ni cambia sus pines históricos.
