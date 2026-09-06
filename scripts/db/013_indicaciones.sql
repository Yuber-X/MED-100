-- =============================================================
-- MED-100 — Medicamentos indicados por el médico
-- Script: 013_indicaciones.sql
--
-- Pedido de Yuber (2026-09-06): "lo que se quiere es que cuando el médico le
-- indique los medicamentos también puedan ser colocados acá para mejor
-- organización e historial de los procesos durante el día trabajado".
--
-- ⚠ ESTO MUEVE EL LÍMITE DEL ALCANCE (CLAUDE.md §1.1). Léase antes de tocarlo.
--
--   Un medicamento indicado ES un dato de salud, y la Ley 172-13 lo clasifica
--   como SENSIBLE: exige consentimiento expreso y por escrito del paciente y
--   obligación de secreto profesional. Hasta hoy la app solo guardaba lo
--   administrativo y los papeles escaneados (§1.1, cambio del 2026-08-14).
--   Esta tabla es la primera vez que MED-100 guarda contenido clínico
--   ESTRUCTURADO, escrito por la clínica.
--
--   Se decidió con Yuber el 2026-09-06 y queda acotado así:
--
--   * Es un REGISTRO DE LO QUE SE INDICÓ, no un expediente clínico. Sigue sin
--     haber un solo campo de diagnóstico, evolución, antecedente ni resultado.
--     Se anota QUÉ se mandó a tomar, no POR QUÉ.
--   * Permiso propio (`indicaciones`) y AUDITORÍA de toda alta y baja: la ley
--     exige poder decir quién escribió y quién miró qué.
--   * Soft delete. Un renglón mal escrito se corrige dejando rastro, porque
--     "lo que se le indicó al paciente" es exactamente el tipo de dato que
--     alguien puede querer discutir después.
--
--   ⚠ LO QUE FALTA Y NO ES TÉCNICO: el consentimiento firmado del paciente.
--   El sistema no lo suple. Confirmar con la clínica antes de entregar.
--
-- DISEÑO
--   indicacion             — la visita: paciente, médico, fecha, quién la cargó.
--   indicacion_medicamento — un renglón por medicamento, con su dosis.
--
--   Dos tablas y no una porque una indicación normal trae varios medicamentos,
--   y meterlos en un campo de texto haría imposible lo único que se pidió:
--   revisar el día ordenado.
--
-- MIGRACIÓN idempotente.
-- =============================================================
SET NAMES utf8mb4;
USE med100_db;

CREATE TABLE IF NOT EXISTS indicacion (
  id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  cliente_id  BIGINT UNSIGNED NOT NULL,     -- el paciente
  medico_id   BIGINT UNSIGNED NULL,         -- quién lo indicó
  -- La cita de la que salió, si salió de una. NULL cuando el paciente llegó
  -- sin cita, que en una clínica pasa todos los días.
  cita_id     BIGINT UNSIGNED NULL,
  fecha_utc   DATETIME       NOT NULL,
  -- Quién lo ESCRIBIÓ en el sistema. No es lo mismo que medico_id: lo carga
  -- recepción con lo que el médico dictó, y ante una discusión hay que poder
  -- decir quién tecleó.
  usuario_id  BIGINT UNSIGNED NOT NULL,
  notas       VARCHAR(500)   NULL,
  created_at  DATETIME       NOT NULL DEFAULT (UTC_TIMESTAMP()),
  updated_at  DATETIME       NULL,
  deleted_at  DATETIME       NULL,
  PRIMARY KEY (id),
  -- La consulta principal es "qué se indicó hoy": este índice es esa pregunta.
  KEY ix_indicacion_fecha (fecha_utc),
  KEY ix_indicacion_cliente (cliente_id, fecha_utc),
  KEY ix_indicacion_medico (medico_id, fecha_utc),
  CONSTRAINT fk_indicacion_cliente FOREIGN KEY (cliente_id)
    REFERENCES cliente (id) ON DELETE RESTRICT,
  CONSTRAINT fk_indicacion_medico FOREIGN KEY (medico_id)
    REFERENCES medico (id) ON DELETE RESTRICT,
  CONSTRAINT fk_indicacion_cita FOREIGN KEY (cita_id)
    REFERENCES cita (id) ON DELETE RESTRICT,
  CONSTRAINT fk_indicacion_usuario FOREIGN KEY (usuario_id)
    REFERENCES usuario (id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS indicacion_medicamento (
  id            BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  indicacion_id BIGINT UNSIGNED NOT NULL,
  -- El nombre se ESCRIBE, no se elige de `producto`: el médico manda a comprar
  -- en la farmacia de la calle, y atarlo al inventario de insumos obligaría a
  -- dar de alta como producto algo que la clínica no vende.
  medicamento   VARCHAR(150)   NOT NULL,
  dosis         VARCHAR(80)    NULL,   -- "500 mg", "1 tableta"
  frecuencia    VARCHAR(80)    NULL,   -- "cada 8 horas"
  duracion      VARCHAR(80)    NULL,   -- "por 7 días"
  instrucciones VARCHAR(250)   NULL,   -- "con comida", "no manejar"
  PRIMARY KEY (id),
  KEY ix_indicacion_medicamento (indicacion_id),
  CONSTRAINT fk_medicamento_indicacion FOREIGN KEY (indicacion_id)
    REFERENCES indicacion (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- -------------------------------------------------------------
-- Permiso
-- -------------------------------------------------------------
-- Admin, Supervisor y Servicio: la recepcionista que atiende al paciente es
-- quien lo carga. El Cajero NO lo lleva por defecto — cobra, no atiende — pero
-- el Admin puede dárselo desde Usuarios si en esa clínica es la misma persona.
INSERT INTO permiso (codigo, nombre, descripcion)
SELECT 'indicaciones', 'Medicamentos indicados',
       'Registrar y consultar los medicamentos que el médico indicó al paciente'
WHERE NOT EXISTS (SELECT 1 FROM permiso WHERE codigo = 'indicaciones');

INSERT INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r
  JOIN permiso p ON p.codigo = 'indicaciones'
 WHERE r.nombre IN ('Admin', 'Supervisor', 'Servicio')
   AND NOT EXISTS (SELECT 1 FROM rol_permiso rp
                    WHERE rp.rol_id = r.id AND rp.permiso_id = p.id);

INSERT INTO usuario_permiso (usuario_id, permiso_id)
SELECT u.id, p.id FROM usuario u
  JOIN rol_permiso rp ON rp.rol_id = u.rol_id
  JOIN permiso p ON p.id = rp.permiso_id AND p.codigo = 'indicaciones'
 WHERE NOT EXISTS (SELECT 1 FROM usuario_permiso up
                    WHERE up.usuario_id = u.id AND up.permiso_id = p.id);
