-- =============================================================
-- MED-100 — Rollback de la migración 008 (licencia)
--
-- ⚠️ Borra la tabla licencia. Lo que eso significa en la práctica:
--    · una instalación ACTIVADA vuelve a estar sin activar y la app pedirá
--      el código otra vez (basta con volver a escribirlo);
--    · el demo NO se reinicia: la fecha de instalación sigue anclada en
--      %ProgramData%\MED-100\licencia.dat. Para reiniciarlo de verdad hay que
--      borrar también ese archivo, y eso es cosa del desarrollador, no del
--      cliente.
--
-- No hay pérdida de datos del negocio: acá no vive nada de la clínica.
-- =============================================================

SET NAMES utf8mb4;
USE med100_db;

DROP TABLE IF EXISTS licencia;

SHOW TABLES LIKE 'licencia';
