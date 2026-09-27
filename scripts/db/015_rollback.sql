-- =============================================================
-- Odonto Unión (MED-100) — Rollback de 015_consentimientos.sql
--
-- Borra el CATÁLOGO de textos de consentimiento informado.
--
-- ⚠ Lo que se pierde son las PLANTILLAS que escribió la clínica, que es
-- trabajo de redacción, no datos generados por el sistema. Antes de correr
-- esto, guardá una copia:
--
--   SELECT id, procedimiento_id, titulo, cuerpo FROM consentimiento
--    WHERE deleted_at IS NULL;
--
-- Los consentimientos ya FIRMADOS no se tocan: viven escaneados en el
-- expediente del paciente (documento_paciente), que esta tabla ni conoce.
--
-- Idempotente: si la tabla no está, no hace nada.
-- =============================================================
SET NAMES utf8mb4;
USE med100_db;

DROP TABLE IF EXISTS consentimiento;
