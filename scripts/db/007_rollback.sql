-- =============================================================
-- MED-100 — Rollback de la migración 007
-- Fecha: 2026-08-15
--
-- Quita los dos interruptores del archivado en PDF. Los PDF que ya se
-- guardaron en los expedientes NO se tocan: son documentos del paciente y
-- borrarlos sería otra cosa muy distinta a deshacer una migración.
-- =============================================================

SET NAMES utf8mb4;
USE med100_db;

SET @existe := (
  SELECT COUNT(*) FROM information_schema.columns
  WHERE table_schema = DATABASE() AND table_name = 'configuracion_negocio'
    AND column_name = 'archivar_factura_pdf');
SET @sql := IF(@existe > 0,
  'ALTER TABLE configuracion_negocio DROP COLUMN archivar_factura_pdf', 'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @existe := (
  SELECT COUNT(*) FROM information_schema.columns
  WHERE table_schema = DATABASE() AND table_name = 'configuracion_negocio'
    AND column_name = 'archivar_turno_pdf');
SET @sql := IF(@existe > 0,
  'ALTER TABLE configuracion_negocio DROP COLUMN archivar_turno_pdf', 'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
