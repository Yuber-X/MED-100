-- =============================================================
-- MED-100 — Rollback de 011_ncf_secuencia.sql
--
-- OJO ANTES DE CORRER ESTO: borra el rango autorizado por la DGII y por dónde
-- iba la numeración. Las facturas NO se tocan —factura.ncf existe desde 001 y
-- los comprobantes ya emitidos quedan intactos—, pero al volver a configurar la
-- secuencia hay que saber en qué número quedó, y eso solo se recupera mirando
-- el último NCF emitido:
--
--   SELECT ncf FROM factura WHERE ncf IS NOT NULL ORDER BY id DESC LIMIT 1;
--
-- Anotá ese valor ANTES de correr el DROP.
-- =============================================================
SET NAMES utf8mb4;
USE med100_db;

DROP TABLE IF EXISTS ncf_secuencia;
