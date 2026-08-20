-- =============================================================
-- MED-100 — Migración 005: código de turno del médico + roles nuevos
-- Fecha: 2026-08-14
--
-- Para bases que YA existen. Una instalación nueva no necesita este
-- script: 001 y 002 ya vienen con todo esto.
--
-- Es idempotente: se puede correr dos veces sin romper nada.
-- Rollback: 005_rollback.sql
-- =============================================================

SET NAMES utf8mb4;
USE med100_db;

-- -------------------------------------------------------------
-- 1) medico.codigo_turno
--
-- Prefijo de dos letras para el turno de la sala: primera letra del
-- nombre + última letra del apellido ("Yuber Santana Lizardo" -> "YO").
-- Así en la sala se ve de un vistazo a qué médico va cada número.
-- -------------------------------------------------------------
SET @existe := (
  SELECT COUNT(*) FROM information_schema.columns
  WHERE table_schema = DATABASE() AND table_name = 'medico' AND column_name = 'codigo_turno');

SET @sql := IF(@existe = 0,
  'ALTER TABLE medico ADD COLUMN codigo_turno VARCHAR(8) NULL AFTER porcentaje_honorario',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @existe_ix := (
  SELECT COUNT(*) FROM information_schema.statistics
  WHERE table_schema = DATABASE() AND table_name = 'medico'
    AND index_name = 'uq_medico_codigo_turno');

SET @sql := IF(@existe_ix = 0,
  'ALTER TABLE medico ADD UNIQUE KEY uq_medico_codigo_turno (codigo_turno)',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- Los médicos que ya estaban cargados no tienen código. Se deja en NULL
-- a propósito: la app lo calcula y lo asigna la primera vez que se edita
-- al médico, resolviendo los choques (dos "YO" no pueden convivir por el
-- índice único). Rellenarlo acá con SQL a ciegas duplicaría códigos.

-- -------------------------------------------------------------
-- 2) Roles: fusionar Vendedor en Cajero y crear Servicio
--
-- Orden importante: primero se mueven los usuarios, después se borra el
-- rol. La FK usuario.rol_id es ON DELETE SET NULL, así que borrar
-- primero dejaría a esa gente SIN rol y sin permisos.
-- -------------------------------------------------------------

-- 2.a) Servicio (si ya existe, no se duplica)
INSERT INTO rol (nombre, descripcion)
SELECT 'Servicio',
       'Atiende: sala de espera, citas, pacientes, médicos y procedimientos. No cobra.'
WHERE NOT EXISTS (SELECT 1 FROM rol WHERE nombre = 'Servicio');

DELETE rp FROM rol_permiso rp
JOIN rol r ON r.id = rp.rol_id
WHERE r.nombre = 'Servicio';

INSERT INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r
JOIN permiso p ON p.codigo IN
  ('clientes','clientes_editar','medicos','procedimientos','citas','turnos')
WHERE r.nombre = 'Servicio';

-- 2.b) Cajero absorbe lo que hacía Vendedor (edición de pacientes)
DELETE rp FROM rol_permiso rp
JOIN rol r ON r.id = rp.rol_id
WHERE r.nombre = 'Cajero';

INSERT INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r
JOIN permiso p ON p.codigo IN
  ('vender','clientes','clientes_editar','comprobantes','cuadre','citas','turnos')
WHERE r.nombre = 'Cajero';

UPDATE rol SET descripcion = 'Cobra: facturación, pacientes, su propio cuadre y sus comprobantes'
WHERE nombre = 'Cajero';

UPDATE rol SET descripcion = 'Operación completa de la clínica, sin configuración ni usuarios'
WHERE nombre = 'Supervisor';

-- 2.c) Los usuarios que eran Vendedor pasan a Cajero.
-- El trigger trg_usuario_after_update les resincroniza los permisos solo.
UPDATE usuario u
JOIN rol viejo  ON viejo.id = u.rol_id AND viejo.nombre = 'Vendedor'
JOIN rol nuevo  ON nuevo.nombre = 'Cajero'
SET u.rol_id = nuevo.id, u.updated_at = UTC_TIMESTAMP();

-- 2.d) Ahora sí, fuera el rol vacío
DELETE FROM rol WHERE nombre = 'Vendedor';

-- 2.e) Supervisor: se re-siembra por si el catálogo de permisos creció
DELETE rp FROM rol_permiso rp
JOIN rol r ON r.id = rp.rol_id
WHERE r.nombre = 'Supervisor';

INSERT INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r
JOIN permiso p ON p.codigo IN
  ('panel','vender','clientes','clientes_editar','productos','almacen','caducidad',
   'comprobantes','comprobantes_todos','cuadre','cuadre_todos','reportes','facturas_anular',
   'medicos','procedimientos','citas','turnos')
WHERE r.nombre = 'Supervisor';

-- 2.f) Admin: siempre todo
DELETE rp FROM rol_permiso rp
JOIN rol r ON r.id = rp.rol_id
WHERE r.nombre = 'Admin';

INSERT INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r CROSS JOIN permiso p
WHERE r.nombre = 'Admin';

-- 2.g) Resincronizar los permisos efectivos de todo el mundo.
-- Los triggers solo disparan cuando CAMBIA el rol del usuario; acá lo que
-- cambió fue el contenido del rol, así que hay que reconstruirlo a mano.
-- Se respetan los overrides: primero se borra lo que venía del rol y se
-- vuelve a poner, sin tocar permisos otorgados a mano que el rol no da...
-- salvo que no hay forma de distinguirlos, así que se reconstruye entero
-- y los overrides individuales hay que volver a darlos desde Usuarios.
DELETE up FROM usuario_permiso up
JOIN usuario u ON u.id = up.usuario_id
WHERE u.rol_id IS NOT NULL;

INSERT IGNORE INTO usuario_permiso (usuario_id, permiso_id)
SELECT u.id, rp.permiso_id
FROM usuario u
JOIN rol_permiso rp ON rp.rol_id = u.rol_id
WHERE u.rol_id IS NOT NULL;

SELECT r.nombre AS rol, COUNT(rp.permiso_id) AS permisos
FROM rol r LEFT JOIN rol_permiso rp ON rp.rol_id = r.id
GROUP BY r.id, r.nombre ORDER BY r.id;
