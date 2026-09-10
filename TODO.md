# TODO — MED-100 (producto: MediControl)

> Estado vivo del proyecto. Actualizar al cierre de cada bloque de trabajo.

## Versión 1.2.0 (2026-09-06) — HECHA, falta prueba manual

Decisiones que Yuber cerró el 2026-09-06 y quedaron implementadas:

- [x] **Paleta médica**: los 3 colores del logo de la clínica en `Colores.xaml`
      (`#0396D4` primario, `#29DAE2` acento, `#100062` navy)
- [x] **Nombre del producto: MediControl**. Solo la capa visible, vía
      `AppInfo.Nombre`. NO se tocó AppId, licencia.dat, namespaces ni la base
- [x] **NCF como PrestControl**: `ncf_secuencia` (011) + sección en Configuración
      + asignación automática al cobrar, atómica dentro de la transacción
- [x] **Fiados** (012): `factura.abonado_inicial`, `fecha_compromiso`,
      tabla `factura_abono`, pantalla Fiados con semáforo, permiso `fiados`,
      cuadre de caja corregido, saldo en el ticket
- [x] **Fiados en el aviso automático** por correo, junto a la caducidad
- [x] **Medicamentos indicados** (013): `indicacion` + `indicacion_medicamento`,
      pantalla por día, permiso `indicaciones`, auditoría, soft delete
- [x] **AppMutex** `Global\MediControl.App.Instancia` en la app y en los dos .iss
- [x] **Versión en la pantalla de inicio** + `Version`/`FileVersion` en el .csproj
- [x] **Instalador 1.2.0** (`MediControl_Setup_1.2.0.exe`, 282 MB)
- [x] **ACTUALIZADOR** (`MediControl_Update_1.2.0.exe`, 61 MB) con las tres
      defensas del incidente de FAControl: AppMutex, CloseApplications y
      comprobación de versión post-install
- [x] Build Release sin warnings · verificador XAML OK · 343 tests pasando

### Pruebas manuales de Yuber (1.2.0)
- [ ] Cargar una secuencia NCF en Configuración, cobrar dejando el NCF vacío y
      ver que asigna el siguiente; después escribir uno a mano y ver que la
      secuencia se corre sola
- [ ] Poner una secuencia con fin de rango ya alcanzado y confirmar que el cobro
      se detiene con mensaje claro en vez de emitir sin comprobante
- [ ] Cobrar dejando saldo: ver el saldo en el ticket impreso, que aparezca en
      Fiados con su semáforo, y que el cuadre del día NO sume lo fiado
- [ ] Cobrar un abono al día siguiente y confirmar que entra en el cuadre de ESE
      día, no en el de la factura
- [ ] Intentar fiar con un usuario sin el permiso `fiados` y ver que no puede
- [ ] Cargar medicamentos indicados, verlos en el día, y confirmar que un usuario
      sin el permiso `indicaciones` no ve la pantalla
- [ ] **Probar el actualizador con la app ABIERTA**: tiene que negarse a instalar
- [ ] Probar el actualizador con la app cerrada y confirmar "Versión 1.2.0" abajo
      en la pantalla de inicio
- [ ] Probar el actualizador en una PC sin MediControl instalado: debe negarse
- [ ] Instalar el 1.2.0 completo en una PC limpia siguiendo INSTALL.md

### Antes de entregarlo al cliente
- [x] Nombre confirmado por la clinica el 2026-09-07: **Odonto Unión**
- [x] MySQL **offline** (566 MB) puesto en `installer/prerequisitos/`, y el
      `.iss` apuntando a el. El LEEME explica por que no se vuelve al web
- [x] `docs/MANUAL.md` con el nombre nuevo y las secciones de Fiados y
      Medicamentos indicados

## Ronda del 2026-09-10 — el producto se llama Odonto Unión (1.3.0)

- [x] `AppInfo.Nombre` -> Odonto Unión; version 1.3.0 en AppInfo, csproj y los dos .iss
- [x] Instaladores renombrados a `OdontoUnion_Setup_` / `OdontoUnion_Update_`
      (sin tilde en el nombre del archivo, a proposito)
- [x] El MUTEX NO se renombro: es lo que mira el actualizador para negarse a
      correr con la app abierta. Renombrarlo repetiria el fallo de FAControl
      del 5-sep. Documentado en AppInfo y en el CHANGELOG
- [x] 10 textos en voseo corregidos
- [x] 343 tests en verde, verificador XAML OK

### Pendiente de probar a mano
- [ ] Abrir la app y confirmar que dice "Odonto Unión" y "Version 1.3.0" abajo
- [ ] Correr `OdontoUnion_Update_1.3.0.exe` **con la app abierta**: tiene que
      negarse. Es la prueba que valida que el mutex viejo sigue sirviendo
- [ ] Instalar en una PC limpia y confirmar que MySQL entra **sin internet**

### Lo que le sigue faltando al manual
- [ ] **7 pantallas sin documentar.** El manual venia del POS y nunca se
      adapto a la clinica: no menciona Medicos, Procedimientos, Citas, Turnos,
      Expedientes ni Panel clinico. Fiados y Medicamentos ya se agregaron.
      Es trabajo de redaccion, no de codigo; decidir con Yuber si entra antes
      de imprimir el manual para la clinica

## Fase 1 — Cimientos (COMPLETA salvo prueba manual)
- [x] Estructura de solución (MED100.sln + 8 proyectos src + 2 tests, clonada de PrestControl)
- [x] Esquema `med100_db` (`001_create_schema.sql`) adaptado de BDPOS-400.sql: DECIMAL(15,2), BCrypt, UTC, soft deletes, auditoría, `configuracion_negocio` (incluye `mostrar_cliente_en_venta` y `factura_siguiente` para numeración atómica) — probado en MySQL local, triggers verificados
- [x] Seed de roles/permisos (`002_seed_data.sql`) — Admin 15 / Supervisor 13 / Cajero 4 / Vendedor 3; sin usuarios seed (wizard de cuenta inicial con BCrypt)
- [x] Login con BCrypt (wizard inicial crea al Admin) + `SesionActual` con rol y permisos + rate-limiting
- [x] `MainWindow` + sidebar de 11 módulos filtrado por permisos (Visibility por PuedeVer*; Navegar revalida)
- [x] Estilos WPF de PrestControl (Colores/Tipografia/Controles + 4 converters; pills de préstamos retirados — los de POS llegan en Fase 2)
- [x] AuditoriaService funcionando (login/logout auditados)
- [x] VerificadorBaseDatos desde el día 1 (ejecuta 001+002 embebidos, triggers como bloques separados)
- [ ] Prueba manual de Yuber: wizard → login → sidebar según rol

## Fase 2 — Datos maestros (COMPLETA salvo prueba manual)
- [x] Clientes: lista con búsqueda + form nuevo/editar + eliminar (soft) — botones de edición solo con permiso clientes_editar
- [x] Productos: lista con búsqueda y filtros (stock bajo/con caducidad/sin stock) + form + eliminar (soft); cambios de precio/stock auditados con antes→después
- [x] Almacén: 4 tarjetas de totales (SQL) + tabla solo lectura con valor en stock
- [x] Caducidad: semáforo mensual del POS-400 mapeado a 4 colores (CalculadoraCaducidad, 100% ramas testeadas) + contadores de críticos/próximos
- [ ] Prueba manual de Yuber (crear cliente/producto, ver semáforo)
- [ ] Ficha de cliente con historial de compras — llega con Facturación (Fase 3/4)

## Fase 3 — Ventas y facturación (COMPLETA salvo prueba manual)
- [x] VentaService: totales puros (ITBIS sobre subtotal, redondeo centavo/peso/arriba), emisión atómica (número FOR UPDATE + factura + detalles + stock validado + auditoría en 1 transacción)
- [x] Pantalla Vender: escaneo por código (agrega directo), búsqueda por nombre, carrito con +/−, cliente OPCIONAL (combo "Consumidor final") y ocultable vía configuracion_negocio, métodos de pago, efectivo/cambio en vivo
- [x] Ticket 80mm: TicketVisualFactory (datos del negocio desde BD, encabezado/pie de AjustesLocales) + TicketWindow con vista previa e impresión reintentable (la venta nunca depende de la impresora)
- [x] docs/ITBIS.md con la matemática completa
- [ ] Prueba manual de Yuber: vender con y sin cliente, escanear código, ticket

## Ajustes pedidos por Yuber (2026-07-12) — HECHOS
- [x] Al cobrar, el ticket se IMPRIME SOLO (sin diálogo). Vista previa opcional en Configuración (OFF por defecto); si la impresora falla, se abre la vista previa para reintentar
- [x] Espacio entre el nombre del producto y el "stock: N" en el buscador de Vender
- [x] Nombre del negocio y RNC (opcional) editables en Configuración → se reflejan en el ticket
- [x] ITBIS activable/desactivable: al apagarlo desaparece de Vender y del ticket, y las facturas se emiten con itbis=0 e itbis_tasa=0
- [x] Pantalla Configuración (solo Admin) creada con 4 secciones: Datos del negocio · Cálculos e impuestos · Ventas y facturación · Impresión y ticket

## Fase 4 — Operaciones (COMPLETA salvo prueba manual)
- [x] Buscar Comprobante: filtros por número/cliente y rango de fechas (por DÍA DE NEGOCIO, UTC-4); un Cajero solo ve SUS facturas (el service fuerza el alcance, no la UI), con `comprobantes_todos` se ven todas
- [x] Reimpresión: mismo ticket que el original (FacturaService.AVentaResultado → TicketVisualFactory); al reimprimir siempre se muestra vista previa
- [x] Anulación (permiso `facturas_anular`): estado='anulada' + motivo obligatorio, DEVUELVE el stock, auditada con acción 'anular'; imposible anular dos veces (el UPDATE exige estado='emitida'); la factura NUNCA se borra
- [x] Cuadre de Caja: totales por método de pago (SQL), tiempo activo desde `sesion`, anuladas informadas aparte sin sumar; cierre inmutable (UNIQUE usuario+fecha); un Cajero solo ve/cierra su turno
- [x] IDialogService.PedirTexto + PedirTextoWindow (motivo de anulación)
- [ ] Prueba manual de Yuber: vender → buscar comprobante → reimprimir → anular → ver cuadre

## Fase 5 — Analítica (COMPLETA salvo prueba manual)
- [x] Panel: KPIs (ventas de hoy, ventas del mes con variación vs. mes anterior, ticket promedio, alertas de inventario), gráfico de barras de ventas diarias, top cajeros y top productos del mes
- [x] Reportes por fecha: atajos (Hoy/Ayer/Esta semana/Este mes/Mes pasado/Personalizado), KPIs del período (total, facturas, ticket promedio, ITBIS cobrado), desglose por método de pago, tendencia diaria, top productos y ventas por cajero
- [x] AnaliticaRepository: TODO por día de negocio (UTC-4) y excluyendo anuladas (que se informan aparte); totales calculados en SQL
- [x] Umbrales de "por caducar" y "stock bajo" leídos de AjustesLocales (no hardcodeados)
- [ ] Prueba manual de Yuber: ver panel con ventas del día y generar un reporte del mes
- [ ] Exportar reporte a Excel → llega en Fase 6 con ExportacionService

## Fase 6 — Administración y Configuración (COMPLETA salvo prueba manual)
- [x] Admin de Usuarios (solo Admin): crear empleados, restablecer contraseñas sin saber la anterior, activar/desactivar, cambiar rol
- [x] Permisos por usuario editables desde la misma pantalla: el rol siembra los permisos por defecto (triggers del POS-400) y el Admin marca/desmarca para limitar quién edita o elimina productos y clientes
- [x] Protecciones: el Admin no puede desactivarse ni quitarse sus permisos de administración
- [x] Configuración: Apariencia (tamaño de texto — escala TODA la UI incluidos los encabezados de tabla), Respaldo/Restauración (mysqldump, para migrar de equipo), Exportación a Excel manual y automática (idéntica a PrestControl), Cierre automático de caja a una hora
- [x] Tabla `configuracion_negocio` + singleton (ya estaba desde Fase 3)
- [ ] BLOCKERS #1 resuelto: solo el Admin cambia contraseñas (regla de Yuber 2026-07-12) — ya no hace falta "cambiar mi contraseña" para no-Admin
- [ ] Prueba manual de Yuber: crear un cajero, limitarle permisos, entrar con él

## Extras pedidos por Yuber (2026-07-12) — HECHOS
- [x] ComboBox y DatePicker modernos (texto centrado, bordes redondeados)
- [x] Cuadre GENERAL por defecto (desglose de todos los cajeros) + cerrar todos los turnos de una vez
- [x] Cierre de caja: vista previa SIEMPRE antes de imprimir + elegir tamaño (ticket 80mm u hoja carta)
- [x] Cierre automático a la hora que elija el Admin (activable en Configuración)

## Fase 7 — Empaquetado (COMPLETA)
- [x] Ícono de la app: una TIENDA (toldo a rayas, escaparate y puerta con arco), 7 tamaños 16–256px; en el exe, las ventanas y el instalador
- [x] Instalador Inno Setup 6 self-contained (60MB, no requiere instalar .NET): verifica que MySQL exista y avisa con el enlace si falta; App.config protegido en actualizaciones
- [x] La BD se crea SOLA en el primer arranque (esquema + roles + permisos) — no hay que ejecutar scripts a mano
- [x] scripts/db: 001 esquema, 002 seed, 003 usuario dedicado, 004 reparar acentos (todos con SET NAMES utf8mb4)
- [x] docs/INSTALL.md (técnico) y docs/MANUAL.md (usuario final, lenguaje sencillo)
- [ ] Prueba de Yuber: instalar en una PC limpia siguiendo INSTALL.md

## Reglas nuevas de Yuber (2026-07-11) — ya integradas en CLAUDE.md
- Cliente opcional en Vender + desactivable en Configuración (sección K, `configuracion_negocio`); `factura.cliente_id` NULLable
- Configuración = solo Admin (sin cards parciales para otros roles)

---

# MED-100 — módulos de clínica (sobre la base heredada del POS-500)

> Todo lo de arriba llegó funcionando del POS-500 y no se reescribe. Lo que
> sigue es lo que pidió el cliente el 2026-08-08 / 08-10.

## Médicos (HECHO — falta prueba manual)
- [x] Tabla `medico` (info simple + exequátur + `porcentaje_honorario` vigente) y `medico_horario`
- [x] Horarios editables con varios tramos por día (mañana/tarde) — `DisponibilidadMedicos` dice quién atiende AHORA
- [x] Validación de solapes y de rangos invertidos
- [ ] Prueba manual de Yuber: crear un médico con horario partido y ver la columna "Ahora"

## Procedimientos (HECHO — falta prueba manual)
- [x] Tarifario con precio, `duracion_minutos` (alimenta la agenda) y `exento_itbis` (por defecto exento: salud)
- [x] Un procedimiento ya facturado o con citas NO se borra, se desactiva
- [ ] Prueba manual de Yuber

## Pacientes (HECHO — falta prueba manual)
- [x] Correo (por acá sale el recordatorio de cita), fecha de nacimiento, sexo y procedencia
- [x] Edad calculada en vivo — años / meses / días, porque "0 años" no le sirve a la recepción
- [x] Procedencia ("registro de proveniento"): tipo cerrado + nombre libre con autocompletado; se reutiliza si existe y se crea si es nuevo, sin salir del formulario
- [x] Validación de correo y de fecha de nacimiento (ni futura ni de hace 130 años)
- [x] Lista con edad, sexo, correo (apagado si falta) y quién lo refirió; se busca también por correo y por procedencia
- [ ] Prueba manual de Yuber: registrar un paciente con procedencia nueva y ver que se reutiliza en el siguiente
- [ ] Ficha del paciente con su historial de citas y facturas — llega con Facturación

## Citas (HECHO — falta prueba manual)
- [x] Agenda del día con navegación (◀ Hoy ▶), filtro por médico y estado a la vista
- [x] `AgendaMedico`: la cita tiene que caber ENTERA en un tramo del médico (no "sigue" después del almuerzo) y no pisarse con otra
- [x] Se OFRECEN los huecos libres en vez de hacer adivinar la hora; grilla de 15 min; no aparecen horas ya pasadas
- [x] La duración se sugiere sola desde el tarifario del procedimiento elegido
- [x] Una cita cancelada o "no asistió" LIBERA el hueco; las transiciones de estado son solo las que tienen sentido
- [x] Recordatorio por correo al paciente: `recordatorio_enviado_at` se marca DESPUÉS de que el envío salió bien y POR CITA (si falla uno, la tanda sigue)
- [x] Interruptor y horas de anticipación en Configuración → Recordatorios por correo, con envío a pedido
- [x] Hora local de RD en toda la pantalla; la conversión a UTC vive solo en `CitaRepository`
- [ ] Prueba manual de Yuber: agendar en el horario partido, mover una cita, cancelar y ver que el hueco se libera
- [ ] Cobrar una cita desde la agenda (`cita.factura_id`) — llega con Facturación

## Turnos de sala (HECHO — falta prueba manual)
- [x] Tablero de la sala: número LLAMANDO en grande, conteo por estado y minutos de espera por persona
- [x] El turno se puede dar EN BLANCO (sin paciente ni médico) y asignarle el paciente después: se entrega en la puerta
- [x] Numeración que reinicia cada día y no se repite — probado con 10 turnos en paralelo contra MySQL real
- [x] Papelito 80mm con el número enorme, en su propia factory (`TurnoVisualFactory`), SEPARADO del recibo
- [x] Imprime directo en el mostrador (configurable) y con vista previa al reimprimir; si falla la impresora el turno NO se pierde
- [x] Llamar al siguiente o a uno puntual; cerrar como atendido o ausente (son distintos: ausente = se cansó de esperar)
- [x] `llamado_at` se sella la PRIMERA vez y no se pisa: es de donde sale cuánto esperó la gente
- [x] Prefijo del turno e impresión automática en Configuración → Sala de espera
- [ ] Prueba manual de Yuber: dar turnos, llamar, imprimir, probar el prefijo
- [ ] Pantalla grande para la sala (segundo monitor) — no pedida todavía, anotar si el cliente la quiere

### Defecto encontrado y corregido en el camino
La primera versión reservaba el número con `SELECT … FOR UPDATE` + `INSERT` dentro de una
transacción abierta. Era correcta en unicidad pero **se deadlockeaba** con varias
recepcionistas a la vez: el `FOR UPDATE` sobre un rango vacío toma un gap lock compartido,
varias transacciones lo consiguen y todas se traban al insertar. Reproducido con el test de
10 turnos en paralelo. Ahora es un solo `INSERT … SELECT MAX(numero)+1` con reintento, y la
unicidad la garantiza `uq_turno_fecha_numero`, no el bloqueo.

## Pedidos de la clinica del 2026-08-28 (screenshot en Claude Active)

- [x] **Deshacer un estado de cita puesto por error.** "si uno elige algo por
      error o se arrepiente. No puede cancelar o darle para atras". Boton
      "Deshacer: volver a programada" + confirmacion antes de los estados
      finales. `RevertirEstadoAsync` revalida el hueco (una cancelada libera su
      lugar) y se niega si la cita ya se cobro. 11 tests nuevos
- [x] **Las citas solo de hoy en adelante.** La regla ya estaba en
      `AgendaMedico.Validar`; lo que faltaba era que el FORMULARIO no dejara
      llegar hasta ahi con un mensaje que culpaba al medico. El calendario de la
      agenda (el de arriba) sigue yendo hacia atras a proposito
- [x] **"Cancelar cobro"** en la pantalla de cobro: se podia quitar linea por
      linea pero no soltar el cobro entero ni la cita traida de la agenda
- [x] **"Vender" pasa a "Cobrar"** (pedido del 27). Solo el rotulo: el enum
      `Pagina.Vender` y el permiso `vender` quedan igual

### Pendiente de probar a mano
- [ ] Marcar una cita como "No asistio", deshacerla y volver a marcarla bien
- [ ] Cancelar una cita, darle el hueco a otro paciente, e intentar deshacer la
      primera: tiene que negarse diciendo con quien choca
- [ ] Abrir "Nueva cita" parado en la agenda de la semana pasada y ver que el
      dia arranca en hoy
- [ ] Cobrar desde una cita, darle "Cancelar cobro", y confirmar que la cita
      sigue en la agenda sin cobrar

## Pedidos de la clinica del 2026-08-27 (screenshots en Claude Active)

- [x] **Rebajar el precio de una linea al cobrar.** Precio editable por linea, NO
      descuento global: un descuento al final obliga a decidir si va antes o
      despues del ITBIS y como se reparte entre exento y gravado, y nadie lo
      decidio. ITBIS y honorario del medico salen de lo que se COBRA.
      `detalle.precio_catalogo` congela el precio de lista para documentar la
      rebaja y que la reimpresion salga igual al papel original. Permiso nuevo
      `precio_editar` (Admin y Supervisor) — CONFIRMAR si el Cajero tambien
- [x] **Fecha retroactiva de consulta** (`cliente.ultima_visita_previa`). Es un
      PISO: si hay actividad real posterior, gana la real. Sin esto, el aviso de
      inactivos del 25 no servia hasta dentro de 6 meses de uso
- [x] **Agendar cita desde la ficha del paciente**
- [x] **Formato de la factura**: tipo de comprobante deducido del prefijo del
      NCF, linea de descuento, cajero al final, pie "Gracias por preferir
      nuestros servicios"
- [x] **Ver la factura antes de imprimir**: la pantalla ya existia; el ajuste
      estaba en OFF por default y por eso el cliente nunca la vio. Ahora ON

### Pendiente de probar a mano
- [ ] Rebajar una consulta y ver que el TOTAL, el ITBIS y el ticket la reflejan
- [ ] Reimprimir esa factura y ver que sigue mostrando el descuento
- [ ] Entrar como Cajero y ver que la columna de precio NO se puede editar
- [ ] Cargar la ultima visita de un paciente viejo y ver que aparece en el
      filtro "Los que dejaron de venir"
- [ ] Agendar desde la ficha de un paciente y ver que llega ya elegido
- [ ] Ver la factura antes de imprimir y decidir no imprimirla

### Falta para que esto le sirva al cliente
- [ ] **Cargar los datos del negocio en Configuracion** (Odonto Union SRL, RNC
      132149971, Aut. San Isidro KM 7 1/2 plaza Eva Maria Local 14,
      829-720-0174). El ticket los lee de ahi, no del codigo
- [ ] **Tildar "Mostrar vista previa" en Configuracion** en la PC de la clinica:
      el cambio de default solo alcanza a instalaciones nuevas

### Sin hacer, necesita decision
- [ ] **"Balance pendiente"** en la factura. Hoy no existe: MED-100 cobra el
      total, o lo reparte con la ARS, pero no deja una factura a medio pagar.
      Eso es un modulo de cuentas por cobrar (abonos, saldo, quien debe), no una
      linea del ticket. PREGUNTAR al cliente si de verdad fian, o si lo puso por
      costumbre del formato

## Pedidos de la clinica del 2026-08-25 (screenshots en Claude Active)

- [x] "Paciente" -> "Nombre" en turnos (columna, campo, boton y ticket impreso).
      Se uso Title Case como el resto de la app; CONFIRMAR con el cliente si lo
      queria en MAYUSCULAS
- [x] Aviso de pacientes que dejaron de venir: franja + filtro "Los que dejaron
      de venir" en Expedientes + marca en la fecha + ajuste del corte en
      Configuracion. La regla en `CalculadoraInactividad` (Services) con 8 tests
- [x] Fuga de handles del spooler en `ImprimirDirecto` (PrintQueue/PrintServer
      nunca se liberaban). Corregido tambien en FAControl, que comparte el codigo
- [x] `ExportadorPdf` fijaba 80mm; ahora deriva el tamano del visual
- [x] `AjustesLocales.TamanoPapel` estaba muerto: se quito
- [x] `docs/IMPRESORA-TERMICA.md` para diagnosticar la 2CONNET por AnyDesk

### Bloqueado por el cliente
- [ ] **Colores de la clinica.** La paleta ya esta extraida del .ai que mando
      (`#0396D4` primario, `#29DAE2` acento, `#100062` navy). Son 4 valores en
      `Themes/Colores.xaml`. FALTA que el cliente confirme — la pregunta
      "cuales colores desea?" nunca la contesto
- [ ] **Nombre nuevo del producto.** El .ai se llama "logo odonto union", pero
      eso es inferencia. FALTA confirmacion. Al renombrar NO tocar:
      `AppId` del instalador, `%ProgramData%\MED-100\licencia.dat`
      (AnclaLicencia), los namespaces `MED100.*` ni la base `med100_db`
- [ ] **Recetas y consentimiento informado.** Faltan las plantillas ("tengo que
      dartelo") Y una decision de alcance: imprimir una receta es contenido
      clinico, y CLAUDE.md §1.1 dice que MED-100 es recepcion, no el expediente
      medico. Decidir explicitamente antes de escribir codigo
- [ ] **Impresora 2CONNET.** Diagnostico escrito; falta ejecutarlo en la PC de
      la clinica y confirmar

### Pendiente de probar a mano
- [ ] Turnos: la columna y el campo dicen "Nombre", y el papelito impreso tambien
- [ ] Expedientes: la franja ambar aparece solo si hay inactivos
- [ ] El filtro "Los que dejaron de venir" deja la lista de a quien llamar
- [ ] Un paciente que nunca vino NO aparece como inactivo
- [ ] Cambiar el corte de meses en Configuracion y ver que la lista cambia
- [ ] Imprimir muchos tickets seguidos y confirmar que el spooler no se degrada

### Deuda conocida (no bloquea)
- [ ] El cierre de caja en Carta usa `PrintVisual`, que NO pagina: con 4+ cajeros
      el contenido puede pasarse de la hoja y recortarse sin avisar. Es el mismo
      defecto que fue el BLOCKER del pagare en FAControl. En una clinica con 1-3
      recepcionistas el riesgo es bajo; si aparece, migrar a FlowDocument como se
      hizo alla (`PrestamoDocumentFactory` es el modelo)

## Facturación de clínica (HECHO — falta prueba manual)
- [x] Una línea es un PROCEDIMIENTO o un INSUMO; el buscador ofrece los dos juntos
- [x] **ITBIS solo sobre la base gravada**: los servicios de salud van exentos y los insumos no
      (`CalculosClinica.CalcularTotales`). Es LA diferencia de fondo con el POS-500
- [x] La exención se copia a `detalle.exento_itbis`: cambiar el catálogo no reescribe facturas viejas
- [x] Stock: se descuenta SOLO en los insumos (un procedimiento no sale de un estante)
- [x] Honorario del médico sobre la base de PROCEDIMIENTOS, con el porcentaje **congelado**
      en la factura al emitirla — hay test de que subirle el % al médico no cambia lo ya facturado
- [x] Médico obligatorio si la factura tiene procedimientos; opcional si son puros insumos
- [x] ARS: `ars_cubierto` y `paciente_paga` separados, con autorización; el cubierto se recorta al total
- [x] **El efectivo se compara contra lo que paga el PACIENTE**, no contra el total
- [x] **El cuadre y los reportes por método suman `paciente_paga`**: lo que cubre el seguro
      no entra en la caja del día. El "total vendido" sigue siendo lo facturado — los dos números
      ahora difieren a propósito
- [x] Módulo de seguros apagable (`ars_activo`) + gestión del catálogo en Configuración;
      10 ARS dominicanas sembradas
- [x] NCF: se asigna a mano por factura, con `uq_factura_ncf` impidiendo repetirlo
- [x] Ticket con médico, NCF y el desglose "cubre el seguro / paga el paciente"
- [x] Cobrar una cita desde la agenda: llega con paciente, médico y procedimiento cargados,
      y al emitir la cita queda unida a la factura y marcada como atendida
- [ ] Prueba manual de Yuber: cobrar consulta + insumo, ver que el ITBIS salga solo del insumo;
      cobrar con ARS y revisar el cuadre del día
- [ ] 🟡 **Decisión pendiente del e-CF** (Ley 32-23, obligatorio desde el 15-nov-2026) — ver CLAUDE.md §1.4.
      Hoy el NCF se teclea a mano. Lo que falta cuando se decida: rangos de NCF por tipo (B01/B02/B04…),
      consumo atómico del siguiente número y reporte para la DGII. **La asignación manual sirve para
      operar, pero no es la solución definitiva.**

## Revisión de Yuber del 2026-08-14 (HECHO — falta prueba manual)

Repaso de toda la app con la pantalla delante. Lo que salió de ahí:

### Se arregló
- [x] **Los ComboBox mostraban el objeto crudo** ("OpcionSexo { Valor = , Etiqueta = … }") en Sexo,
      ¿Quién lo refirió?, Citas y Sala de espera. Causa: el template propio del ComboBox no
      propagaba `ItemTemplateSelector`, que es donde WPF deja lo que fabrica `DisplayMemberPath`.
      Se arregló el template Y se pasaron las nueve combos a `ItemTemplate`, que no depende de eso
- [x] **Citas**: los botones y el combo de médico se pisaban al achicar la ventana. La barra pasó
      a dos renglones — navegación del día arriba, filtro de médico abajo
- [x] **Pacientes**: el grid recortaba los datos. Ahora cada columna tiene mínimo y aparece el
      scroll horizontal cuando no caben (una columna estrella sin mínimo nunca desborda)
- [x] Icono de la app: cruz médica blanca sobre indigo, en 7 tamaños. El logo del menú también
      (era el "5" heredado del POS-500) y el título de la ventana ya no dice "Punto de venta"

### Se agregó
- [x] **Descripción de cada módulo en el menú**, al posar el mouse 2 segundos y visible 30
- [x] **Ficha del paciente ("Ver detalles")**: datos completos + historial de citas,
      procedimientos, facturas y turnos, con totales y última visita. Doble clic en la fila
      también la abre. Consultarla queda en `auditoria` (Ley 172-13)
- [x] **Días fijos del médico**: siete pastillas para marcar en qué días atiende, con su horario.
      Un día que ya tenía mañana y tarde NO se toca; destildarlo le borra sus tramos. Se ven en el
      grid y en la tarjeta. El combo de tramos sigue estando para el día suelto
- [x] **Código de turno por médico**: dos letras sacadas del nombre ("Yuber Santana Lizardo" → YO)
      y los turnos de ese médico salen YO-1, YO-2. Se calcula solo, resuelve choques (YO2, YO3) y
      se puede escribir a mano. El número sigue siendo uno por día para toda la clínica: numerar
      por médico haría que dos personas en la misma sala tuvieran ambas el "3"
- [x] **Sala de espera**: "Dar turno" pasó a un botón debajo de "Llamar al siguiente" y el panel
      derecho ahora muestra, al elegir un turno, el médico que lo atiende y TODOS sus turnos del
      día sin mezclar con los de otros médicos
- [x] **Procedencia conectada al sistema**: al elegir "médico" en ¿Quién lo refirió? la lista trae
      los médicos de la clínica, y con "ARS" trae las aseguradoras. Sigue siendo escribible para el
      que no está (un médico de otra clínica)
- [x] **Los combos de paciente listan a los pacientes registrados** de entrada (Citas, Sala de
      espera y el tarifario). Antes había que escribir dos letras y en blanco parecían rotos
- [x] **Agendar desde el tarifario**: en la ficha del procedimiento, combos de paciente y médico
      que crean la cita. Va en la ficha y no en el alta: el tarifario es un catálogo de precios y
      no tiene dueño; lo que une paciente + médico + procedimiento es la CITA
- [x] **El ITBIS se decide en un solo lugar**: se quitó la casilla por procedimiento y quedó la de
      Configuración → Cálculos e impuestos, con la explicación de que el impuesto sale solo de los
      insumos
- [x] Controles modernos que faltaban: CheckBox, pestañas y globos de ayuda (eran los de fábrica)
- [x] Se explicó qué es el **exequátur** en la etiqueta y en el globo de ayuda del campo

### Roles rehechos
- [x] **Vendedor y Cajero eran lo mismo** — en una clínica no hay piso de venta, hay un mostrador.
      Quedó Cajero, que hereda de Vendedor la edición de pacientes
- [x] Entró **Servicio**: sala de espera, citas, pacientes, médicos y procedimientos. **No cobra**
      (sin `vender`, sin `cuadre`, sin `comprobantes`). Separar quién cobra de quién atiende es lo
      que hace que el cuadre del día signifique algo
- [x] Migración `005_codigo_turno_y_roles.sql` (+ su rollback), idempotente y ya aplicada en la BD
      de desarrollo. Mueve a los usuarios de Vendedor a Cajero ANTES de borrar el rol: la FK es
      ON DELETE SET NULL y borrarlo primero los dejaría sin permisos
- [ ] ⚠️ La migración **reconstruye los permisos efectivos de todos los usuarios** desde su rol.
      Si alguien tenía permisos sueltos dados a mano, hay que volver a dárselos desde Usuarios

- [ ] Prueba manual de Yuber de todo lo de arriba

## Segunda revisión de Yuber, 2026-08-14 (HECHO — falta prueba manual)

### Almacén de Expedientes (módulo nuevo)
- [x] Grid de pacientes con cuántos documentos, citas y facturas tiene cada uno y
      cuándo vino por última vez. Se ven TODOS, también los que no tienen nada:
      son a los que hay que pedirles los papeles
- [x] Botón "Expedientes" por fila (y doble clic) → el expediente del paciente
- [x] Subida de VARIOS archivos a la vez, con el tipo elegido de antemano
      (identificación, seguro, consentimiento, referimiento, estudio, factura)
- [x] Dos modos de ver: lista y cuadrícula con ícono por formato
- [x] Doble clic en un documento → abrir con la app de Windows, guardar copia,
      reclasificar, re-ubicar en otro paciente o eliminar
- [x] "Descargar todo (ZIP)" con los nombres reales; los repetidos se numeran
- [x] Atajo a la ficha completa del paciente sin volver a la lista
- [x] **El archivo va al DISCO, no a la base**: un BLOB por cada cédula escaneada
      hincha el dump y hace inviable el respaldo diario. Carpeta configurable
      (`AjustesLocales.CarpetaExpedientes`), por defecto `expedientes\` junto al .exe
- [x] **Lista blanca de extensiones**: nada de .exe, .bat, .ps1, .lnk ni .dll. El
      documento se abre con doble clic y UseShellExecute — un ejecutable disfrazado
      de "cedula.pdf" sería una puerta de entrada al equipo de la clínica
- [x] Tope de 50 MB por archivo, con mensaje que dice qué hacer
- [x] Eliminar y re-ubicar: **solo Admin**. Eliminar es soft delete y el archivo
      queda en disco — soporte tiene que poder deshacerlo
- [x] **El respaldo ahora saca DOS archivos**: el .sql de la base y un ZIP con la
      carpeta de expedientes. Restaurar solo el .sql dejaría cada ficha apuntando
      a la nada
- [x] Permiso propio `expedientes`: lo tienen Admin, Supervisor y Servicio. El
      Cajero NO — cobra, y los papeles del paciente no son parte de cobrar
- [x] 31 tests de integración: la ficha y el archivo quedando de acuerdo, la lista
      blanca, el tope de tamaño, el ZIP, los permisos y la auditoría

> ⚠️ **Ley 172-13.** Acá entran datos de salud, que la ley clasifica como
> SENSIBLES — es un salto respecto de §1.1, donde la app no guardaba nada
> clínico. Lo que se mitigó: cada alta, apertura y borrado queda en `auditoria`
> con nombre y hora; eliminar es solo del Admin; y la app **sigue sin campos de
> diagnóstico ni tratamiento** — esto es un archivador de papeles escaneados, no
> un expediente clínico estructurado. Falta que Yuber confirme con el cliente el
> consentimiento por escrito del paciente, que es lo que la ley pide para tratar
> estos datos.

### Ajustes de pantalla
- [x] **Horas en 12h con AM/PM** en Médicos (entra/sale y los tramos). En RD nadie
      dice "las 17:00". Helper `Hora12` con 31 tests — el caso que rompe el
      "sumale 12 si es PM" son las 12: 12 AM es medianoche y 12 PM es mediodía
- [x] **Scroll horizontal en todos los grids**: 90 columnas pasaron de ancho fijo a
      `Width="Auto" MinWidth=<el que tenían>`. Con ancho fijo la celda recorta el
      texto y la tabla nunca desborda, así que el scroll no aparecía nunca
- [x] Datos centrados en el grid de Procedimientos, bajo encabezados que ya lo estaban
- [x] **Flechas de Citas dibujadas con Path**, no con los glifos ◀ ▶: esos
      caracteres no están en la fuente de la interfaz, Windows los sacaba de dos
      fuentes distintas y salían de tamaños diferentes y recortados
- [x] El scroll del card de cobro (y de los otros paneles derechos) ya no está
      pegado al contenido
- [x] Explicación de para qué sirve "Agregar tramo" en la ficha del médico
- [x] **Tipo de producto**: insumo, medicamento, material médico, equipo, limpieza,
      oficina, otro. Con columna en la lista. Todo lo ya cargado quedó como insumo
- [ ] Prueba manual de Yuber de todo lo de arriba

## Copia en PDF de los documentos (2026-08-15 — HECHO, falta prueba manual)

Pedido: *"al hacer una factura o cualquier documento, se guarde como archivo pdf
en expedientes"*.

- [x] `ExportadorPdf`: convierte a PDF el MISMO visual que va a la impresora.
      Rasteriza a 192 DPI y lo mete en una página del ancho del papel (80mm)
- [x] **Al cobrar, la factura se archiva sola** en el expediente del paciente
- [x] Los turnos también, pero **apagado por defecto**: es un papelito que se tira
      al salir, y un paciente frecuente juntaría decenas de PDF inútiles
- [x] Dos interruptores en Configuración → Respaldo
- [x] Botón "Al expediente" en Comprobantes para las facturas ANTERIORES a esto,
      con aviso si ya hay una copia para no duplicar
- [x] `VentaResultado` y `FacturaResumen` ahora llevan `ClienteId` — sin el id no
      se sabe a qué expediente va el PDF
- [x] 9 tests del archivado automático
- [ ] Prueba manual de Yuber: cobrar con paciente y ver el PDF en su expediente

### Por qué el PDF es una IMAGEN del ticket y no texto
Traducir el árbol visual de WPF a primitivas vectoriales de PDF significaría
escribir el ticket dos veces y que las dos versiones se desincronicen con el
primer cambio. Rasterizando, **el PDF es exactamente lo que salió por la
impresora**, que es justo lo que hay que poder demostrar de un comprobante.
Es el mismo camino que ya usa FAControl.

### El permiso: la trampa que casi rompe todo esto
El archivado automático **NO** exige el permiso `expedientes`. Si lo exigiera,
ninguna factura cobrada por un **Cajero** quedaría archivada —el Cajero no tiene
ese permiso, a propósito— y el fallo sería **invisible**: la venta saldría bien,
el ticket se imprimiría, y la copia simplemente no estaría.

La distinción es real: `expedientes` gobierna *hojear los papeles de un
paciente*. El archivado automático es la app guardando copia de un documento que
el usuario ya tuvo permiso de emitir. El archivado A MANO desde Comprobantes sí
lo pide, porque ahí sí es una persona metiendo un papel en una carpeta ajena.

### Lo que NO se archiva, y por qué
- **Facturas de consumidor final**: sin paciente no hay expediente donde ponerlas.
  El botón de Comprobantes lo dice con todas las letras en vez de fallar callado.
- **Cierre de caja**: es un documento de la clínica, no de un paciente.
- **Reimpresiones**: reimprimir es buscar un papel que ya existe; archivar de
  nuevo llenaría el expediente de duplicados del mismo comprobante.

## Licencia y empaquetado 1.1.0 (2026-08-18) — COMPLETA
- [x] `CodigoLicencia`: llave `MED1-XXXXX-XXXXX-XXXXX`, alfabeto sin 0/1/I/O, solo el
      SHA-256 viaja en el binario; `Hashes` es lista para poder retirar una llave quemada
- [x] `CalculadoraLicencia`: 15 días por días enteros, reloj atrasado con 24h de tolerancia,
      activada gana sobre todo, la fecha más vieja entre base y ancla es la que cuenta
- [x] `AnclaLicencia` en `%ProgramData%\MED-100\licencia.dat`, cifrada con DPAPI de máquina
- [x] `LicenciaRepository` + tabla `licencia` (008 + rollback, y dentro de 001 para instalaciones nuevas)
- [x] `VerificadorBaseDatos.ActualizarEsquemaAsync()` crea la tabla al arrancar: sin eso la
      1.1.0 se caía antes del login en toda instalación 1.0.x
- [x] Ventana de activación (antes del login y desde Configuración → Licencia) + pastilla DEMO en el menú
- [x] 45 tests nuevos (llave, regla de los días, ancla, repositorio y actualización de esquema)
- [x] Instalador `MED100_Setup_1.1.0.exe` + `{commonappdata}\MED-100` con permiso de escritura para todos los usuarios
- [x] Verificado en la app publicada: demo 15 días → vencida no abre → llave activa → reabre en Completa
- [ ] Prueba manual de Yuber en una PC limpia (con MySQL recién instalado)
- [ ] Cuando el paquete vaya a un cliente: cambiar el MySQL **web** (2 MB, necesita internet)
      de `installer/prerequisitos/` por el **offline** de 593 MB, como se hizo en POS-500 v1.2.0

## Ausencias de médicos (POSPUESTO por Yuber, 2026-08-10)
- [ ] Marcar que un médico no atiende un día puntual (vacaciones, congreso)
