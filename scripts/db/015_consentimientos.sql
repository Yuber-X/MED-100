-- =============================================================
-- MED-100 — Consentimiento informado por procedimiento
-- Script: 015_consentimientos.sql
--
-- Pedido de la clínica (2026-09-21): "AGREGAR UN BOTÓN DE CONSENTIMIENTO
-- INFORMADO QUE LISTE LOS DOCUMENTOS DE CONSENTIMIENTO POR PROCEDIMIENTO,
-- FIRMADO ANTES DE CADA PROCEDIMIENTO".
-- Yuber decidió el flujo el 2026-09-24: IMPRIMIR Y ESCANEAR. El paciente firma
-- en papel y el papel firmado se guarda en su expediente (documento_paciente),
-- que ya existe. Acá no se guarda ninguna firma digital.
--
-- QUÉ ES ESTA TABLA Y QUÉ NO ES
--   Es el CATÁLOGO de textos: "para una extracción se firma este papel". La
--   clínica los escribe y los edita desde la pantalla de Procedimientos.
--   NO es el registro de consentimientos firmados: el firmado es el papel
--   escaneado en el expediente del paciente. Que una plantilla exista no
--   significa que alguien la firmó.
--
--   Cada impresión sí queda en `auditoria` (quién la imprimió, para qué
--   paciente y cuándo): es lo que permite decir después "este papel salió del
--   sistema el día tal".
--
-- ⚠ EL TEXTO LEGAL NO LO PONE EL PROGRAMADOR. La plantilla de ejemplo que
--   siembra este script es un PUNTO DE PARTIDA redactado en términos generales.
--   El contenido de un consentimiento informado —riesgos, alternativas,
--   complicaciones de CADA procedimiento— lo define la clínica y conviene que
--   lo revise su abogado. Ver CLAUDE.md §1.1: la Ley 172-13 exige consentimiento
--   expreso y por escrito para tratar datos de salud, y el sistema no lo suple.
--
-- PERMISOS (decisión 2026-09-25, sin permiso nuevo)
--   * EDITAR las plantillas exige `procedimientos`: son parte del catálogo del
--     tarifario, y quien define un procedimiento define su consentimiento.
--   * IMPRIMIR uno no exige permiso propio: lo imprime quien atiende al
--     paciente en el mostrador, y es el papel que el paciente se lleva a firmar.
--     Una fila más en `permiso` por esto solo agregaría una llamada al Admin el
--     primer día.
--
-- MIGRACIÓN idempotente.
-- =============================================================
SET NAMES utf8mb4;
USE med100_db;

CREATE TABLE IF NOT EXISTS consentimiento (
  id               BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  -- NULL = general: sirve para cualquier procedimiento. Es el caso de la
  -- clínica que tiene UN papel para todo y no uno por tratamiento.
  procedimiento_id BIGINT UNSIGNED NULL,
  titulo           VARCHAR(150)  NOT NULL,
  -- TEXT y no VARCHAR: un consentimiento serio pasa largo de los 65 mil
  -- caracteres de holgura que da TEXT, pero nunca de los 500 de un VARCHAR.
  cuerpo           TEXT          NOT NULL,
  activo           TINYINT(1)    NOT NULL DEFAULT 1,
  created_at       DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  updated_at       DATETIME      NULL,
  deleted_at       DATETIME      NULL,
  PRIMARY KEY (id),
  KEY ix_consentimiento_procedimiento (procedimiento_id),
  CONSTRAINT fk_consentimiento_procedimiento FOREIGN KEY (procedimiento_id)
    REFERENCES procedimiento (id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- -------------------------------------------------------------
-- Plantilla de ejemplo (general). Se siembra UNA sola vez: si la clínica la
-- borra o la reescribe, este script no se la vuelve a poner encima.
-- -------------------------------------------------------------
INSERT INTO consentimiento (procedimiento_id, titulo, cuerpo)
SELECT NULL, 'Consentimiento informado general',
'Por este documento hago constar que el personal de la clínica me explicó, en un lenguaje que entiendo, en qué consiste el procedimiento que se me va a realizar, para qué se hace y qué se espera de él.

Se me informó que todo procedimiento tiene riesgos y posibles complicaciones, que pueden incluir molestias, inflamación, sangrado, infección o reacciones a los medicamentos, y que ningún resultado puede garantizarse por completo.

Se me explicaron las alternativas disponibles, incluida la de no realizarme el procedimiento, y las consecuencias de cada una.

Tuve la oportunidad de hacer todas las preguntas que quise y me fueron respondidas. Entiendo que puedo retirar este consentimiento en cualquier momento antes del procedimiento.

Autorizo al personal de la clínica a realizar el procedimiento descrito, así como los cuidados y tratamientos que resulten necesarios durante el mismo.

Autorizo además el tratamiento de mis datos personales y de salud para los fines de mi atención, conforme a la Ley 172-13 de Protección de Datos Personales.'
WHERE NOT EXISTS (
  SELECT 1 FROM consentimiento
   WHERE titulo = 'Consentimiento informado general' AND deleted_at IS NULL
);
