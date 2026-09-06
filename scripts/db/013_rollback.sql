-- =============================================================
-- MED-100 — Rollback de 013_indicaciones.sql
--
-- ⚠ BORRA CONTENIDO CLÍNICO. No hay otra copia en ninguna parte.
--
-- Lo que se pierde es el registro de qué medicamentos se le indicaron a cada
-- paciente y quién los cargó. Si en algún momento hay que responder por eso
-- —al paciente, a un médico o a una autoridad sanitaria— después de correr
-- esto ya no se puede.
--
-- Sacá una copia primero:
--
--   SELECT i.fecha_utc, c.nombre AS paciente, m.nombre AS medico,
--          x.medicamento, x.dosis, x.frecuencia, x.duracion, x.instrucciones,
--          i.notas
--     FROM indicacion i
--     JOIN cliente c ON c.id = i.cliente_id
--     LEFT JOIN medico m ON m.id = i.medico_id
--     LEFT JOIN indicacion_medicamento x ON x.indicacion_id = i.id
--    WHERE i.deleted_at IS NULL
--    ORDER BY i.fecha_utc, i.id;
-- =============================================================
SET NAMES utf8mb4;
USE med100_db;

-- El detalle primero: cuelga de indicacion con ON DELETE CASCADE, pero se
-- borra explícito para no depender de eso al desarmar.
DROP TABLE IF EXISTS indicacion_medicamento;
DROP TABLE IF EXISTS indicacion;

DELETE up FROM usuario_permiso up
  JOIN permiso p ON p.id = up.permiso_id WHERE p.codigo = 'indicaciones';
DELETE rp FROM rol_permiso rp
  JOIN permiso p ON p.id = rp.permiso_id WHERE p.codigo = 'indicaciones';
DELETE FROM permiso WHERE codigo = 'indicaciones';
