# I22 — Instalación confirmada y aceptación pendiente

Fecha de reporte: 3 de noviembre de 2026, Ciudad de México.

## Aplicación v4 completada

El usuario compartió la ejecución del instalador de SHA 9b65851d9eea95b8af4f83d01650e37d446be528a813f450cd52c5a7a3e438fe. Lectura planned/completed, historia 16 y salud previas correctas. Después, --apply terminó updated/completed con:

- portable_validation passed 3/3, cleanupVerified=true.
- exact_sql_validation passed; exactSql, strictMetadata, applicationPermissions, rollbackVerified, replayVerified y cleanupVerified=true.
- isolated_validation_verified=true y validation_databases_cleaned=true.
- Snapshot privado y respaldo SQL verificado antes del DDL.
- migrations_after=17, migrated_schema_verified=true, existing_data_counts_preserved=true.
- price_history_append_only_verified, tokens_preserved, security_permissions_verified, production_permissions_verified y delivery_permissions_verified=true.
- encryption_key_effective_verified=true; contraseña y clave sin cambio/rotación y tokens sin revocación.
- service_restarted=true, health_verified=true, frontend_verified=true y recovery_pending=false.
- Fuente de aplicación 0cfe3ef511d80e4af032a3e64abd97391e86df08; ambos releases delivery-0cfe3ef511d8.

La revisión4 validó el SQL con una fixture legacy antes de tocar producción. La creación del rol canónico verifica nombre Repartidor - Camioneta (sistema), dos permisos propios y ausencia de asignación automática. Los fingerprints verifican la conservación del rol personalizado y sus usuarios/grants. El resultado operativo posterior confirma la instalación; no es sólo una compilación ni ejecución de pruebas.

## Repetición saludable

El usuario volvió a ejecutar el mismo comando. Tanto lectura como --apply terminaron already_current/completed, con las 17 identidades exactas incluyendo 20261103020000_AddDeliveryOperations. changes_made, database_migrations_run y service_restarted=false; salud, frontend, clave efectiva y tokens verificados. La repetición no sembró, migró o reinició otra vez.

Esta evidencia procede de los JSON del usuario. No se abrió una sesión SSH independiente desde el agente. Una consulta pública HTTPS adicional a https://pedidos.prodimt.site/health/db devolvió status ok/database reachable.

## Configuración y aceptación de interfaz

1. Actualizar con Ctrl+F5 e ingresar con administración.
2. Seguridad→Usuarios→Editar al chofer. En Roles del usuario seleccionar Repartidor - Camioneta (sistema) y Guardar usuario. Los roles/directos existentes siguen siendo aditivos; revisar explícitamente permisos previos si se requiere restringirlo a sus recorridos.
3. Reparto→Nueva ruta: usar el código ya asociado a los pedidos/clientes, nombre y Repartidor habitual; Guardar ruta.
4. Elegir Fecha de reparto correspondiente al pedido. En Pendientes de asignar→Ubicación, guardar enlace Maps y referencia confirmados antes de asignar.
5. Nueva vuelta→Crear vuelta. Por partida, Asignar a vuelta, escoger destino y cantidad, Confirmar asignación.
6. Iniciar como chofer, Mi reparto, misma fecha, Consultar reparto. Comprobar sólo sus vueltas y Maps. En operación real registrar conteo, carga, entrega y observaciones; revisar hora/responsable en Bitácora.

Referencia aritmética: pedido50, asignado/contado/cargado25 y entrega20 dejan30 pendientes del pedido,25 disponibles para otra vuelta y 5 bajo custodia. Es un ejemplo, no una entrega ejecutada desde este agente. No cerrar una vuelta mientras conserve custodia o reservas sin resolver.

No se han configurado usuarios/rutas/clientes ni registrado entregas por el agente. La instalación saludable no equivale a aceptación visual o del piloto. No se afirma ejecución Playwright ni entrega física del producto. Las pruebas locales anteriores no se repiten por este cambio documental.

## Continuidad

Aplicación/payload e instalador v4 permanecen sin cambios. Commit de herramientas validado e1130a5ed31ec51573e20639df1e7f93137e960d; la continuación de documentación se guarda en el mismo bundle autosuficiente, sin publicar GitHub ni solicitar otra autorización para el despliegue ya realizado.
