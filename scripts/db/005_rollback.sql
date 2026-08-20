-- =============================================================
-- MED-100 — Rollback de la migración 005
-- Fecha: 2026-08-14
--
-- Deshace el código de turno del médico y devuelve el catálogo de roles
-- al de antes (Admin / Supervisor / Cajero / Vendedor).
--
-- ⚠️ Lo que NO devuelve: a qué usuario le tocaba el rol Vendedor. Esa
-- información se perdió al fusionarlo en Cajero. Después de correr esto
-- hay que volver a poner en Vendedor a quien corresponda, desde Usuarios.
-- =============================================================

SET NAMES utf8mb4;
USE med100_db;

-- 1) Fuera el código de turno
SET @existe_ix := (
  SELECT COUNT(*) FROM information_schema.statistics
  WHERE table_schema = DATABASE() AND table_name = 'medico'
    AND index_name = 'uq_medico_codigo_turno');
SET @sql := IF(@existe_ix > 0,
  'ALTER TABLE medico DROP INDEX uq_medico_codigo_turno', 'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @existe := (
  SELECT COUNT(*) FROM information_schema.columns
  WHERE table_schema = DATABASE() AND table_name = 'medico' AND column_name = 'codigo_turno');
SET @sql := IF(@existe > 0,
  'ALTER TABLE medico DROP COLUMN codigo_turno', 'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- 2) Vuelve Vendedor
INSERT INTO rol (nombre, descripcion)
SELECT 'Vendedor', 'Ventas y gestión de clientes'
WHERE NOT EXISTS (SELECT 1 FROM rol WHERE nombre = 'Vendedor');

DELETE rp FROM rol_permiso rp JOIN rol r ON r.id = rp.rol_id WHERE r.nombre = 'Vendedor';
INSERT INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r
JOIN permiso p ON p.codigo IN ('vender','clientes','clientes_editar','citas','turnos')
WHERE r.nombre = 'Vendedor';

-- 3) Cajero vuelve a lo de antes (sin clientes_editar)
DELETE rp FROM rol_permiso rp JOIN rol r ON r.id = rp.rol_id WHERE r.nombre = 'Cajero';
INSERT INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r
JOIN permiso p ON p.codigo IN ('vender','clientes','comprobantes','cuadre','citas','turnos')
WHERE r.nombre = 'Cajero';
UPDATE rol SET descripcion = 'Ventas, consulta de clientes, su propio cuadre y comprobantes'
WHERE nombre = 'Cajero';

-- 4) Los usuarios de Servicio quedan sin rol antes de borrarlo
UPDATE usuario u JOIN rol r ON r.id = u.rol_id AND r.nombre = 'Servicio'
SET u.rol_id = NULL, u.updated_at = UTC_TIMESTAMP();
DELETE FROM rol WHERE nombre = 'Servicio';

-- 5) Resincronizar permisos efectivos
DELETE up FROM usuario_permiso up JOIN usuario u ON u.id = up.usuario_id
WHERE u.rol_id IS NOT NULL;
INSERT IGNORE INTO usuario_permiso (usuario_id, permiso_id)
SELECT u.id, rp.permiso_id FROM usuario u
JOIN rol_permiso rp ON rp.rol_id = u.rol_id WHERE u.rol_id IS NOT NULL;
