-- =============================================================
-- 009 — Última visita anterior al sistema
--
-- Pedido de la clínica (2026-08-27):
--   "Se le puede poner la fecha retroactiva de consulta de manera que el
--    sistema arroje a partir de que se cargue todas las data de pacientes
--    atendiendo anteriormente todos los que tienen 6 meses sin venir"
--
-- El aviso de pacientes inactivos deduce la última visita de la actividad
-- REGISTRADA (última cita atendida o última factura emitida). Un paciente que
-- se acaba de cargar al pasar los archivos viejos no tiene ninguna de las dos,
-- así que figura como "nunca vino" y queda fuera del aviso — que es justo el
-- que la clínica necesita llamar. Esta columna guarda la fecha que la
-- recepción sabe y el sistema no puede deducir.
--
-- Es un PISO: si el paciente tiene actividad real posterior, gana la real.
--
-- Idempotente: se puede correr dos veces sin romper nada.
-- =============================================================

SET @existe := (
  SELECT COUNT(*) FROM information_schema.columns
   WHERE table_schema = DATABASE()
     AND table_name   = 'cliente'
     AND column_name  = 'ultima_visita_previa'
);

SET @sql := IF(@existe = 0,
  'ALTER TABLE cliente ADD COLUMN ultima_visita_previa DATE NULL AFTER referidor_id;',
  'SELECT "009: la columna ultima_visita_previa ya existe" AS aviso;');

PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
