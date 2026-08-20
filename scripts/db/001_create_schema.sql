-- =============================================================
-- MED-100 — Esquema inicial
-- Script: 001_create_schema.sql
-- Motor: MySQL 8.0+ · InnoDB · utf8mb4_unicode_ci
-- Origen: BDPOS-400.sql modernizado con las reglas de PrestControl:
--   dinero DECIMAL(15,2), fechas DATETIME en UTC, soft deletes,
--   BCrypt (nunca texto plano), auditoría, numeración atómica.
-- Los triggers usan DELIMITER (solo para el cliente mysql); el
-- auto-aprovisionamiento de la app los ejecuta como bloques separados
-- usando los marcadores "-- @bloque".
-- =============================================================

-- Los scripts están en UTF-8. Sin esto, el cliente mysql.exe asume la
-- codificación de la consola y guarda 'Almacén' como basura (mojibake).
SET NAMES utf8mb4;

CREATE DATABASE IF NOT EXISTS med100_db
  CHARACTER SET utf8mb4
  COLLATE utf8mb4_unicode_ci;

USE med100_db;

-- -------------------------------------------------------------
-- rol: catálogo de roles (Admin / Supervisor / Cajero / Servicio)
-- -------------------------------------------------------------
CREATE TABLE rol (
  id          INT UNSIGNED NOT NULL AUTO_INCREMENT,
  nombre      VARCHAR(50)  NOT NULL,
  descripcion VARCHAR(200) NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_rol_nombre (nombre)
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- permiso: catálogo de permisos por módulo/acción
-- -------------------------------------------------------------
CREATE TABLE permiso (
  id          INT UNSIGNED NOT NULL AUTO_INCREMENT,
  codigo      VARCHAR(50)  NOT NULL,             -- ej: 'vender', 'clientes_editar'
  nombre      VARCHAR(100) NOT NULL,
  descripcion VARCHAR(200) NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_permiso_codigo (codigo)
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- rol_permiso: permisos que otorga cada rol
-- -------------------------------------------------------------
CREATE TABLE rol_permiso (
  rol_id     INT UNSIGNED NOT NULL,
  permiso_id INT UNSIGNED NOT NULL,
  PRIMARY KEY (rol_id, permiso_id),
  CONSTRAINT fk_rolperm_rol FOREIGN KEY (rol_id)
    REFERENCES rol (id) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT fk_rolperm_permiso FOREIGN KEY (permiso_id)
    REFERENCES permiso (id) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- usuario: empleados del negocio (multiusuario, a diferencia de PrestControl)
-- -------------------------------------------------------------
CREATE TABLE usuario (
  id            BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  username      VARCHAR(50)  NOT NULL,
  password_hash VARCHAR(100) NOT NULL,           -- BCrypt cost 12, JAMÁS texto plano
  nombre        VARCHAR(100) NOT NULL,
  apellido      VARCHAR(100) NULL,
  rol_id        INT UNSIGNED NULL,
  activo        TINYINT(1)   NOT NULL DEFAULT 1,
  created_at    DATETIME     NOT NULL DEFAULT (UTC_TIMESTAMP()),
  updated_at    DATETIME     NULL,
  last_login_at DATETIME     NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_usuario_username (username),
  CONSTRAINT fk_usuario_rol FOREIGN KEY (rol_id)
    REFERENCES rol (id) ON DELETE SET NULL ON UPDATE CASCADE
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- usuario_permiso: permisos efectivos por usuario
-- (sincronizado desde rol_permiso por triggers + overrides manuales)
-- -------------------------------------------------------------
CREATE TABLE usuario_permiso (
  usuario_id BIGINT UNSIGNED NOT NULL,
  permiso_id INT UNSIGNED    NOT NULL,
  PRIMARY KEY (usuario_id, permiso_id),
  CONSTRAINT fk_usuperm_usuario FOREIGN KEY (usuario_id)
    REFERENCES usuario (id) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT fk_usuperm_permiso FOREIGN KEY (permiso_id)
    REFERENCES permiso (id) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- sesion: logins/logouts (alimenta el tiempo activo del cuadre)
-- -------------------------------------------------------------
CREATE TABLE sesion (
  id         BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  usuario_id BIGINT UNSIGNED NOT NULL,
  login_at   DATETIME    NOT NULL DEFAULT (UTC_TIMESTAMP()),
  logout_at  DATETIME    NULL,
  ip_local   VARCHAR(45) NULL,
  PRIMARY KEY (id),
  KEY ix_sesion_usuario (usuario_id, login_at),
  CONSTRAINT fk_sesion_usuario FOREIGN KEY (usuario_id)
    REFERENCES usuario (id) ON DELETE RESTRICT
) ENGINE=InnoDB;

-- =============================================================
-- CLÍNICA (MED-100) — catálogos propios del negocio médico
-- Van ANTES de cliente y factura porque las dos los referencian.
-- =============================================================

-- -------------------------------------------------------------
-- referidor: de dónde viene el paciente (pedido del cliente 2026-08-10,
-- "registro de proveniento"). No es de dónde vive: es QUIÉN lo mandó.
-- Sirve para saber qué canal trae pacientes y cuál no rinde.
-- -------------------------------------------------------------
CREATE TABLE referidor (
  id     BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  nombre VARCHAR(150) NOT NULL,
  tipo   ENUM('medico','ars','publicidad','paciente','redes','otro') NOT NULL DEFAULT 'otro',
  notas  VARCHAR(250) NULL,
  activo TINYINT(1)   NOT NULL DEFAULT 1,
  PRIMARY KEY (id),
  KEY ix_referidor_tipo (tipo)
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- medico: información SIMPLE, es una app de recepción, no un
-- registro profesional. El exequátur es el número que habilita a
-- ejercer en RD; se guarda porque va impreso en la factura.
--
-- porcentaje_honorario: cuánto le toca de lo que factura. Es el valor
-- VIGENTE; el que se cobró en cada factura se copia a la factura (ver
-- factura.honorario_porcentaje). Cambiarlo acá NO reescribe el pasado.
-- -------------------------------------------------------------
CREATE TABLE medico (
  id                    BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  nombre                VARCHAR(150)  NOT NULL,
  cedula                VARCHAR(20)   NULL,
  exequatur             VARCHAR(30)   NULL,
  especialidad          VARCHAR(100)  NULL,
  telefono              VARCHAR(20)   NULL,
  email                 VARCHAR(150)  NULL,
  porcentaje_honorario  DECIMAL(5,2)  NOT NULL DEFAULT 0.00,
  -- Prefijo del turno de la sala: dos letras sacadas del nombre
  -- ("Yuber Santana Lizardo" -> "YO": primera del nombre, última del
  -- apellido). Sirve para que en la sala se distinga a simple vista
  -- a quién le toca cada número (pedido 2026-08-14).
  codigo_turno          VARCHAR(8)    NULL,
  activo                TINYINT(1)    NOT NULL DEFAULT 1,
  created_at            DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  updated_at            DATETIME      NULL,
  deleted_at            DATETIME      NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_medico_cedula (cedula),
  UNIQUE KEY uq_medico_codigo_turno (codigo_turno),
  KEY ix_medico_nombre (nombre),
  CONSTRAINT ck_medico_porcentaje CHECK (porcentaje_honorario BETWEEN 0 AND 100)
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- medico_horario: en qué días y a qué hora atiende cada médico
-- (pedido 2026-08-10: "sus horarios editables para saber quién está
-- disponible en el momento"). Varios tramos por día: mañana y tarde
-- son DOS filas, que es lo que permite el corte del almuerzo.
--
-- dia_semana sigue la convención de MySQL DAYOFWEEK(): 1=domingo .. 7=sábado.
-- -------------------------------------------------------------
CREATE TABLE medico_horario (
  id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  medico_id   BIGINT UNSIGNED NOT NULL,
  dia_semana  TINYINT UNSIGNED NOT NULL,
  hora_inicio TIME NOT NULL,
  hora_fin    TIME NOT NULL,
  PRIMARY KEY (id),
  KEY ix_horario_medico (medico_id, dia_semana),
  CONSTRAINT fk_horario_medico FOREIGN KEY (medico_id)
    REFERENCES medico (id) ON DELETE CASCADE,
  CONSTRAINT ck_horario_dia CHECK (dia_semana BETWEEN 1 AND 7),
  CONSTRAINT ck_horario_rango CHECK (hora_fin > hora_inicio)
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- ars: aseguradoras (Humano, Senasa, Primera ARS...). El módulo
-- completo se apaga desde configuracion_negocio.ars_activo si la
-- clínica termina trabajando solo privado (pedido 2026-08-10).
-- -------------------------------------------------------------
CREATE TABLE ars (
  id       BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  nombre   VARCHAR(150) NOT NULL,
  rnc      VARCHAR(20)  NULL,
  telefono VARCHAR(20)  NULL,
  notas    VARCHAR(250) NULL,
  activo   TINYINT(1)   NOT NULL DEFAULT 1,
  PRIMARY KEY (id),
  UNIQUE KEY uq_ars_nombre (nombre)
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- procedimiento: el tarifario ("costo de procedimientos").
--
-- exento_itbis viene en 1 a propósito: los servicios de salud están
-- EXENTOS de ITBIS en RD (consulta, laboratorio, imágenes, odontología).
-- Es la diferencia de fondo con POS-500, que cobra 18% a todo. Los
-- INSUMOS que se venden aparte sí pueden llevar ITBIS: por eso la
-- exención es por línea y no global.
--
-- duracion_minutos alimenta la agenda: con eso se sabe cuánto ocupa
-- el turno del médico.
-- -------------------------------------------------------------
CREATE TABLE procedimiento (
  id               BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  codigo           VARCHAR(50)   NULL,
  nombre           VARCHAR(200)  NOT NULL,
  precio           DECIMAL(15,2) NOT NULL,
  duracion_minutos INT UNSIGNED  NOT NULL DEFAULT 30,
  exento_itbis     TINYINT(1)    NOT NULL DEFAULT 1,
  descripcion      TEXT          NULL,
  activo           TINYINT(1)    NOT NULL DEFAULT 1,
  created_at       DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  updated_at       DATETIME      NULL,
  deleted_at       DATETIME      NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_procedimiento_codigo (codigo),
  KEY ix_procedimiento_nombre (nombre)
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- cliente: el PACIENTE. Se mantiene el nombre `cliente` porque es
-- como lo llama el dueño ("registro de cliente") y porque toda la
-- capa de datos heredada ya lo usa. En pantalla dice "Pacientes".
--
-- OJO — Ley 172-13: los datos de SALUD son sensibles y exigen
-- consentimiento expreso y por escrito. Por eso acá NO hay
-- diagnósticos, ni tratamientos, ni antecedentes: MED-100 es una app
-- de RECEPCIÓN, no el expediente clínico legal (decisión 2026-08-10).
-- `notas` es administrativo ("prefiere turno en la tarde"), no clínico.
-- -------------------------------------------------------------
CREATE TABLE cliente (
  id               BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  cedula           VARCHAR(20)  NULL,
  nombre           VARCHAR(150) NOT NULL,
  telefono         VARCHAR(20)  NULL,
  -- Hace falta de verdad: es por donde sale el recordatorio de cita.
  email            VARCHAR(150) NULL,
  fecha_nacimiento DATE         NULL,
  sexo             ENUM('F','M','otro') NULL,
  direccion        VARCHAR(250) NULL,
  -- Quién lo refirió (procedencia). NULL = llegó por su cuenta.
  referidor_id     BIGINT UNSIGNED NULL,
  notas            TEXT         NULL,
  created_at       DATETIME     NOT NULL DEFAULT (UTC_TIMESTAMP()),
  updated_at       DATETIME     NULL,
  deleted_at       DATETIME     NULL,            -- soft delete
  PRIMARY KEY (id),
  UNIQUE KEY uq_cliente_cedula (cedula),         -- múltiples NULL permitidos
  KEY ix_cliente_nombre (nombre),
  KEY ix_cliente_referidor (referidor_id),
  CONSTRAINT fk_cliente_referidor FOREIGN KEY (referidor_id)
    REFERENCES referidor (id) ON DELETE SET NULL
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- producto: el INVENTARIO DE INSUMOS (jeringas, guantes, reactivos...).
-- Se mantiene el nombre `producto` por la capa de datos heredada; en
-- pantalla dice "Insumos".
--
-- La caducidad, que en POS-500 era útil, acá es crítica: un insumo
-- médico vencido no se puede usar con un paciente.
--
-- exento_itbis por defecto en 0: a diferencia de los procedimientos,
-- vender un insumo NO es un servicio de salud y puede llevar ITBIS.
-- Confirmar con el contador de la clínica caso por caso.
-- -------------------------------------------------------------
CREATE TABLE producto (
  id              BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  codigo          VARCHAR(50)   NULL,            -- código de barras / interno
  nombre          VARCHAR(150)  NOT NULL,
  precio          DECIMAL(15,2) NOT NULL,
  cantidad        INT           NOT NULL DEFAULT 0,
  -- Qué clase de cosa es. En una clínica el almacén no es solo "insumos":
  -- hay medicamentos que se dispensan, material que se esteriliza y
  -- artículos de limpieza. Agruparlos permite pedirle al almacén una
  -- respuesta por familia (pedido de Yuber 2026-08-14).
  tipo            ENUM('insumo','medicamento','material','equipo','limpieza','oficina','otro')
                  NOT NULL DEFAULT 'insumo',
  exento_itbis    TINYINT(1)    NOT NULL DEFAULT 0,
  descripcion     TEXT          NULL,
  fecha_caducidad DATE          NULL,
  created_at      DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  updated_at      DATETIME      NULL,
  deleted_at      DATETIME      NULL,            -- soft delete
  PRIMARY KEY (id),
  UNIQUE KEY uq_producto_codigo (codigo),        -- múltiples NULL permitidos
  KEY ix_producto_nombre (nombre),
  KEY ix_producto_caducidad (fecha_caducidad),
  KEY ix_producto_tipo (tipo)
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- factura: NUNCA se elimina, solo se anula (estado). Totales
-- persistidos + tasa de ITBIS aplicada (histórico consistente si
-- la tasa cambia en configuracion_negocio).
-- cliente_id NULL = venta sin cliente / consumidor final.
-- -------------------------------------------------------------
CREATE TABLE factura (
  id                BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  numero_factura    VARCHAR(30)   NOT NULL,
  cliente_id        BIGINT UNSIGNED NULL,
  usuario_id        BIGINT UNSIGNED NOT NULL,    -- quién la cobró (recepción)
  -- Médico que atiende. NULL solo si la factura es de puros insumos.
  medico_id         BIGINT UNSIGNED NULL,
  fecha_emision     DATETIME      NOT NULL,      -- UTC
  subtotal          DECIMAL(15,2) NOT NULL,
  itbis_tasa        DECIMAL(5,2)  NOT NULL,      -- 0.00 en salud (exenta)
  itbis             DECIMAL(15,2) NOT NULL,
  total             DECIMAL(15,2) NOT NULL,

  -- ---- Reparto de honorarios (pedido 2026-08-10) ----
  -- El PORCENTAJE se copia acá al emitir, no se lee de medico. Si mañana
  -- se le sube el porcentaje al médico, los cuadres viejos y las
  -- reimpresiones tienen que seguir diciendo lo mismo que se pagó ese día.
  -- Es la lección que dejó la comisión del vendedor en POS-500.
  honorario_porcentaje DECIMAL(5,2)  NOT NULL DEFAULT 0.00,
  honorario_monto      DECIMAL(15,2) NOT NULL DEFAULT 0.00,

  -- ---- Seguro / ARS (pedido 2026-08-10, apagable en configuración) ----
  -- ars_cubierto + paciente_paga = total. Lo que cubre el seguro no
  -- entra en la caja del día: por eso se guardan separados y no como
  -- un descuento.
  ars_id            BIGINT UNSIGNED NULL,        -- NULL = paciente privado
  ars_autorizacion  VARCHAR(50)   NULL,          -- número que da la ARS
  ars_cubierto      DECIMAL(15,2) NOT NULL DEFAULT 0.00,
  paciente_paga     DECIMAL(15,2) NOT NULL DEFAULT 0.00,  -- copago/diferencia

  metodo_pago       ENUM('efectivo','tarjeta','transferencia','mixto') NOT NULL,
  efectivo_recibido DECIMAL(15,2) NULL,          -- solo efectivo/mixto
  cambio            DECIMAL(15,2) NULL,
  ncf               VARCHAR(19)   NULL,          -- comprobante fiscal (ver nota e-CF)
  estado            ENUM('emitida','anulada') NOT NULL DEFAULT 'emitida',
  anulada_at        DATETIME      NULL,
  anulada_motivo    VARCHAR(250)  NULL,
  created_at        DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  PRIMARY KEY (id),
  UNIQUE KEY uq_factura_numero (numero_factura),
  UNIQUE KEY uq_factura_ncf (ncf),               -- múltiples NULL permitidos
  KEY ix_factura_fecha (fecha_emision),
  KEY ix_factura_usuario (usuario_id, fecha_emision),
  KEY ix_factura_medico (medico_id, fecha_emision),   -- reporte "por médico"
  KEY ix_factura_ars (ars_id, fecha_emision),
  CONSTRAINT fk_factura_cliente FOREIGN KEY (cliente_id)
    REFERENCES cliente (id) ON DELETE RESTRICT,
  CONSTRAINT fk_factura_usuario FOREIGN KEY (usuario_id)
    REFERENCES usuario (id) ON DELETE RESTRICT,
  CONSTRAINT fk_factura_medico FOREIGN KEY (medico_id)
    REFERENCES medico (id) ON DELETE RESTRICT,
  CONSTRAINT fk_factura_ars FOREIGN KEY (ars_id)
    REFERENCES ars (id) ON DELETE RESTRICT
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- detalle: líneas de factura (RESTRICT: las facturas no se borran).
--
-- Una línea es UN PROCEDIMIENTO o UN INSUMO, nunca las dos cosas y
-- nunca ninguna: lo garantiza ck_detalle_una_cosa. Es el mismo patrón
-- que el expediente de FAControl, que cuelga de una venta o de un
-- préstamo con clave foránea de verdad en ambos casos.
--
-- exento_itbis se COPIA acá al facturar en vez de leerse del catálogo:
-- si mañana cambia la exención de un procedimiento, la factura vieja
-- tiene que seguir diciendo lo que se cobró.
-- -------------------------------------------------------------
CREATE TABLE detalle (
  id               BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  factura_id       BIGINT UNSIGNED NOT NULL,
  procedimiento_id BIGINT UNSIGNED NULL,
  producto_id      BIGINT UNSIGNED NULL,
  descripcion      VARCHAR(200)  NOT NULL,       -- nombre al momento de facturar
  cantidad         INT           NOT NULL,
  precio_unitario  DECIMAL(15,2) NOT NULL,       -- precio al momento de la venta
  exento_itbis     TINYINT(1)    NOT NULL,     -- sin default a propósito: siempre explícito
  subtotal         DECIMAL(15,2) NOT NULL,       -- cantidad * precio_unitario
  PRIMARY KEY (id),
  KEY ix_detalle_factura (factura_id),
  KEY ix_detalle_procedimiento (procedimiento_id),
  KEY ix_detalle_producto (producto_id),
  CONSTRAINT fk_detalle_factura FOREIGN KEY (factura_id)
    REFERENCES factura (id) ON DELETE RESTRICT,
  CONSTRAINT fk_detalle_procedimiento FOREIGN KEY (procedimiento_id)
    REFERENCES procedimiento (id) ON DELETE RESTRICT,
  CONSTRAINT fk_detalle_producto FOREIGN KEY (producto_id)
    REFERENCES producto (id) ON DELETE RESTRICT,
  CONSTRAINT ck_detalle_una_cosa CHECK (
    (procedimiento_id IS NOT NULL AND producto_id IS NULL) OR
    (procedimiento_id IS NULL     AND producto_id IS NOT NULL))
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- cita: la agenda. "Colocar cita recordatorio por correo" (2026-08-10).
--
-- recordatorio_enviado_at existe para no mandar el mismo correo dos
-- veces si la app se abre varias veces en el día. Se llena cuando el
-- envío SALIÓ BIEN, nunca antes.
-- -------------------------------------------------------------
CREATE TABLE cita (
  id                     BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  cliente_id             BIGINT UNSIGNED NOT NULL,
  medico_id              BIGINT UNSIGNED NOT NULL,
  procedimiento_id       BIGINT UNSIGNED NULL,   -- a qué viene; NULL = consulta general
  fecha_hora             DATETIME      NOT NULL, -- UTC
  duracion_minutos       INT UNSIGNED  NOT NULL DEFAULT 30,
  estado                 ENUM('programada','confirmada','atendida','cancelada','no_asistio')
                         NOT NULL DEFAULT 'programada',
  notas                  VARCHAR(250)  NULL,
  recordatorio_enviado_at DATETIME     NULL,
  factura_id             BIGINT UNSIGNED NULL,   -- se llena al cobrarla
  created_at             DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  updated_at             DATETIME      NULL,
  PRIMARY KEY (id),
  KEY ix_cita_fecha (fecha_hora),
  KEY ix_cita_medico (medico_id, fecha_hora),
  KEY ix_cita_cliente (cliente_id, fecha_hora),
  KEY ix_cita_recordatorio (fecha_hora, recordatorio_enviado_at),
  CONSTRAINT fk_cita_cliente FOREIGN KEY (cliente_id)
    REFERENCES cliente (id) ON DELETE RESTRICT,
  CONSTRAINT fk_cita_medico FOREIGN KEY (medico_id)
    REFERENCES medico (id) ON DELETE RESTRICT,
  CONSTRAINT fk_cita_procedimiento FOREIGN KEY (procedimiento_id)
    REFERENCES procedimiento (id) ON DELETE RESTRICT,
  CONSTRAINT fk_cita_factura FOREIGN KEY (factura_id)
    REFERENCES factura (id) ON DELETE RESTRICT
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- turno: el número de la sala de espera (pedido 2026-08-10). Se
-- imprime en un ticket APARTE del recibo de facturación: son dos
-- papeles distintos porque el paciente se lleva el turno antes de
-- que exista la factura.
--
-- El número reinicia todos los días: uq_turno_fecha_numero lo
-- garantiza, y se reserva con SELECT ... FOR UPDATE como el número
-- de factura. `fecha` es día de negocio local, no UTC: el turno 15
-- es el turno 15 de hoy para la gente de la sala, no de un día UTC.
-- -------------------------------------------------------------
CREATE TABLE turno (
  id         BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  fecha      DATE          NOT NULL,
  numero     INT UNSIGNED  NOT NULL,
  cliente_id BIGINT UNSIGNED NULL,       -- puede darse antes de registrarlo
  medico_id  BIGINT UNSIGNED NULL,       -- a qué médico espera
  cita_id    BIGINT UNSIGNED NULL,       -- si venía con cita
  estado     ENUM('esperando','llamado','atendido','ausente') NOT NULL DEFAULT 'esperando',
  created_at DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  llamado_at DATETIME      NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_turno_fecha_numero (fecha, numero),
  KEY ix_turno_estado (fecha, estado),
  CONSTRAINT fk_turno_cliente FOREIGN KEY (cliente_id)
    REFERENCES cliente (id) ON DELETE RESTRICT,
  CONSTRAINT fk_turno_medico FOREIGN KEY (medico_id)
    REFERENCES medico (id) ON DELETE RESTRICT,
  CONSTRAINT fk_turno_cita FOREIGN KEY (cita_id)
    REFERENCES cita (id) ON DELETE RESTRICT
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- cuadre_caja: cierre por usuario y día de negocio; inmutable tras crearse
-- -------------------------------------------------------------
CREATE TABLE cuadre_caja (
  id                     BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  usuario_id             BIGINT UNSIGNED NOT NULL,
  fecha                  DATE          NOT NULL,  -- día de negocio (UTC-4)
  total_facturas         INT           NOT NULL,
  total_vendido          DECIMAL(15,2) NOT NULL,
  tiempo_activo_segundos INT           NOT NULL DEFAULT 0,
  created_at             DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  PRIMARY KEY (id),
  UNIQUE KEY uq_cuadre_usuario_fecha (usuario_id, fecha),
  CONSTRAINT fk_cuadre_usuario FOREIGN KEY (usuario_id)
    REFERENCES usuario (id) ON DELETE RESTRICT
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- auditoria: toda mutación queda registrada (patrón PrestControl)
-- -------------------------------------------------------------
CREATE TABLE auditoria (
  id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  usuario_id  BIGINT UNSIGNED NOT NULL,
  entidad     VARCHAR(50)  NOT NULL,  -- 'cliente','producto','factura','usuario','configuracion',...
  entidad_id  BIGINT UNSIGNED NULL,
  accion      ENUM('crear','modificar','eliminar','consultar','login','logout','anular') NOT NULL,
  descripcion TEXT         NULL,
  ip_local    VARCHAR(45)  NULL,
  timestamp   DATETIME     NOT NULL DEFAULT (UTC_TIMESTAMP()),
  PRIMARY KEY (id),
  KEY ix_auditoria_entidad (entidad, entidad_id),
  KEY ix_auditoria_timestamp (timestamp),
  CONSTRAINT fk_auditoria_usuario FOREIGN KEY (usuario_id)
    REFERENCES usuario (id) ON DELETE RESTRICT
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- configuracion_negocio: UNA sola fila (id fijo = 1).
-- Datos compartidos del negocio; las preferencias por PC van en
-- ajustes.json (AjustesLocales), no aquí.
-- -------------------------------------------------------------
CREATE TABLE configuracion_negocio (
  id                       TINYINT UNSIGNED NOT NULL,
  nombre_negocio           VARCHAR(150)  NOT NULL DEFAULT 'Mi Negocio',
  rnc                      VARCHAR(20)   NULL,
  direccion                VARCHAR(250)  NULL,
  telefono                 VARCHAR(20)   NULL,
  email                    VARCHAR(150)  NULL,
  logo_ruta                VARCHAR(500)  NULL,
  -- Arranca APAGADO: los servicios de salud están exentos de ITBIS en RD.
  -- Se enciende solo si la clínica vende insumos gravados; aun así la
  -- exención real se decide por línea (procedimiento.exento_itbis).
  itbis_activo             TINYINT(1)    NOT NULL DEFAULT 0,
  itbis_tasa               DECIMAL(5,2)  NOT NULL DEFAULT 18.00,
  -- Módulo de seguros. OFF = la pantalla de cobro ni menciona la ARS
  -- (pedido 2026-08-10: "un checkbox para deshabilitarlo si no llegan a usarlo").
  ars_activo               TINYINT(1)    NOT NULL DEFAULT 1,
  -- Turnos de sala de espera: prefijo y si se imprime el ticket solo
  turno_prefijo            VARCHAR(10)   NOT NULL DEFAULT '',
  turno_imprimir_auto      TINYINT(1)    NOT NULL DEFAULT 1,
  -- Copia en PDF de los documentos en el expediente del paciente.
  -- La FACTURA sí por defecto: es un comprobante fiscal que el paciente puede
  -- reclamar meses después. El TURNO no: es un papelito que se tira al salir,
  -- y archivarlo llenaría el expediente de un paciente frecuente con cuarenta
  -- PDF al año.
  archivar_factura_pdf     TINYINT(1)    NOT NULL DEFAULT 1,
  archivar_turno_pdf       TINYINT(1)    NOT NULL DEFAULT 0,
  redondeo                 ENUM('centavo','peso','arriba') NOT NULL DEFAULT 'centavo',
  moneda_simbolo           VARCHAR(10)   NOT NULL DEFAULT 'RD$',
  formato_miles            ENUM('coma','punto') NOT NULL DEFAULT 'coma',  -- 1,234.56 / 1.234,56
  factura_prefijo          VARCHAR(10)   NOT NULL DEFAULT 'F-',
  factura_siguiente        BIGINT UNSIGNED NOT NULL DEFAULT 1,  -- SELECT ... FOR UPDATE al emitir
  factura_formato          ENUM('simple','con_anio') NOT NULL DEFAULT 'simple',  -- F-0001 / F-2026-0001
  mostrar_cliente_en_venta TINYINT(1)    NOT NULL DEFAULT 1,   -- regla Yuber: OFF = solo código de compra
  updated_at               DATETIME      NULL,
  PRIMARY KEY (id),
  CONSTRAINT ck_config_unica CHECK (id = 1)
) ENGINE=InnoDB;

INSERT INTO configuracion_negocio (id) VALUES (1);

-- =============================================================
-- TRIGGERS: sincronizar usuario_permiso con el rol (patrón POS-400)
-- Los overrides manuales se conservan solo mientras no cambie el rol.
-- =============================================================

-- -------------------------------------------------------------
-- documento_paciente: el expediente digital del paciente
-- (pedido de Yuber 2026-08-14, copiando el patrón que ya probó
-- FAControl). Guarda lo que el paciente entrega —cédula, carné de
-- la ARS, consentimientos, referimientos, estudios— para tenerlo a
-- mano la próxima vez que venga.
--
-- El ARCHIVO NO va acá. Un BLOB por cada foto de cédula hincha el
-- dump y hace lentísimo el respaldo: el archivo va al disco, en
-- <carpeta de la app>\expedientes\pacientes\<cliente_id>\, y esta
-- tabla guarda su ficha. `ruta_relativa` es relativa a esa raíz,
-- así mover la instalación de PC no rompe el expediente.
--
-- ⚠️ LEY 172-13: acá entran datos de salud, que son SENSIBLES. Cada
-- alta, apertura y borrado queda en `auditoria`. MED-100 sigue sin
-- tener campos de diagnóstico ni tratamiento (§1.1): esto es un
-- archivador de papeles escaneados, no un expediente clínico.
-- -------------------------------------------------------------
CREATE TABLE documento_paciente (
  id             BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  cliente_id     BIGINT UNSIGNED NOT NULL,
  nombre         VARCHAR(255)  NOT NULL,          -- como lo ve el usuario
  ruta_relativa  VARCHAR(400)  NOT NULL,          -- 'pacientes/<id>/<doc>_<nombre>'
  extension      VARCHAR(15)   NOT NULL,
  tamano_bytes   BIGINT UNSIGNED NOT NULL DEFAULT 0,
  tipo           ENUM('otro','identificacion','seguro','consentimiento',
                      'referimiento','estudio','factura') NOT NULL DEFAULT 'otro',
  notas          VARCHAR(300)  NULL,
  created_at     DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  created_by     BIGINT UNSIGNED NULL,
  deleted_at     DATETIME      NULL,              -- soft delete
  PRIMARY KEY (id),
  KEY ix_docpaciente_cliente (cliente_id),
  KEY ix_docpaciente_tipo (cliente_id, tipo),
  -- RESTRICT y no CASCADE: un paciente con papeles no se borra sin que
  -- alguien decida antes qué hacer con ellos.
  CONSTRAINT fk_docpaciente_cliente FOREIGN KEY (cliente_id)
    REFERENCES cliente (id) ON DELETE RESTRICT,
  CONSTRAINT fk_docpaciente_usuario FOREIGN KEY (created_by)
    REFERENCES usuario (id) ON DELETE SET NULL
) ENGINE=InnoDB;


-- -------------------------------------------------------------
-- licencia: fila unica (id = 1). Desde cuando corre el demo de 15
-- dias y si ya se activo con la llave del producto.
-- La app la crea sola si falta (LicenciaRepository), asi que una base
-- vieja tampoco se queda sin ella.
-- -------------------------------------------------------------
CREATE TABLE licencia (
  id                  TINYINT UNSIGNED NOT NULL,
  instalada_at_utc    DATETIME     NOT NULL,          -- primer arranque en este equipo
  activada            TINYINT(1)   NOT NULL DEFAULT 0,
  activada_at_utc     DATETIME     NULL,
  activada_por        VARCHAR(80)  NULL,               -- usuario de MED-100 que escribio la llave
  -- Ultimo arranque. Si el reloj queda por detras de esto, alguien lo movio.
  ultima_apertura_utc DATETIME     NOT NULL,
  PRIMARY KEY (id),
  CONSTRAINT ck_licencia_fila_unica CHECK (id = 1)
) ENGINE=InnoDB;

DELIMITER $$

-- @bloque
CREATE TRIGGER trg_usuario_after_insert
AFTER INSERT ON usuario
FOR EACH ROW
BEGIN
  IF NEW.rol_id IS NOT NULL THEN
    INSERT IGNORE INTO usuario_permiso (usuario_id, permiso_id)
    SELECT NEW.id, rp.permiso_id
    FROM rol_permiso rp
    WHERE rp.rol_id = NEW.rol_id;
  END IF;
END$$

-- @bloque
CREATE TRIGGER trg_usuario_after_update
AFTER UPDATE ON usuario
FOR EACH ROW
BEGIN
  IF (OLD.rol_id IS NULL AND NEW.rol_id IS NOT NULL)
     OR (OLD.rol_id IS NOT NULL AND NEW.rol_id IS NULL)
     OR (OLD.rol_id <> NEW.rol_id) THEN
    DELETE FROM usuario_permiso WHERE usuario_id = NEW.id;
    IF NEW.rol_id IS NOT NULL THEN
      INSERT IGNORE INTO usuario_permiso (usuario_id, permiso_id)
      SELECT NEW.id, rp.permiso_id
      FROM rol_permiso rp
      WHERE rp.rol_id = NEW.rol_id;
    END IF;
  END IF;
END$$

DELIMITER ;
