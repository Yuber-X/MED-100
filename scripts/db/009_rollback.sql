-- =============================================================
-- 009 — ROLLBACK
--
-- OJO: borra las fechas que la recepción haya cargado a mano. No hay de dónde
-- volver a sacarlas: son un dato que solo existe en la memoria de la clínica.
-- Sacar copia antes.
-- =============================================================

SET @existe := (
  SELECT COUNT(*) FROM information_schema.columns
   WHERE table_schema = DATABASE()
     AND table_name   = 'cliente'
     AND column_name  = 'ultima_visita_previa'
);

SET @sql := IF(@existe = 1,
  'ALTER TABLE cliente DROP COLUMN ultima_visita_previa;',
  'SELECT "009 rollback: la columna no existe" AS aviso;');

PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
