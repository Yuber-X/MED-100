-- =============================================================
-- MED-100 — Migración 008: licencia (demo de 15 días / modo completo)
-- Fecha: 2026-08-18
--
-- Pedido de Yuber: que la app pida un código y, según eso, corra en demo de
-- 15 días o en modo completo.
--
-- La tabla guarda TRES cosas y ninguna es el código:
--  · instalada_at_utc    → desde cuándo se cuentan los 15 días;
--  · activada            → si ya se escribió la llave del producto;
--  · ultima_apertura_utc → el arranque anterior, para notar el reloj atrasado.
--
-- El código NO se guarda ni acá ni en ningún lado: la app solo compara su
-- SHA-256 contra el que lleva adentro. Guardarlo sería dejar la llave escrita
-- en la base de datos del cliente, al alcance de cualquier respaldo.
--
-- La fecha de instalación tiene una SEGUNDA copia fuera de la base, en
-- %ProgramData%\MED-100\licencia.dat, y la app se queda con la más vieja de
-- las dos. Borrar la base no reinicia el demo.
--
-- Idempotente: se puede correr las veces que haga falta.
-- Rollback: 008_rollback.sql
-- =============================================================

SET NAMES utf8mb4;
USE med100_db;

CREATE TABLE IF NOT EXISTS licencia (
  id                  TINYINT UNSIGNED NOT NULL,
  instalada_at_utc    DATETIME     NOT NULL,
  activada            TINYINT(1)   NOT NULL DEFAULT 0,
  activada_at_utc     DATETIME     NULL,
  activada_por        VARCHAR(80)  NULL,
  ultima_apertura_utc DATETIME     NOT NULL,
  PRIMARY KEY (id),
  CONSTRAINT ck_licencia_fila_unica CHECK (id = 1)
) ENGINE=InnoDB;

-- La fila arranca AHORA. En una instalación que ya venía trabajando eso
-- significa 15 días desde hoy, no desde que se instaló MED-100: es a favor del
-- cliente y es lo correcto, porque hasta hoy no había nada que aceptar.
INSERT IGNORE INTO licencia (id, instalada_at_utc, activada, ultima_apertura_utc)
VALUES (1, UTC_TIMESTAMP(), 0, UTC_TIMESTAMP());

SELECT instalada_at_utc, activada, ultima_apertura_utc FROM licencia;
