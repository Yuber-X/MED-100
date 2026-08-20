-- =============================================================
-- MED-100 — Migración 007: copia en PDF de los documentos
-- Fecha: 2026-08-15
--
-- Pedido de Yuber: que al emitir una factura (o cualquier documento) quede
-- guardada como PDF en el expediente del paciente.
--
-- Son dos interruptores y no uno porque los dos papeles no se parecen:
--  · la FACTURA es un comprobante fiscal que el paciente puede reclamar meses
--    después → se archiva por defecto;
--  · el TURNO es un papelito que se tira al salir del consultorio. Archivarlo
--    llenaría el expediente de un paciente frecuente con cuarenta PDF al año
--    → apagado por defecto, pero disponible para quien lo quiera.
--
-- Van en configuracion_negocio y no en ajustes.json porque es una decisión del
-- NEGOCIO ("acá guardamos copia de todo"), no una preferencia de cada PC.
--
-- Idempotente. Rollback: 007_rollback.sql
-- =============================================================

SET NAMES utf8mb4;
USE med100_db;

SET @existe := (
  SELECT COUNT(*) FROM information_schema.columns
  WHERE table_schema = DATABASE() AND table_name = 'configuracion_negocio'
    AND column_name = 'archivar_factura_pdf');

SET @sql := IF(@existe = 0,
  'ALTER TABLE configuracion_negocio
     ADD COLUMN archivar_factura_pdf TINYINT(1) NOT NULL DEFAULT 1',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @existe := (
  SELECT COUNT(*) FROM information_schema.columns
  WHERE table_schema = DATABASE() AND table_name = 'configuracion_negocio'
    AND column_name = 'archivar_turno_pdf');

SET @sql := IF(@existe = 0,
  'ALTER TABLE configuracion_negocio
     ADD COLUMN archivar_turno_pdf TINYINT(1) NOT NULL DEFAULT 0',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SELECT archivar_factura_pdf, archivar_turno_pdf FROM configuracion_negocio;
