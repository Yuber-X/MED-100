-- =============================================================
-- MED-100 — Rollback de 012_fiados.sql
--
-- ⚠ LEER ANTES DE CORRER: esto BORRA los pagos parciales registrados.
--
-- Si hay facturas fiadas con abonos, al volver atrás se pierde el rastro de
-- quién pagó cuánto y cuándo, y las facturas quedan como si nunca se hubiera
-- cobrado nada de ellas. Eso NO se recupera de ninguna otra tabla.
--
-- Sacá primero una copia de lo que se va a perder:
--
--   SELECT f.numero_factura, c.nombre, f.paciente_paga, f.abonado_inicial,
--          f.fecha_compromiso, a.fecha_utc, a.monto, a.metodo_pago
--     FROM factura f
--     LEFT JOIN cliente c ON c.id = f.cliente_id
--     LEFT JOIN factura_abono a ON a.factura_id = f.id
--    WHERE f.abonado_inicial < f.paciente_paga
--    ORDER BY f.id, a.fecha_utc;
--
-- Las facturas en sí NO se tocan: total, paciente_paga y NCF quedan intactos.
-- =============================================================
SET NAMES utf8mb4;
USE med100_db;

DROP TABLE IF EXISTS factura_abono;

ALTER TABLE factura DROP INDEX ix_factura_compromiso;
ALTER TABLE factura DROP COLUMN fecha_compromiso;
ALTER TABLE factura DROP COLUMN abonado_inicial;

-- El permiso se retira de todos lados (usuario_permiso primero por la FK).
DELETE up FROM usuario_permiso up
  JOIN permiso p ON p.id = up.permiso_id WHERE p.codigo = 'fiados';
DELETE rp FROM rol_permiso rp
  JOIN permiso p ON p.id = rp.permiso_id WHERE p.codigo = 'fiados';
DELETE FROM permiso WHERE codigo = 'fiados';
