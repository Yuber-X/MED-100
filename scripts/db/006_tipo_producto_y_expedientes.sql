-- =============================================================
-- MED-100 — Migración 006: tipo de producto + expediente del paciente
-- Fecha: 2026-08-14
--
-- Para bases que YA existen. Una instalación nueva no la necesita:
-- 001 ya viene con las dos cosas.
--
-- Es idempotente. Rollback: 006_rollback.sql
-- =============================================================

SET NAMES utf8mb4;
USE med100_db;

-- -------------------------------------------------------------
-- 1) producto.tipo
--
-- En una clínica el almacén no es solo "insumos": hay medicamentos que se
-- dispensan, material que se esteriliza, equipos y artículos de limpieza.
-- Todo lo que ya está cargado queda como 'insumo', que es lo que era.
-- -------------------------------------------------------------
SET @existe := (
  SELECT COUNT(*) FROM information_schema.columns
  WHERE table_schema = DATABASE() AND table_name = 'producto' AND column_name = 'tipo');

SET @sql := IF(@existe = 0,
  "ALTER TABLE producto
     ADD COLUMN tipo ENUM('insumo','medicamento','material','equipo','limpieza','oficina','otro')
     NOT NULL DEFAULT 'insumo' AFTER cantidad",
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @existe_ix := (
  SELECT COUNT(*) FROM information_schema.statistics
  WHERE table_schema = DATABASE() AND table_name = 'producto' AND index_name = 'ix_producto_tipo');

SET @sql := IF(@existe_ix = 0,
  'ALTER TABLE producto ADD KEY ix_producto_tipo (tipo)', 'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- -------------------------------------------------------------
-- 2) documento_paciente: el expediente digital
--
-- Pedido de Yuber (2026-08-14): un almacén de documentos como el de
-- FAControl, para guardar todo lo que el paciente entrega y tenerlo a mano
-- cuando vuelva.
--
-- DISEÑO (el mismo que probó FAControl en 018):
--  * El ARCHIVO NO va en la base. Un BLOB por cada foto de cédula hincha el
--    dump y hace lentísimo el respaldo. Va al disco, en
--    <carpeta de la app>\expedientes\pacientes\<cliente_id>\, y acá se guarda
--    su ficha.
--  * `ruta_relativa` es relativa a esa carpeta raíz: mover la instalación de
--    PC no rompe el expediente.
--  * Soft delete: quitar un papel del expediente de un paciente es un acto
--    administrativo, y además es exclusivo del Admin.
--
-- ⚠️ LEY 172-13: acá entran datos de salud, que son SENSIBLES. Por eso cada
-- alta, descarga y borrado queda en `auditoria` con nombre y hora. MED-100
-- sigue sin tener campos de diagnóstico ni tratamiento: esto es un archivador
-- de papeles escaneados, no un expediente clínico estructurado.
-- -------------------------------------------------------------
CREATE TABLE IF NOT EXISTS documento_paciente (
  id             BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  cliente_id     BIGINT UNSIGNED NOT NULL,
  -- Nombre tal como lo entregó el usuario (es lo que se muestra y lo que sale en el ZIP)
  nombre         VARCHAR(255)  NOT NULL,
  -- Ruta dentro de la carpeta de expedientes: 'pacientes/<cliente_id>/<id>_<nombre>'
  ruta_relativa  VARCHAR(400)  NOT NULL,
  extension      VARCHAR(15)   NOT NULL,
  tamano_bytes   BIGINT UNSIGNED NOT NULL DEFAULT 0,
  tipo           ENUM('otro','identificacion','seguro','consentimiento',
                      'referimiento','estudio','factura') NOT NULL DEFAULT 'otro',
  notas          VARCHAR(300)  NULL,
  created_at     DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  created_by     BIGINT UNSIGNED NULL,
  deleted_at     DATETIME      NULL,
  PRIMARY KEY (id),
  KEY ix_docpaciente_cliente (cliente_id),
  KEY ix_docpaciente_tipo (cliente_id, tipo),
  -- RESTRICT y no CASCADE: un paciente con papeles no se borra de la base sin
  -- que alguien decida antes qué hacer con ellos.
  CONSTRAINT fk_docpaciente_cliente FOREIGN KEY (cliente_id)
    REFERENCES cliente (id) ON DELETE RESTRICT,
  CONSTRAINT fk_docpaciente_usuario FOREIGN KEY (created_by)
    REFERENCES usuario (id) ON DELETE SET NULL
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- 3) El permiso del módulo
-- -------------------------------------------------------------
INSERT INTO permiso (codigo, nombre, descripcion)
SELECT 'expedientes', 'Expedientes de pacientes',
       'Ver y subir los documentos del paciente. Eliminarlos es solo del Admin.'
WHERE NOT EXISTS (SELECT 1 FROM permiso WHERE codigo = 'expedientes');

-- Admin, Supervisor y Servicio: los tres atienden al paciente en el mostrador.
-- El Cajero NO: cobra, y los papeles del paciente no son parte de cobrar.
INSERT IGNORE INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r
JOIN permiso p ON p.codigo = 'expedientes'
WHERE r.nombre IN ('Admin', 'Supervisor', 'Servicio');

-- Resincronizar los permisos efectivos de esos roles
INSERT IGNORE INTO usuario_permiso (usuario_id, permiso_id)
SELECT u.id, rp.permiso_id
FROM usuario u
JOIN rol_permiso rp ON rp.rol_id = u.rol_id
JOIN permiso p ON p.id = rp.permiso_id AND p.codigo = 'expedientes'
WHERE u.rol_id IS NOT NULL;

SELECT 'producto.tipo' AS cambio,
       (SELECT COUNT(*) FROM information_schema.columns
        WHERE table_schema = DATABASE() AND table_name = 'producto' AND column_name = 'tipo') AS ok
UNION ALL
SELECT 'documento_paciente',
       (SELECT COUNT(*) FROM information_schema.tables
        WHERE table_schema = DATABASE() AND table_name = 'documento_paciente')
UNION ALL
SELECT 'permiso expedientes',
       (SELECT COUNT(*) FROM permiso WHERE codigo = 'expedientes');
