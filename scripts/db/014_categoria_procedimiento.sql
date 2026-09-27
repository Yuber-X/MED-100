-- =============================================================
-- Odonto Unión (MED-100) — Categoría del procedimiento
-- Script: 014_categoria_procedimiento.sql
--
-- Pedido de la clínica (propuesta del 2026-09-21):
--   "PROCEDIMIENTO: AGREGAR UNA COLUMNA QUE SE LLAME CATEGORIA.
--    CODIGO, CATEGORIA, PROCEDIMIENTO, PRECIO"
--
-- Texto libre y no una tabla de categorías a propósito: la clínica todavía no
-- tiene una lista cerrada ("endodoncia", "ortodoncia", "limpieza"...), y una
-- tabla aparte obligaría a mantener un catálogo antes de poder escribir el
-- primer procedimiento. El buscador la usa igual y la pantalla ofrece las que
-- ya se escribieron, así que en la práctica se comporta como un catálogo que
-- se arma solo. Si algún día piden ordenarlas o renombrarlas en masa, ahí sí
-- toca la tabla.
--
-- Idempotente: la aplica el migrador al arrancar y puede repetirse.
-- =============================================================
SET NAMES utf8mb4;
USE med100_db;

SET @tiene := (SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME = 'procedimiento'
    AND COLUMN_NAME = 'categoria');

SET @sql := IF(@tiene = 0,
  "ALTER TABLE procedimiento
     ADD COLUMN categoria VARCHAR(100) NULL AFTER codigo,
     ADD KEY ix_procedimiento_categoria (categoria)",
  'SELECT "procedimiento.categoria ya existe"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE
FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE()
  AND TABLE_NAME = 'procedimiento'
  AND COLUMN_NAME = 'categoria';
