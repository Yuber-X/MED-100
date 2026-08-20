-- =============================================================
-- MED-100 — Reparación de acentos en el catálogo (roles y permisos)
-- Script: 004_reparar_acentos.sql
--
-- Solo hace falta en bases creadas ANTES del 2026-07-12 con el cliente
-- mysql.exe sin `--default-character-set=utf8mb4`: los nombres con tilde
-- ("Almacén", "Configuración") quedaron guardados como basura.
-- Reescribe los textos del catálogo con los valores correctos.
-- Es idempotente: puede correrse las veces que haga falta.
-- =============================================================

SET NAMES utf8mb4;

USE med100_db;

UPDATE rol SET descripcion = 'Acceso total, único rol con Configuración y gestión de usuarios'
  WHERE nombre = 'Admin';
UPDATE rol SET descripcion = 'Operación completa del piso de venta, sin configuración ni usuarios'
  WHERE nombre = 'Supervisor';
UPDATE rol SET descripcion = 'Ventas, consulta de clientes, su propio cuadre y comprobantes'
  WHERE nombre = 'Cajero';
UPDATE rol SET descripcion = 'Ventas y gestión de clientes'
  WHERE nombre = 'Vendedor';

UPDATE permiso SET nombre = 'Dashboard',               descripcion = 'KPIs de ventas del día/mes'                      WHERE codigo = 'panel';
UPDATE permiso SET nombre = 'Vender',                  descripcion = 'Pantalla de facturación'                         WHERE codigo = 'vender';
UPDATE permiso SET nombre = 'Clientes (ver)',          descripcion = 'Consulta de clientes'                            WHERE codigo = 'clientes';
UPDATE permiso SET nombre = 'Clientes (crear/editar)', descripcion = 'Alta y edición de clientes'                      WHERE codigo = 'clientes_editar';
UPDATE permiso SET nombre = 'Productos',               descripcion = 'CRUD de productos y caducidad'                   WHERE codigo = 'productos';
UPDATE permiso SET nombre = 'Almacén',                 descripcion = 'Vista de stock con totales'                      WHERE codigo = 'almacen';
UPDATE permiso SET nombre = 'Caducidad',               descripcion = 'Semáforo de productos por caducar'               WHERE codigo = 'caducidad';
UPDATE permiso SET nombre = 'Buscar comprobante',      descripcion = 'Búsqueda y reimpresión de sus propias facturas'  WHERE codigo = 'comprobantes';
UPDATE permiso SET nombre = 'Comprobantes de todos',   descripcion = 'Ver facturas de todos los usuarios'              WHERE codigo = 'comprobantes_todos';
UPDATE permiso SET nombre = 'Cuadre de caja',          descripcion = 'Cuadre del propio turno'                         WHERE codigo = 'cuadre';
UPDATE permiso SET nombre = 'Cuadre de todos',         descripcion = 'Cuadres de todos los cajeros'                    WHERE codigo = 'cuadre_todos';
UPDATE permiso SET nombre = 'Reportes por fecha',      descripcion = 'Reportes por rango de fechas'                    WHERE codigo = 'reportes';
UPDATE permiso SET nombre = 'Anular facturas',         descripcion = 'Permiso especial: anulación con auditoría'       WHERE codigo = 'facturas_anular';
UPDATE permiso SET nombre = 'Admin de usuarios',       descripcion = 'CRUD de usuarios, roles y overrides'             WHERE codigo = 'usuarios';
UPDATE permiso SET nombre = 'Configuración',           descripcion = 'EXCLUSIVO Admin (regla 2026-07-11)'              WHERE codigo = 'configuracion';

-- Verificación:
-- SELECT codigo, nombre FROM permiso WHERE codigo IN ('almacen','configuracion');
