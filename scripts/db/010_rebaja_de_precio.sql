-- =============================================================
-- 010 — Rebaja de precio al cobrar
--
-- Pedido de la clínica (2026-08-27):
--   "En la parte de cobro, luego que pongo un procedimiento, debe permitir
--    eliminar dicho procedimiento y hacer una modificación al precio o una
--    rebajas"
--
-- Dos cosas:
--
--  1) detalle.precio_catalogo — el precio de LISTA al momento de facturar.
--     `precio_unitario` sigue siendo lo que se COBRÓ. Sin guardar el de lista,
--     un procedimiento rebajado de 5,000 a 4,000 es indistinguible de uno que
--     siempre valió 4,000, y la línea "Descuento" del ticket no se podría
--     reconstruir al reimprimir. Se congela por la misma razón que
--     `exento_itbis`: cambiar el tarifario mañana no puede reescribir una
--     factura vieja.
--
--     NULL = se cobró el precio de lista (que es el caso normal).
--
--  2) permiso `precio_editar` — va aparte de `vender` a propósito: cobrar y
--     decidir cuánto se cobra son dos responsabilidades distintas. Se otorga a
--     Admin y Supervisor. Si la clínica quiere que el Cajero también pueda,
--     se marca desde Admin de Usuarios sin tocar SQL.
--
-- Idempotente: se puede correr dos veces sin romper nada.
-- =============================================================

-- ---- 1) La columna ----
SET @existe := (
  SELECT COUNT(*) FROM information_schema.columns
   WHERE table_schema = DATABASE()
     AND table_name   = 'detalle'
     AND column_name  = 'precio_catalogo'
);

SET @sql := IF(@existe = 0,
  'ALTER TABLE detalle ADD COLUMN precio_catalogo DECIMAL(15,2) NULL AFTER precio_unitario;',
  'SELECT "010: la columna precio_catalogo ya existe" AS aviso;');

PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- Las facturas anteriores se cobraron al precio de lista: dejarlas en NULL es
-- exactamente eso, "sin rebaja". No se rellena nada hacia atrás.

-- ---- 2) El permiso ----
INSERT INTO permiso (codigo, nombre, descripcion)
SELECT 'precio_editar', 'Rebajar precios al cobrar',
       'Cambiar el precio de una línea en la pantalla de cobro'
WHERE NOT EXISTS (SELECT 1 FROM permiso WHERE codigo = 'precio_editar');

INSERT INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id
  FROM rol r
  JOIN permiso p ON p.codigo = 'precio_editar'
 WHERE r.nombre IN ('Admin', 'Supervisor')
   AND NOT EXISTS (
     SELECT 1 FROM rol_permiso rp
      WHERE rp.rol_id = r.id AND rp.permiso_id = p.id);

-- Los usuarios YA creados tienen sus permisos copiados en usuario_permiso por
-- el trigger del alta: agregar el permiso al rol no los alcanza. Se les da a
-- los que pertenecen a un rol que lo tiene.
-- No se filtra por `activo`: un usuario dado de baja no puede entrar de todos
-- modos, y dejarlo fuera haria que le faltara el permiso el dia que lo
-- reactiven.
INSERT INTO usuario_permiso (usuario_id, permiso_id)
SELECT u.id, p.id
  FROM usuario u
  JOIN rol_permiso rp ON rp.rol_id = u.rol_id
  JOIN permiso p ON p.id = rp.permiso_id AND p.codigo = 'precio_editar'
 WHERE NOT EXISTS (
     SELECT 1 FROM usuario_permiso up
      WHERE up.usuario_id = u.id AND up.permiso_id = p.id);
