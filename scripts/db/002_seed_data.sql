-- =============================================================
-- MED-100 — Seed de roles y permisos base
-- Script: 002_seed_data.sql (requerido para operar — NO es data de prueba)
-- Ejecutar después de 001_create_schema.sql.
-- NOTA: no crea ningún usuario. El primer arranque de la app muestra
-- el wizard "Crear cuenta inicial" que crea el Admin con BCrypt
-- (patrón PrestControl) — jamás contraseñas en texto plano aquí.
-- =============================================================

-- Los scripts están en UTF-8. Sin esto, el cliente mysql.exe asume la
-- codificación de la consola y guarda 'Almacén' como basura (mojibake).
SET NAMES utf8mb4;

USE med100_db;

-- Roles.
--
-- Revisión de Yuber (2026-08-14): 'Vendedor' y 'Cajero' hacían lo mismo
-- —en una clínica no hay piso de venta, hay un mostrador— así que quedó
-- uno solo. Y entró 'Servicio', que es quien de verdad mueve la clínica:
-- la recepcionista que da turnos, agenda citas y registra pacientes pero
-- NO toca la caja. Separar quién cobra de quién atiende es lo que hace
-- que el cuadre del día signifique algo.
INSERT INTO rol (nombre, descripcion) VALUES
  ('Admin',      'Acceso total, único rol con Configuración y gestión de usuarios'),
  ('Supervisor', 'Operación completa de la clínica, sin configuración ni usuarios'),
  ('Cajero',     'Cobra: facturación, pacientes, su propio cuadre y sus comprobantes'),
  ('Servicio',   'Atiende: sala de espera, citas, pacientes, médicos y procedimientos. No cobra.');

-- Permisos por módulo/acción
INSERT INTO permiso (codigo, nombre, descripcion) VALUES
  ('panel',              'Dashboard',                 'KPIs de ventas del día/mes'),
  ('vender',             'Vender',                    'Pantalla de facturación'),
  ('clientes',           'Clientes (ver)',            'Consulta de clientes'),
  ('clientes_editar',    'Clientes (crear/editar)',   'Alta y edición de clientes'),
  ('productos',          'Productos',                 'CRUD de productos y caducidad'),
  ('almacen',            'Almacén',                   'Vista de stock con totales'),
  ('caducidad',          'Caducidad',                 'Semáforo de productos por caducar'),
  ('comprobantes',       'Buscar comprobante',        'Búsqueda y reimpresión de sus propias facturas'),
  ('comprobantes_todos', 'Comprobantes de todos',     'Ver facturas de todos los usuarios'),
  ('cuadre',             'Cuadre de caja',            'Cuadre del propio turno'),
  ('cuadre_todos',       'Cuadre de todos',           'Cuadres de todos los cajeros'),
  ('reportes',           'Reportes por fecha',        'Reportes por rango de fechas'),
  ('facturas_anular',    'Anular facturas',           'Permiso especial: anulación con auditoría'),
  ('usuarios',           'Admin de usuarios',         'CRUD de usuarios, roles y overrides'),
  ('configuracion',      'Configuración',             'EXCLUSIVO Admin (regla 2026-07-11)'),
  -- ---- Clínica (MED-100) ----
  ('medicos',            'Médicos y horarios',        'CRUD de médicos, su honorario y sus horarios de atención'),
  ('procedimientos',     'Procedimientos',            'Tarifario de procedimientos'),
  ('citas',              'Citas',                     'Agenda de citas y recordatorios'),
  ('turnos',             'Turnos de sala',            'Dar y llamar turnos de la sala de espera'),
  ('expedientes',        'Expedientes de pacientes',  'Ver y subir los documentos del paciente. Eliminarlos es solo del Admin.');

-- Admin: todos los permisos
INSERT INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r CROSS JOIN permiso p
WHERE r.nombre = 'Admin';

-- Supervisor: todo el piso de venta, sin configuracion/usuarios
INSERT INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r
JOIN permiso p ON p.codigo IN
  ('panel','vender','clientes','clientes_editar','productos','almacen','caducidad',
   'comprobantes','comprobantes_todos','cuadre','cuadre_todos','reportes','facturas_anular',
   'medicos','procedimientos','citas','turnos','expedientes')
WHERE r.nombre = 'Supervisor';

-- Cajero: cobra y atiende el mostrador. Hereda el 'clientes_editar' que
-- tenía el viejo rol Vendedor: quien cobra también registra al paciente
-- que llega sin ficha, y obligarlo a llamar a otro para eso era absurdo.
INSERT INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r
JOIN permiso p ON p.codigo IN
  ('vender','clientes','clientes_editar','comprobantes','cuadre','citas','turnos')
WHERE r.nombre = 'Cajero';

-- Servicio: todo lo asistencial, NADA de plata. Sin 'vender', sin
-- 'cuadre' y sin 'comprobantes' a propósito — es el rol de la
-- recepcionista que agenda y da turnos.
INSERT INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r
JOIN permiso p ON p.codigo IN
  ('clientes','clientes_editar','medicos','procedimientos','citas','turnos','expedientes')
WHERE r.nombre = 'Servicio';

-- =============================================================
-- Procedencias base ("registro de proveniento")
--
-- Solo las genéricas, que son las mismas en cualquier clínica. Los
-- médicos que refieren y las ARS los va agregando la recepción sobre
-- la marcha desde el formulario del paciente: sembrarlos acá con
-- nombres inventados obligaría a borrarlos después.
--
-- Ojo: el paciente que llega por su cuenta va con referidor_id NULL.
-- No hay una fila "Vino solo" a propósito — sería ruido en el reporte.
-- =============================================================
INSERT INTO referidor (nombre, tipo, notas) VALUES
  ('Publicidad',        'publicidad', 'Volantes, radio, prensa'),
  ('Redes sociales',    'redes',      'Instagram, Facebook, WhatsApp'),
  ('Otro paciente',     'paciente',   'Vino recomendado por alguien que ya se atiende acá');

-- =============================================================
-- ARS más comunes en República Dominicana.
--
-- Se siembran porque son las mismas en cualquier clínica del país y
-- tecleárselas una por una el primer día es trabajo perdido. Las que
-- la clínica no use se desactivan desde Configuración; no se borran,
-- porque una ARS con facturas tiene que seguir teniendo nombre.
--
-- El módulo completo se apaga con configuracion_negocio.ars_activo.
-- =============================================================
INSERT INTO ars (nombre) VALUES
  ('SeNaSa'),
  ('ARS Humano'),
  ('ARS Palic Salud'),
  ('ARS Universal'),
  ('ARS Futuro'),
  ('ARS Renacer'),
  ('ARS Reservas'),
  ('ARS Simag'),
  ('ARS Meta Salud'),
  ('ARS APS');
