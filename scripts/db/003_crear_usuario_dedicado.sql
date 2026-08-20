-- =============================================================
-- MED-100 — Usuario MySQL dedicado para la instalación final
-- Script: 003_crear_usuario_dedicado.sql
-- Ejecutar como root UNA sola vez en la PC del cliente.
--
-- ⚠ IMPORTANTE: cambia 'CAMBIAR-ESTA-CLAVE' por una contraseña real y usa
--   esa misma contraseña en MED100.App.dll.config.
--   La aplicación NUNCA debe correr como root en producción.
-- =============================================================

SET NAMES utf8mb4;

CREATE USER IF NOT EXISTS 'med100'@'localhost'
  IDENTIFIED BY 'CAMBIAR-ESTA-CLAVE';

-- Permisos solo sobre la base de datos de la aplicación (nada más).
-- Incluye DELETE porque la app lo necesita para reasignar permisos de usuario
-- (usuario_permiso); las ventas y clientes usan soft delete, nunca se borran.
GRANT SELECT, INSERT, UPDATE, DELETE ON med100_db.* TO 'med100'@'localhost';

-- Necesarios para respaldar desde Configuración (mysqldump)
GRANT LOCK TABLES, SHOW VIEW, TRIGGER ON med100_db.* TO 'med100'@'localhost';

FLUSH PRIVILEGES;

-- Verificación rápida:
-- SHOW GRANTS FOR 'med100'@'localhost';
--
-- NOTA: este usuario NO puede crear la base de datos. Si la BD todavía no
-- existe, ábrela una primera vez con root (o ejecuta 001 + 002 a mano) para
-- que MED-100 la genere, y recién entonces cambia el App.config al usuario
-- dedicado. Restaurar respaldos también requiere privilegios de estructura.
