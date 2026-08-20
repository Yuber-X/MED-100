-- =============================================================
-- MED-100 — Rollback de la migración 006
-- Fecha: 2026-08-14
--
-- ⚠️ BORRA LOS EXPEDIENTES de la base. Los ARCHIVOS siguen en el disco
-- (<carpeta de la app>\expedientes\pacientes\), pero sin la tabla la app ya no
-- sabe de quién es cada uno. Antes de correr esto, respaldá esa carpeta.
-- =============================================================

SET NAMES utf8mb4;
USE med100_db;

-- 1) Fuera el expediente
DROP TABLE IF EXISTS documento_paciente;

DELETE up FROM usuario_permiso up
JOIN permiso p ON p.id = up.permiso_id WHERE p.codigo = 'expedientes';
DELETE rp FROM rol_permiso rp
JOIN permiso p ON p.id = rp.permiso_id WHERE p.codigo = 'expedientes';
DELETE FROM permiso WHERE codigo = 'expedientes';

-- 2) Fuera el tipo de producto
SET @existe_ix := (
  SELECT COUNT(*) FROM information_schema.statistics
  WHERE table_schema = DATABASE() AND table_name = 'producto' AND index_name = 'ix_producto_tipo');
SET @sql := IF(@existe_ix > 0, 'ALTER TABLE producto DROP INDEX ix_producto_tipo', 'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @existe := (
  SELECT COUNT(*) FROM information_schema.columns
  WHERE table_schema = DATABASE() AND table_name = 'producto' AND column_name = 'tipo');
SET @sql := IF(@existe > 0, 'ALTER TABLE producto DROP COLUMN tipo', 'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
