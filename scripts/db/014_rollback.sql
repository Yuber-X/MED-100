-- =============================================================
-- Odonto Unión (MED-100) — Rollback de 014_categoria_procedimiento.sql
--
-- Devuelve la tabla `procedimiento` a como estaba: sin categoría.
--
-- Lo único que se pierde es en qué categoría había quedado cada procedimiento
-- (texto libre, escrito a mano por la clínica). Si querés conservarlo:
--
--   SELECT id, codigo, nombre, categoria FROM procedimiento
--    WHERE categoria IS NOT NULL AND deleted_at IS NULL;
--
-- Idempotente: si la columna no está, no hace nada.
-- =============================================================
SET NAMES utf8mb4;
USE med100_db;

SET @tiene := (SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME = 'procedimiento'
    AND COLUMN_NAME = 'categoria');

SET @sql := IF(@tiene = 1,
  "ALTER TABLE procedimiento DROP COLUMN categoria",
  'SELECT "procedimiento.categoria no existe"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
