-- =============================================================
-- MED-100 — Secuencia de comprobantes fiscales (NCF)
-- Script: 011_ncf_secuencia.sql
--
-- Pedido de Yuber (2026-09-06): "agreguemos también las opciones de NCF como en
-- el PrestControl en cobro y en configuración".
--
-- QUÉ CAMBIA Y QUÉ NO
--   factura.ncf YA EXISTE desde 001 (VARCHAR(19) NULL, UNIQUE uq_factura_ncf).
--   Lo único que faltaba era de dónde sale ese número. Hasta hoy se escribía a
--   mano en cada cobro; desde ahora la clínica puede además cargar el rango que
--   le autorizó la DGII y dejar que la app lo asigne sola.
--
-- DISEÑO
--   ncf_secuencia — prefijo (ej. B02 tradicional / E32 e-CF), próxima secuencia,
--   fin del rango autorizado y vencimiento. La reserva del siguiente número es
--   atómica (SELECT … FOR UPDATE dentro de la transacción de la factura), igual
--   que configuracion_negocio.factura_siguiente.
--
--   Una sola fila activa a la vez: la clínica es un solo negocio con un solo
--   libro de ventas. (FAControl lleva una por modo porque es una suite de tres
--   rubros; esa dimensión acá no aplica y se quitó a propósito.)
--
-- REGLA DGII: un NCF consumido NUNCA se reusa, ni aunque la factura se anule.
-- Por eso anular escribe estado='anulada' y jamás devuelve el comprobante al
-- rango.
--
-- MIGRACIÓN para bases existentes; las nuevas reciben lo mismo desde el
-- verificador al abrir. Idempotente: se puede correr dos veces.
-- =============================================================
SET NAMES utf8mb4;
USE med100_db;

CREATE TABLE IF NOT EXISTS ncf_secuencia (
  id          INT UNSIGNED NOT NULL AUTO_INCREMENT,
  -- Prefijo autorizado por la DGII: serie + tipo (ej. 'B02', 'E32')
  prefijo     VARCHAR(5)  NOT NULL,
  -- Largo de la parte numérica: 8 para NCF tradicional, 10 para e-CF
  largo       TINYINT UNSIGNED NOT NULL DEFAULT 8,
  -- Próximo número a asignar y fin del rango autorizado (inclusive)
  proxima     BIGINT UNSIGNED NOT NULL DEFAULT 1,
  fin_rango   BIGINT UNSIGNED NULL,
  -- Vencimiento de la autorización. La pantalla de Cobrar deja de ofrecer
  -- números cuando pasa, en vez de entregar comprobantes ya inválidos.
  vencimiento DATE NULL,
  activo      TINYINT(1) NOT NULL DEFAULT 1,
  created_at  DATETIME NOT NULL DEFAULT (UTC_TIMESTAMP()),
  updated_at  DATETIME NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_ncf_secuencia_prefijo (prefijo)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- NO se siembra ninguna fila. Sin autorización de la DGII cargada, la app sigue
-- comportándose como hasta ahora (NCF a mano), que es lo correcto: inventar un
-- rango B02 que la clínica no tiene autorizado produciría comprobantes falsos.
