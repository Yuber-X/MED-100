# CLAUDE.md — MED-100

> Guía de proyecto para Claude Code. Define el alcance, la arquitectura y las reglas de **MED-100**, la app de recepción para una clínica. Nace como copia del **POS-500** (2026-08-10) y hereda su arquitectura entera. Se complementa con el `CLAUDE.md` global de freelance y con el `DESIGN.md`.

---

## 1. Identidad del proyecto

- **Nombre provisional:** MED-100 (renombrar si el cliente elige otro)
- **Tipo:** Aplicación de escritorio para Windows
- **Propósito:** Atención al cliente en la recepción de una clínica: pacientes, citas, turnos de sala de espera, facturación de procedimientos e inventario de insumos
- **Usuarios finales:** Personal de recepción y administración, con roles
- **Modelo de operación:** 100% local; internet solo para correo y respaldo
- **Idioma:** Español dominicano en toda la UI
- **Origen:** copia del POS-500 standalone. Todo lo que no sea del dominio médico (login, roles, auditoría, respaldo, correo, cuadre, impresión, reportes) ya venía funcionando y **no se reescribe**.

---

## 1.1. 🔴 EL LÍMITE DEL ALCANCE (leer antes de agregar cualquier cosa)

**MED-100 NO es el expediente clínico.** Es una app de **recepción**. Decisión tomada con Yuber el 2026-08-10 y no es un detalle: es lo que mantiene el proyecto chico y fuera de riesgo legal.

Concretamente, **acá NO se guardan**: diagnósticos, tratamientos, evoluciones, resultados de laboratorio, antecedentes ni notas médicas.

**Por qué importa:**

- La **Ley 172-13** clasifica los datos de salud como **sensibles**: exigen consentimiento *expreso y por escrito* del paciente y obligación de secreto profesional. Guardar diagnósticos convierte la app en un tratamiento de datos sensibles con todo lo que eso arrastra.
- El **expediente clínico** de verdad está regulado por el MSP: conservación **mínima de 5 años desde el último acto médico**, es propiedad de la institución y del médico, el paciente pide su resumen por escrito y motivado, y terceros solo acceden por orden judicial o autoridad sanitaria.

`cliente.notas` es **administrativo** ("prefiere turno en la tarde", "viene con su mamá"), nunca clínico. Si el cliente pide guardar diagnósticos, **eso es otro proyecto y otra conversación** — no se agrega sin decidirlo explícitamente.

### 🟠 El límite se movió: Almacén de Expedientes (2026-08-14)

Yuber pidió un almacén de documentos del paciente, igual al de contratos de FAControl:
*"guardar cada información de todos los documentos de los pacientes por si vuelven a pasar
a la clínica y se sepa ya lo necesario para proceder"*.

**Eso es un cambio real de alcance y hay que verlo de frente:** entre los papeles que
entrega un paciente hay estudios y resultados, y eso **es dato de salud** — sensible bajo
la Ley 172-13. Antes de esto la app no guardaba nada clínico.

Lo que se decidió y cómo quedó acotado:

- Es un **archivador de papeles escaneados**, no un expediente clínico estructurado. La
  app **sigue sin un solo campo** de diagnóstico, tratamiento, evolución ni antecedente.
  Nada se puede buscar, filtrar ni reportar por contenido clínico: son archivos con nombre.
- **Cada alta, apertura y borrado queda en `auditoria`** con usuario y hora. La ley exige
  poder decir quién miró qué.
- **Eliminar y re-ubicar son exclusivos del Admin.** Soft delete: el archivo se conserva
  en disco para que soporte pueda deshacer.
- Permiso propio `expedientes`. El Cajero **no** lo tiene.
- **El archivado AUTOMÁTICO de la factura al cobrar no pide ese permiso** y es
  deliberado: el Cajero es quien emite las facturas, así que exigirlo dejaría sin
  copia a todas las del mostrador y el fallo sería invisible. `expedientes`
  gobierna *hojear los papeles*; archivar lo que uno mismo acaba de emitir, no.
  Ver `ExpedienteService.ArchivarImpresoAsync`.

⚠️ **Lo que falta y no es técnico:** la Ley 172-13 exige **consentimiento expreso y por
escrito** del paciente para tratar datos de salud. Eso lo tiene que resolver la clínica con
un formulario firmado; el sistema no lo suple. Confirmar con el cliente antes de entregar.

---

## 1.2. Lo que pidió el cliente (2026-08-08 / 08-10)

> *"Aquí solo registro de cliente, por médico y total, registro de proveniento, costo de procedimientos, factura, inventario de insumos. Colocar cita recordatorio por correo, luego haremos el convenio con WhatsApp."*

Traducido a módulos, con las respuestas que dio Yuber el 2026-08-10:

| Pedido | Qué significa | Decisión |
|---|---|---|
| Registro de cliente | Pacientes | Tabla `cliente` (se conserva el nombre: así lo llama el dueño) |
| Por médico y total | Ingresos desglosados por médico | **Reparto de honorarios** con % por médico |
| Registro de proveniento | **Quién refirió** al paciente | Tabla `referidor` (médico, ARS, publicidad, redes…) |
| Costo de procedimientos | Tarifario | Tabla `procedimiento` |
| Factura | Cobro | Hereda de POS-500 + honorarios + ARS |
| Inventario de insumos | Stock con caducidad | Tabla `producto` (UI: "Insumos") |
| Cita + recordatorio | Agenda con aviso por correo | Tablas `cita` + `EmailService` heredado |
| Tickets | **Dos impresiones distintas**: recibo de factura Y número de turno | `factura` + tabla `turno` |
| Médicos | Info simple + **horarios editables** para saber quién está disponible | `medico` + `medico_horario` |
| ARS / seguros | Se usarán, con casilla para apagarlo | `ars` + `configuracion_negocio.ars_activo` |
| WhatsApp | Más adelante | Fuera de alcance por ahora |

---

## 1.3. Reglas del dominio que NO se negocian

1. **Los servicios de salud están EXENTOS de ITBIS** en RD (consulta, laboratorio, imágenes, odontología). Es la diferencia de fondo con POS-500, que cobra 18% a todo. Por eso `itbis_activo` nace en 0 y la exención se decide **por línea** (`procedimiento.exento_itbis` = 1, `producto.exento_itbis` = 0). Vender un insumo no es un servicio de salud.
2. **El porcentaje de honorario se copia a la factura al emitirla.** Nunca se lee del catálogo `medico` para mostrar una factura vieja. Si mañana se le sube el porcentaje al médico, los cuadres y las reimpresiones del pasado tienen que seguir diciendo lo que se pagó ese día. Es la lección que dejó la comisión del vendedor en POS-500.
3. **Lo que cubre la ARS no entra en la caja del día.** `ars_cubierto` y `paciente_paga` van separados y suman el total. No es un descuento.
4. **El turno y la factura son dos papeles distintos.** El paciente se lleva el turno *antes* de que exista la factura.
5. Todo lo demás sigue vigente del `CLAUDE.md` global: dinero en `DECIMAL`, UTC en la base, soft deletes, BCrypt, auditoría de toda mutación, SQL parametrizado siempre.

---

## 1.4. 🟡 Pendiente de confirmar con el cliente: la factura electrónica

**La Ley 32-23 hace obligatorio el e-CF para pequeños, micro y no clasificados desde el 15 de noviembre de 2026.** Una clínica cae casi seguro ahí. Quien no cumple arriesga multas de 5 a 50 salarios mínimos y que sus comprobantes pierdan validez fiscal.

Por ahora MED-100 maneja el comprobante **como FAControl**: se configura o se asigna un NCF (`factura.ncf`). Yuber decidió el 2026-08-10 dejar la decisión del e-CF para el final y verificarla con el cliente.

Los tres caminos, para cuando se retome:

- **A.** La clínica factura por el **Facturador Gratuito de la DGII**. Gratis, pero tope de ~150 facturas/mes, sin guardar datos del cliente, y hay que retipear todo.
- **B.** MED-100 emite e-CF (XML firmado + conexión con DGII). Requiere **certificado digital pagado**: el gratuito de la DGII **solo funciona dentro de su propio facturador**.
- **C.** Integrar con un PFE (proveedor autorizado).

⚠️ **Punto sin retorno:** una vez autorizado por otro método, no se puede volver al Facturador Gratuito.

**Diferencia clave con PrestControl:** este sistema **sí es multiusuario**. Incluye toda la lógica de `rol`, `permiso`, `rol_permiso`, `usuario_permiso` heredada del POS-500. La restricción de acceso por rol es obligatoria en cada módulo.

---

## 2. Stack técnico

Idéntico a PrestControl (reutilización total del stack para minimizar riesgo):

| Capa | Tecnología |
|---|---|
| Runtime | .NET 8 (LTS) |
| UI | WPF (Windows Presentation Foundation) con XAML |
| Patrón | MVVM estricto |
| MVVM helpers | `CommunityToolkit.Mvvm` |
| Base de datos | MySQL 8.0+ instalado localmente |
| Acceso a datos | `MySqlConnector` |
| Hashing de contraseñas | `BCrypt.Net-Next` (cost 12) |
| Logging | `Serilog` con sink a archivo rotativo |
| Impresión | `System.Printing` para tickets 80mm |
| Gráficos del dashboard | `LiveChartsCore.SkiaSharpView.WPF` |
| Excel export | `ClosedXML` (o equivalente que ya use PrestControl) |
| Testing | `xUnit` + `FluentAssertions` |

**Nullable reference types:** habilitado.
**Namespaces:** file-scoped.
**Async/await:** obligatorio en toda operación de BD y de I/O.

---

## 3. Proyectos de referencia

### PrestControl (referencia arquitectural PRINCIPAL)
Ruta esperada: hermano en el workspace, carpeta `PrestControl`.

Claude Code **debe leer PrestControl primero** y reutilizar sin modificaciones:
- Estructura completa de la solución (`src/PrestControl.App`, `src/PrestControl.ViewModels`, etc.)
- `MainWindow` con sidebar y sistema de navegación
- `LoginView` y flujo de autenticación con BCrypt
- Estilos WPF completos (`Card`, `Boton.Primario`, `Input.Password`, `Texto.Titulo3`, etc.)
- `IDialogService`, `AjustesLocales`, `SesionActual`, `MoneyConverter`, `DateConverter`, `FechaNegocio`, `DbNames`
- Patrón de repositorio con `MySqlConnector`
- `AuditoriaService` y su tabla
- `RespaldoService` (mysqldump + restore)
- `ExportacionService` (Excel manual + automática)
- Estructura de `AjustesLocales.cs` (JSON local)
- Instalador con Inno Setup en `installer/`

**Regla:** cualquier patrón que ya exista en PrestControl se copia y adapta. NO se reinventa.

### POS-400 (referencia de lógica de negocio PRINCIPAL)
Ruta esperada: hermano en el workspace, carpeta `POS-400` o `MiPOSCSharpMySQL`.

Fuente de la lógica de negocio y del esquema de datos:
- Estructura de tablas: `usuario`, `rol`, `permiso`, `rol_permiso`, `usuario_permiso`, `cliente`, `producto`, `factura`, `detalle`
- Triggers de sincronización de permisos al crear/modificar usuarios
- Cálculo de ITBIS 18%
- Lógica de venta (búsqueda, carrito, validación de stock, método de pago)
- Impresión de ticket 80mm (patrón de imagen con logo como marca de agua)
- Cierre de caja por usuario con tiempo activo de sesión
- Búsqueda de comprobante por número y reimpresión
- Sistema de alertas de caducidad con semáforo (verde → rojo)
- Reportes por rango de fechas

**Regla:** el CÓDIGO del POS-400 no se copia porque es Windows Forms. Solo se replican los PATRONES y las QUERIES SQL. Toda la UI se hace en WPF nuevo siguiendo PrestControl.

### Referencia SQL del POS-400
El script `BDPOS-400.sql` es la base del nuevo esquema. Adaptaciones necesarias:
- Todas las columnas monetarias a `DECIMAL(15,2)` sin excepción
- Contraseñas: cambiar `VARCHAR(50)` texto plano a `VARCHAR(255)` para almacenar hash BCrypt
- Agregar columnas de auditoría (`created_at`, `updated_at`, `deleted_at`) donde aplique
- Todas las fechas en UTC
- Agregar tabla `auditoria` (patrón de PrestControl)

---

## 4. Arquitectura de solución

Idéntica a PrestControl. Solo cambia el nombre del namespace raíz.

```
MED-100/
├── MED100.sln
├── src/
│   ├── MED100.App/                 → App.xaml, MainWindow, DI container
│   ├── MED100.Views/               → todas las Views XAML
│   ├── MED100.ViewModels/          → ViewModels
│   ├── MED100.Models/              → entidades de dominio
│   ├── MED100.Services/            → lógica de negocio
│   ├── MED100.Data/                → repositorios, acceso a MySQL
│   ├── MED100.Common/              → helpers, converters, AjustesLocales
│   └── MED100.Printing/            → generación de tickets 80mm
├── tests/
│   ├── MED100.Services.Tests/
│   └── MED100.Data.Tests/
├── scripts/
│   └── db/
│       ├── 001_create_schema.sql
│       ├── 002_seed_data.sql       → roles y permisos base
│       └── 999_rollback.sql
├── docs/
│   ├── DESIGN.md
│   ├── SCHEMA.md
│   └── ITBIS.md                    → notas sobre el cálculo fiscal
├── installer/
│   └── MED100.iss                  → Inno Setup
├── CLAUDE.md                       → este archivo
├── TODO.md
├── CHANGELOG.md
└── BLOCKERS.md
```

**Regla de dependencias (idéntica a PrestControl):**

```
Views → ViewModels → Services → Data → Models
Common es utilizado por todas.
Ninguna dependencia inversa está permitida.
```

---

## 5. Esquema de base de datos

Base: `med100_db`. `InnoDB`, `utf8mb4_unicode_ci`.

### Tablas de autenticación y control de acceso

Del POS-400, adaptadas al patrón moderno:

| Tabla | Propósito |
|---|---|
| `usuario` | id, username, password_hash (BCrypt), nombre, apellido, rol_id, activo, created_at, updated_at, last_login_at |
| `rol` | id, nombre (Admin/Supervisor/Cajero/Vendedor), descripcion |
| `permiso` | id, codigo, nombre, descripcion |
| `rol_permiso` | rol_id, permiso_id (composite PK) |
| `usuario_permiso` | usuario_id, permiso_id (overrides individuales) |
| `sesion` | id, usuario_id, login_at, logout_at, ip_local |

### Tablas de negocio

Heredadas del POS-500 (mismo significado):

| Tabla | Propósito |
|---|---|
| `cliente` | **El paciente.** + email, fecha_nacimiento, sexo, referidor_id. Sin datos clínicos (ver §1.1) |
| `producto` | **El almacén.** + exento_itbis + `tipo` (insumo, medicamento, material, equipo, limpieza, oficina, otro). La caducidad acá es crítica: un insumo vencido no se usa con un paciente |
| `factura` | + medico_id, honorario_porcentaje, honorario_monto, ars_id, ars_autorizacion, ars_cubierto, paciente_paga, ncf |
| `detalle` | Ahora la línea es **un procedimiento O un insumo**, nunca las dos ni ninguna (`ck_detalle_una_cosa`) |
| `cuadre_caja` | Cierre por usuario y día |
| `auditoria` | Toda mutación |

Propias de la clínica:

| Tabla | Propósito |
|---|---|
| `medico` | Info simple + `porcentaje_honorario` vigente + exequátur (va impreso en la factura) + `codigo_turno` (dos letras del nombre: "Yuber Santana Lizardo" → `YO`, y sus turnos salen `YO-1`, `YO-2`) |
| `medico_horario` | Días y tramos horarios. Varias filas por día: mañana y tarde son dos, y eso es lo que permite el corte del almuerzo. `dia_semana` sigue `DAYOFWEEK()` de MySQL: 1=domingo |
| `referidor` | De dónde viene el paciente (médico, ARS, publicidad, redes, otro paciente) |
| `procedimiento` | Tarifario: precio, `duracion_minutos` (alimenta la agenda), `exento_itbis` |
| `ars` | Aseguradoras. El módulo entero se apaga con `configuracion_negocio.ars_activo` |
| `cita` | Agenda + `recordatorio_enviado_at` (se llena cuando el correo SALIÓ BIEN, nunca antes) |
| `turno` | Número de sala de espera. Reinicia cada día (`uq_turno_fecha_numero`) y se reserva con `SELECT … FOR UPDATE`, igual que el número de factura |
| `documento_paciente` | **Expediente digital del paciente**: la FICHA de cada papel. El archivo vive en el disco (`expedientes\pacientes\<cliente_id>\`), no en la base — un BLOB por cada cédula escaneada haría inviable el respaldo diario. Ver §1.1 |

### Enumeraciones

```sql
usuario.rol_id             → FK a rol
factura.metodo_pago        → ENUM('efectivo','tarjeta','transferencia','mixto')
factura.estado             → ENUM('emitida','anulada')
producto.estado_caducidad  → CALCULADO (no persistido) — verde/amarillo/naranja/rojo
auditoria.accion           → ENUM('crear','modificar','eliminar','consultar','login','logout','anular')
referidor.tipo             → ENUM('medico','ars','publicidad','paciente','redes','otro')
cita.estado                → ENUM('programada','confirmada','atendida','cancelada','no_asistio')
turno.estado               → ENUM('esperando','llamado','atendido','ausente')
cliente.sexo               → ENUM('F','M','otro')
```

### Triggers requeridos (del POS-400)
- `AFTER INSERT` en `usuario`: sincronizar `usuario_permiso` con los `rol_permiso` del rol asignado
- `AFTER UPDATE` en `usuario.rol_id`: resincronizar permisos si cambió el rol

### Restricciones críticas
- `usuario.username` es `UNIQUE`
- `cliente.cedula` es `UNIQUE`
- `producto.codigo` y `procedimiento.codigo` son `UNIQUE` (cuando no son NULL)
- `factura.numero_factura` es `UNIQUE`; `factura.ncf` también (con NULL múltiples)
- `turno` es `UNIQUE (fecha, numero)` — el número reinicia cada día
- `detalle` lleva exactamente un `procedimiento_id` **o** un `producto_id`
- `medico.porcentaje_honorario` entre 0 y 100
- Todas las lecturas filtran `deleted_at IS NULL`

### Por qué se copian datos en la factura
`detalle.descripcion`, `detalle.precio_unitario`, `detalle.exento_itbis`,
`factura.itbis_tasa` y `factura.honorario_porcentaje` se **copian al emitir**
en vez de leerse del catálogo. Un comprobante ya entregado no puede cambiar
porque alguien editó un precio, una exención o un porcentaje seis meses
después. Es la misma regla que rige en FAControl y en POS-500.

---

## 6. Roles y permisos

**A diferencia de PrestControl, este proyecto SÍ tiene sistema de roles.** Los cuatro roles base son los del POS-400 original, adaptados:

| Rol | Módulos accesibles |
|---|---|
| **Admin** | Todos los módulos + gestión de usuarios + gestión de roles y permisos + Configuración completa |
| **Supervisor** | Operación completa de la clínica: cobro, pacientes, insumos, almacén, caducidad, cuadre de todos, reportes, comprobantes, médicos, procedimientos, citas y turnos. Sin configuración ni usuarios |
| **Cajero** | Cobra: Vender, Pacientes (crear/editar), Citas, Turnos, su propio cuadre y sus propios comprobantes |
| **Servicio** | Atiende: Sala de espera, Citas, Pacientes (crear/editar), Médicos y Procedimientos. **No cobra** — sin `vender`, `cuadre` ni `comprobantes` |

> **Revisión de Yuber (2026-08-14):** `Vendedor` y `Cajero` hacían lo mismo —en una clínica no hay
> piso de venta, hay un mostrador— así que quedó uno solo, y Cajero heredó de Vendedor la edición
> de pacientes. Entró `Servicio`, que es quien de verdad mueve la clínica: la recepcionista que da
> turnos, agenda citas y registra pacientes pero **no toca la caja**. Separar quién cobra de quién
> atiende es lo que hace que el cuadre del día signifique algo.
> Migración: `scripts/db/005_codigo_turno_y_roles.sql`.

> **Regla añadida por Yuber (2026-07-11):** el módulo **Configuración es EXCLUSIVO del rol Admin**.
> Para los demás roles no aparece en el sidebar. Esto reemplaza el reparto de cards por rol
> que aparecía en §8.4 — la pantalla completa es solo-Admin.

### Permisos individuales (override)

La tabla `usuario_permiso` permite dar permisos específicos a un usuario aunque su rol no los tenga (patrón del POS-400). Todo cambio en `usuario_permiso` queda registrado en `auditoria`.

### Aplicación en la UI
- El sidebar muestra solo los ítems permitidos según los permisos del `SesionActual`
- Cada ViewModel valida al inicio si el usuario tiene el permiso requerido
- Intentar navegar a una view sin permiso muestra diálogo "No tiene permisos para acceder"

### Aplicación en la BD
- Cada Service valida permisos antes de ejecutar operaciones sensibles (anular factura, eliminar producto, cambiar precio)
- Toda operación mutable queda en `auditoria` con `usuario_id`

---

## 7. Módulos del sistema

Los módulos son los del POS-400 modernizados. Cada uno tiene su View, ViewModel y Service correspondientes.

| # | Módulo | Origen | Notas |
|---|---|---|---|
| 0 | Login | PrestControl | Reutilizar patrón exacto (BCrypt + `SesionActual`) |
| 1 | Shell + Sidebar | PrestControl | Reutilizar `MainWindow`, filtrar ítems por permisos |
| 2 | Dashboard | Nuevo | KPIs de ventas: ventas del día, total del mes, productos por caducar, stock bajo, top vendedores |
| 3 | Vender (FormVentas) | POS-400 | Reescribir en WPF/MVVM. Búsqueda, carrito, ITBIS, impresión. **La selección de cliente es OPCIONAL** (feedback del cliente del POS-400: casi nunca hace falta); además puede desactivarse por completo en Configuración → sección K, quedando visible solo el código de compra |
| 4 | Clientes | POS-400 + PrestControl | Lista + ficha + CRUD (patrón visual PrestControl) |
| 5 | Productos | POS-400 | Lista + CRUD + gestión de caducidad |
| 6 | Almacén | POS-400 | Vista de stock con totales |
| 7 | Caducidad | POS-400 | Semáforo verde/amarillo/naranja/rojo por proximidad |
| 8 | Buscar Comprobante | POS-400 | Buscar por número, reimprimir |
| 9 | Cuadre de Caja | POS-400 | Por usuario, tiempo activo, detalle de facturas |
| 10 | Reportes por Fecha | POS-400 | Rango de fechas + totales |
| 11 | Admin de Usuarios | POS-400 | CRUD de usuarios + asignación de rol + override de permisos |
| 12 | **Configuración** | PrestControl + nuevo | **Ver sección 8** |
| 13 | **Almacén de Expedientes** | FAControl (018/026) | Los papeles del paciente: archivo en disco, ficha en BD, lista blanca de extensiones, ZIP. Permiso `expedientes`. **Leer §1.1 antes de tocarlo** |

### Cálculos y reglas fiscales
- ITBIS estándar: **18%** (configurable en Configuración → Impuestos)
- ITBIS aplica sobre el subtotal, no sobre cada línea individualmente redondeada
- Redondeo: `Math.Round(valor, 2, MidpointRounding.AwayFromZero)`
- El cambio se calcula como `efectivo_recibido - total`
- Documentar todas las fórmulas en `docs/ITBIS.md`

---

## 8. Módulo Configuración (detallado)

Esta es la sección más importante del proyecto según pedido del cliente. Se reutiliza la estructura de PrestControl y se agregan las funciones nuevas necesarias para un POS.

### 8.1. Secciones heredadas de PrestControl (adaptar tal cual)

**A) Apariencia**
- Tamaño del texto: Pequeño / Mediano / Grande
- Enum `TamanoTexto` con `FactorEscala` 1.0 / 1.12 / 1.25
- El shell escala la UI cuando cambia el tamaño (evento `EscalaCambiada`)
- Persistido en `AjustesLocales` (JSON local, no en BD)

**B) Cambio de contraseña**
- 3 campos: contraseña actual, nueva, confirmar
- Validación con BCrypt via `AuthService.CambiarPasswordAsync`
- Aplicable al usuario activo (`SesionActual`)
- **Diferencia con PrestControl:** cualquier usuario puede cambiar su propia contraseña; solo Admin puede resetear la de otros usuarios (esa función va en Admin de Usuarios, no aquí)

**C) Respaldo de la base de datos**
- Botón "Respaldar ahora" → `SaveFileDialog` → `mysqldump` → archivo `.sql`
- Botón "Restaurar desde archivo" → **doble confirmación** obligatoria (patrón exacto de PrestControl)
- Reutilizar `RespaldoService` de PrestControl con cambio de nombre de BD

**D) Exportación a Excel**
- Manual: botón "Exportar ahora" genera `.xlsx` con todas las tablas
- Automática: checkbox + campo "cada N días" + selector de carpeta
- Persistido en `AjustesLocales` (patrón exacto de PrestControl)
- **Diferencia con PrestControl:** las tablas exportadas son diferentes (facturas, productos, cuadres, usuarios) pero el patrón del servicio es idéntico

### 8.2. Sección adaptada

**E) Aviso al iniciar sesión** (adaptación de "Aviso de vencimientos" de PrestControl)

En vez de avisar de préstamos vencidos, avisa de dos cosas críticas del inventario:

1. **Productos por caducar en los próximos 30 días** (configurable en la misma pantalla)
2. **Productos con stock bajo** (umbral configurable, por defecto 10 unidades)

Estructura de UI:
- Checkbox "Avisar al iniciar sobre productos por caducar"
- Slider o input numérico: "Avisar cuando falten X días para caducidad" (default 30)
- Checkbox "Avisar al iniciar sobre productos con stock bajo"
- Input numérico: "Considerar stock bajo cuando queden X unidades o menos" (default 10)
- Botón "Restablecer productos silenciados" (lista `AvisoProductosSilenciados: List<long>` en `AjustesLocales`)

### 8.3. Secciones NUEVAS (específicas para POS)

**F) Datos del negocio**
Requerido para que el ticket impreso muestre la información legal correcta.
- Nombre comercial del negocio
- RNC (Registro Nacional del Contribuyente)
- Dirección
- Teléfono
- Email (opcional)
- Ruta del logo (`ImagePicker` → `OpenFileDialog` filtrando `.png/.jpg`)
- Vista previa del logo dentro del card
- **Persistido en tabla `configuracion_negocio` en BD** (no en `AjustesLocales`), porque es dato compartido entre PCs si algún día se migra

Solo accesible por rol **Admin**.

**G) Impresión y ticket**
- Impresora predeterminada (dropdown con impresoras instaladas del sistema)
- Tamaño de papel: `80mm` / `Carta` (radio buttons)
- Número de copias del ticket (numérico 1-3)
- Encabezado personalizable (textarea, texto libre que aparece arriba del ticket)
- Pie de ticket personalizable (textarea, "Gracias por su compra", políticas, etc.)
- Botón "Imprimir ticket de prueba" (imprime un ticket dummy para verificar configuración)
- Persistido en `AjustesLocales` (es preferencia por PC — cada terminal puede tener su impresora)

**H) Cálculos e impuestos**
- ITBIS aplicado por defecto: numérico decimal (default 18.00)
- Redondeo: dropdown (`Al centavo más cercano` / `Al peso más cercano` / `Hacia arriba`)
- Símbolo de moneda: text input (default `RD$`)
- Formato de miles: dropdown (`1,234.56` / `1.234,56`)
- **Persistido en tabla `configuracion_negocio`** (dato de negocio, no de PC)

Solo accesible por rol **Admin**.

**I) Numeración de comprobantes**
- Prefijo de factura (default `F-`)
- Siguiente número de factura (numérico, editable con confirmación por ser sensible)
- Formato: dropdown (`F-0001` / `F-2026-0001` / etc.)
- Nota informativa: "El sistema asigna números secuencialmente. Modificarlo puede crear duplicados. Usar con precaución."
- **Persistido en tabla `configuracion_negocio`**

Solo accesible por rol **Admin**.

**J) Gestión de sesión**
- Cerrar sesión automáticamente tras N minutos de inactividad (default: 30, `0` = nunca)
- Requerir contraseña para operaciones sensibles (anular factura, cambiar precios)
- Persistido en `AjustesLocales`

**K) Ventas y facturación** (añadida por Yuber, 2026-07-11)
- Checkbox "Mostrar selección de cliente en la pantalla de Vender" (default: activado)
- Al desactivarlo, la pantalla Vender oculta el selector de cliente y muestra únicamente el código de compra; todas las facturas se emiten con `cliente_id = NULL` (consumidor final)
- Aunque esté activado, seleccionar cliente NUNCA es obligatorio para facturar
- **Persistido en tabla `configuracion_negocio`** (comportamiento del negocio, no de la PC)

### 8.4. Orden de las secciones en la UI


Se muestran como cards apiladas verticalmente en el `ScrollViewer`, en este orden:

1. Apariencia
2. Aviso al iniciar
3. Gestión de sesión
4. Cambio de contraseña
5. Impresión y ticket (preferencia por terminal)
6. Ventas y facturación (sección K)
7. Datos del negocio
8. Cálculos e impuestos
9. Numeración de comprobantes
10. Respaldo de base de datos
11. Exportación a Excel

**El módulo Configuración completo es EXCLUSIVO del rol Admin** (regla de Yuber, 2026-07-11):
no aparece en el sidebar de los demás roles. El cambio de contraseña del propio usuario
sigue disponible para todos los roles, pero se reubica fuera de Configuración
(menú de usuario del shell o card propia) — decidir en Fase 6 y anotar en BLOCKERS.md.

### 8.5. Persistencia

Dos capas distintas según el tipo de dato:

**`AjustesLocales` (JSON local por PC)** — `ajustes.json` junto al ejecutable:
- Tamaño de texto
- Configuración de aviso al iniciar y productos silenciados
- Impresora predeterminada, tamaño de papel, copias
- Encabezado y pie del ticket
- Configuración de exportación automática
- Timeout de sesión inactiva

**Tabla `configuracion_negocio` (BD)** — un solo registro:
- Nombre del negocio, RNC, dirección, teléfono, email, ruta del logo
- ITBIS por defecto, redondeo, moneda, formato de miles
- Prefijo de factura, siguiente número, formato
- Mostrar selección de cliente en Vender (sección K)

Cargar `configuracion_negocio` una vez al iniciar la app y exponer como singleton `ConfiguracionNegocio` (patrón similar a `SesionActual`).

---

## 9. Reglas específicas del proyecto

Hereda todas las reglas de PrestControl (decimal para dinero, UTC en BD, soft deletes, BCrypt, auditoría, parametrized queries). Adiciones específicas de MED-100:

1. **ITBIS calculado sobre subtotal**, no acumulando redondeos por línea. Documentar en `docs/ITBIS.md`.
2. **Números de factura secuenciales y atómicos.** Al emitir factura, `SELECT ... FOR UPDATE` sobre `configuracion_negocio.siguiente_numero`, incrementar en la misma transacción.
3. **Nunca eliminar facturas emitidas.** Solo se pueden anular (estado `anulada`), lo que las mantiene en el historial. Anular requiere permiso especial y queda en `auditoria`.
4. **Stock decrementado dentro de la transacción de venta.** Nunca fuera. Si falla la impresión, la venta ya está persistida y se puede reimprimir.
5. **Validación de stock antes de agregar al carrito.** Si otro cajero está vendiendo el mismo producto simultáneamente, revalidar al facturar.
6. **Impresión no bloquea la venta.** Si la impresora falla, la venta se marca como completada y aparece un aviso "Ticket pendiente de impresión" con botón para reintentar.
7. **Tiempo activo por usuario.** Al login se inicia contador; al logout o cierre de app se guarda en tabla `sesion`. Se usa en Cuadre de Caja.
8. **El cuadre de caja no se puede modificar una vez cerrado.** Es solo lectura tras generarlo.

---

## 10. Fases de desarrollo sugeridas

Estructura similar a PrestControl, adaptada al alcance del POS.

### Fase 1 — Cimientos (semana 1)
- Clonar estructura de PrestControl como base
- Renombrar namespaces a `MED100.*`
- Adaptar `MainWindow` y sidebar a los módulos del POS
- Crear `med100_db` con script `001_create_schema.sql`
- Roles y permisos base (script `002_seed_data.sql`)
- Login funcional con BCrypt (reutilizando de PrestControl)
- `SesionActual` con soporte de rol y permisos
- Aplicación del `DESIGN.md`

### Fase 2 — Datos maestros (semana 2)
- Módulo Clientes (CRUD)
- Módulo Productos (CRUD con caducidad)
- Módulo Almacén (vista de stock)
- Módulo Caducidad (con semáforo)

### Fase 3 — Ventas y facturación (semanas 3-4)
- Módulo Vender: búsqueda, carrito, validación de stock, ITBIS
- Impresión de ticket 80mm (reutilizar patrón de POS-400, adaptar a WPF)
- Emisión atómica de números de factura
- `docs/ITBIS.md` con la matemática

### Fase 4 — Operaciones (semana 5)
- Módulo Buscar Comprobante (con reimpresión)
- Módulo Cuadre de Caja
- Anulación de facturas con permiso

### Fase 5 — Analítica (semana 6)
- Dashboard con KPIs
- Módulo Reportes por Fecha
- Gráfico de tendencia con `LiveChartsCore`

### Fase 6 — Administración y Configuración (semana 7)
- Módulo Admin de Usuarios (CRUD + asignación de rol + override de permisos)
- **Módulo Configuración COMPLETO** (todas las 10 secciones descritas en la sección 8)
- Tabla `configuracion_negocio` + carga singleton al iniciar

### Fase 7 — Empaquetado (semana 8)
- Instalador con Inno Setup (reutilizar el de PrestControl)
- Script de instalación de MySQL Community
- `docs/INSTALL.md` y `docs/MANUAL.md`

---

## 11. Testing

Cobertura mínima:
- **VentaService** (cálculo de ITBIS, stock, número de factura): 90%+
- **AuthService** (login, cambio de contraseña, permisos): 90%+
- **PermisoValidator** (matriz rol → permisos): 100%
- **AlertaCaducidadCalculator** (semáforo): 100%
- **Repositorios:** integración contra BD `med100_test`

Casos obligatorios para `VentaService`:
- Venta simple con 1 producto y efectivo exacto
- Venta con múltiples productos y ITBIS calculado sobre subtotal
- Venta con stock insuficiente (debe fallar y no persistir nada)
- Emisión concurrente de dos ventas simultáneas (debe generar números distintos)
- Anulación de factura (verificar que queda en `auditoria`)

### Los XAML no se prueban solos: correr el verificador

```powershell
python scripts/verificar_recursos_xaml.py
```

WPF resuelve `StaticResource` / `DynamicResource` **en tiempo de ejecución**: una
clave mal escrita compila sin una sola advertencia y revienta recién cuando
alguien abre esa pantalla. Ya pasó dos veces (el `ElementStyle` de Médicos el
2026-08-12 y un `BasedOn` a un estilo implícito inexistente el 2026-08-14). El
script tarda un segundo; correrlo antes de entregar es obligatorio.

Lo que el script **no** cubre y sigue exigiendo abrir la pantalla: que el
`TargetType` de un estilo case con el elemento al que se aplica, y los estilos
implícitos (`{x:Type …}`).

---

## 12. Anti-patrones prohibidos

Todos los de PrestControl más:

- ❌ **Copiar código Windows Forms directamente del POS-400.** Solo se replican patrones lógicos y queries SQL.
- ❌ **Guardar contraseñas en texto plano.** BCrypt sin excepción, incluso en seed data.
- ❌ **Calcular ITBIS línea por línea acumulando redondeos.** Siempre sobre subtotal.
- ❌ **Emitir factura sin transacción.** El número + inserción + decremento de stock son atómicos.
- ❌ **Permitir eliminar facturas.** Solo anular.
- ❌ **Bloquear la venta por fallo de impresión.** La venta se completa; la impresión se puede reintentar.
- ❌ **Mostrar módulos en el sidebar sin verificar permisos.** El filtrado es obligatorio.
- ❌ **Hardcodear el ITBIS del 18%** en el código. Leer de `ConfiguracionNegocio` (configurable en Configuración → Cálculos e impuestos).
- ❌ **Hardcodear datos del negocio** (nombre, RNC, dirección) en el ticket. Leer de `ConfiguracionNegocio`.

---

## 13. Entregables por fase

Idénticos a PrestControl:
1. Commit descriptivo: `[Fase N] Descripción corta`
2. Actualización de `TODO.md`
3. Actualización de `CHANGELOG.md`
4. Screenshots en `docs/screenshots/`
5. Notas de bloqueos en `BLOCKERS.md`

---

## 14. Instrucciones de arranque para Claude Code

Al iniciar cada sesión:

1. Leer este `CLAUDE.md` completo
2. Leer `docs/DESIGN.md`
3. Verificar y leer PrestControl en el workspace — es la referencia arquitectural principal
4. Verificar y leer POS-400 en el workspace — es la referencia de lógica de negocio y esquema SQL
5. Verificar existencia de `TODO.md`, `BLOCKERS.md`, `CHANGELOG.md`. Si no, crearlos.
6. Verificar la última fase completada mirando `CHANGELOG.md`
7. Continuar la siguiente tarea pendiente en `TODO.md` respetando el orden de fases

**Regla de reutilización:** antes de escribir cualquier archivo, verificar si su equivalente existe en PrestControl. Si existe, copiar y adaptar en vez de escribir desde cero. Esto aplica especialmente a:
- Estilos WPF completos (`Styles/Estilos.xaml`)
- `MainWindow.xaml` y su sidebar
- `LoginView.xaml`
- Converters (`MoneyConverter`, `DateConverter`, `InverseBooleanConverter`, `IgualdadConverter`)
- `IDialogService` y su implementación
- `AjustesLocales` (adaptando propiedades al POS)
- `SesionActual`
- `AuthService` (adaptando para soporte multi-usuario con roles)
- `RespaldoService` y `ExportacionService` (adaptando queries)
- `AuditoriaService`
- Instalador Inno Setup

**Regla de ambigüedad:** ante duda entre "hacer como PrestControl" o "hacer como POS-400", **preferir PrestControl** para todo lo arquitectural y visual, y **preferir POS-400** para todo lo relacionado con lógica de venta, ITBIS y flujo de facturación.

**Frente a decisiones técnicas nuevas:** documentar en `BLOCKERS.md` y consultar al desarrollador antes de asumir.

---

## 15. Contacto y ownership

- **Desarrollador:** Yuber Santana
- **Cliente final:** A definir contractualmente
- **Licencia:** Software propietario, entregado con contrato de mantenimiento
