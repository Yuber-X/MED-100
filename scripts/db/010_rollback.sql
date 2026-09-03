-- =============================================================
-- 010 — ROLLBACK
--
-- OJO: borrar precio_catalogo pierde la constancia de qué facturas salieron
-- rebajadas. Las facturas siguen cuadrando (precio_unitario es lo que se
-- cobró), pero deja de poder decirse cuánto se rebajó. Sacar copia antes.
-- =============================================================

DELETE up FROM usuario_permiso up
  JOIN permiso p ON p.id = up.permiso_id
 WHERE p.codigo = 'precio_editar';

DELETE rp FROM rol_permiso rp
  JOIN permiso p ON p.id = rp.permiso_id
 WHERE p.codigo = 'precio_editar';

DELETE FROM permiso WHERE codigo = 'precio_editar';

SET @existe := (
  SELECT COUNT(*) FROM information_schema.columns
   WHERE table_schema = DATABASE()
     AND table_name   = 'detalle'
     AND column_name  = 'precio_catalogo'
);

SET @sql := IF(@existe = 1,
  'ALTER TABLE detalle DROP COLUMN precio_catalogo;',
  'SELECT "010 rollback: la columna no existe" AS aviso;');

PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
