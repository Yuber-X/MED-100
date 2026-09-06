-- =============================================================
-- MED-100 — Fiados: facturas que quedan con saldo pendiente
-- Script: 012_fiados.sql
--
-- Pedido de Yuber (2026-09-06): "agreguemos ambas opciones y que el usuario
-- tenga opción de elegir (también colocar el tiempo estimado a cuando tenga el
-- cliente que pagar, o sea, parecido a los préstamos a punto de caducar, pero
-- orientado a los fiados a clientes)".
--
-- EL DISEÑO, Y POR QUÉ
--
--   factura.abonado_inicial — lo que el paciente pagó EN EL MOSTRADOR el día que
--   se emitió la factura. Es INMUTABLE, igual que el resto de la factura: nunca
--   se toca después. Ese es el punto. Si acumulara los pagos posteriores, el
--   cuadre de caja de aquel día cambiaría solo, semanas más tarde, y un cierre
--   ya firmado dejaría de cuadrar.
--
--   factura_abono — cada pago POSTERIOR. Lleva su propia fecha, su método de
--   pago y quién lo recibió, porque entra a la caja del día en que se cobra, no
--   del día de la factura.
--
--   factura.fecha_compromiso — cuándo dijo el paciente que iba a pagar. De acá
--   sale el semáforo (CalculadoraFiado) y el aviso automático, igual que la
--   fecha de caducidad de un insumo alimenta el suyo.
--
--   El SALDO no se guarda: es paciente_paga − abonado_inicial − SUM(abonos).
--   Un saldo persistido es un número que se puede desincronizar de sus propios
--   pagos, y cuando eso pasa nadie sabe cuál de los dos miente.
--
-- ANULAR: los abonos NO se borran al anular una factura. Quedan como registro
-- de que ese dinero entró y después se devolvió; el cuadre los excluye por el
-- estado de la factura, no borrándolos.
--
-- MIGRACIÓN idempotente. Las facturas que ya existen se dan por pagadas
-- completas (abonado_inicial = paciente_paga): hasta hoy no se podía fiar, así
-- que es exactamente lo que pasó.
-- =============================================================
SET NAMES utf8mb4;
USE med100_db;

-- -------------------------------------------------------------
-- 1. factura.abonado_inicial
-- -------------------------------------------------------------
SET @existe := (SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='factura' AND COLUMN_NAME='abonado_inicial');
SET @sql := IF(@existe=0,
  "ALTER TABLE factura ADD COLUMN abonado_inicial DECIMAL(15,2) NOT NULL DEFAULT 0.00 AFTER paciente_paga",
  'SELECT "factura.abonado_inicial ya existe"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- Backfill: todo lo emitido antes de esta versión se cobró completo.
-- Solo corre la primera vez (después ya no quedan filas en 0 por este motivo).
SET @sql := IF(@existe=0,
  "UPDATE factura SET abonado_inicial = paciente_paga",
  'SELECT "backfill de abonado_inicial ya hecho"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- -------------------------------------------------------------
-- 2. factura.fecha_compromiso
-- -------------------------------------------------------------
SET @existe := (SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='factura' AND COLUMN_NAME='fecha_compromiso');
SET @sql := IF(@existe=0,
  "ALTER TABLE factura ADD COLUMN fecha_compromiso DATE NULL AFTER abonado_inicial",
  'SELECT "factura.fecha_compromiso ya existe"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- Índice para la pantalla de fiados y el aviso automático: los dos preguntan
-- "qué hay pendiente ordenado por fecha de compromiso".
SET @existe := (SELECT COUNT(*) FROM information_schema.STATISTICS
  WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='factura' AND INDEX_NAME='ix_factura_compromiso');
SET @sql := IF(@existe=0,
  'ALTER TABLE factura ADD INDEX ix_factura_compromiso (fecha_compromiso, estado)',
  'SELECT "ix_factura_compromiso ya existe"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- -------------------------------------------------------------
-- 3. factura_abono: cada pago posterior a la emisión
-- -------------------------------------------------------------
CREATE TABLE IF NOT EXISTS factura_abono (
  id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  factura_id  BIGINT UNSIGNED NOT NULL,
  -- Quién lo recibió: el abono entra en SU cuadre, no en el del que facturó.
  usuario_id  BIGINT UNSIGNED NOT NULL,
  fecha_utc   DATETIME       NOT NULL,
  monto       DECIMAL(15,2)  NOT NULL,
  metodo_pago ENUM('efectivo','tarjeta','transferencia','mixto') NOT NULL,
  notas       VARCHAR(250)   NULL,
  created_at  DATETIME       NOT NULL DEFAULT (UTC_TIMESTAMP()),
  PRIMARY KEY (id),
  KEY ix_abono_factura (factura_id),
  -- El cuadre pregunta "qué abonos entraron tal día, por quién": este índice es
  -- exactamente esa consulta.
  KEY ix_abono_fecha (fecha_utc, usuario_id),
  CONSTRAINT fk_abono_factura FOREIGN KEY (factura_id)
    REFERENCES factura (id) ON DELETE RESTRICT,
  CONSTRAINT fk_abono_usuario FOREIGN KEY (usuario_id)
    REFERENCES usuario (id) ON DELETE RESTRICT,
  -- Un abono de 0 o negativo no es un pago. Devolver plata es anular la
  -- factura, no cargar un abono al revés.
  CONSTRAINT ck_abono_positivo CHECK (monto > 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- -------------------------------------------------------------
-- 4. Permiso para cobrar deudas
-- -------------------------------------------------------------
-- Va aparte de `vender`: fiar y cobrar un fiado son decisiones de crédito, y en
-- una clínica chica no todo el que cobra en el mostrador decide a quién se le
-- fía. Admin y Supervisor lo tienen; el Cajero solo si el Admin se lo da.
INSERT INTO permiso (codigo, nombre, descripcion)
SELECT 'fiados', 'Fiar y cobrar deudas',
       'Dejar una factura con saldo pendiente y registrar abonos posteriores'
WHERE NOT EXISTS (SELECT 1 FROM permiso WHERE codigo = 'fiados');

INSERT INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r
  JOIN permiso p ON p.codigo = 'fiados'
 WHERE r.nombre IN ('Admin', 'Supervisor')
   AND NOT EXISTS (SELECT 1 FROM rol_permiso rp
                    WHERE rp.rol_id = r.id AND rp.permiso_id = p.id);

-- Los usuarios ya creados tienen sus permisos COPIADOS en usuario_permiso por
-- el trigger del alta, así que darlo solo al rol no los alcanza.
INSERT INTO usuario_permiso (usuario_id, permiso_id)
SELECT u.id, p.id FROM usuario u
  JOIN rol_permiso rp ON rp.rol_id = u.rol_id
  JOIN permiso p ON p.id = rp.permiso_id AND p.codigo = 'fiados'
 WHERE NOT EXISTS (SELECT 1 FROM usuario_permiso up
                    WHERE up.usuario_id = u.id AND up.permiso_id = p.id);
