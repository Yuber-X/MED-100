# Changelog — MED-100

Formato: [Keep a Changelog](https://keepachangelog.com/es/1.0.0/). Fechas en hora de República Dominicana.

## [1.0.1] — 2026-07-12

### Added
- **Cambio rápido de usuario**: botón junto al de cerrar sesión en el pie del menú. Cierra la
  sesión actual y pide credenciales de nuevo **sin cerrar la aplicación** (pensado para el relevo
  de turno). Si el nuevo usuario no entra, la app se cierra: nunca queda una pantalla abierta con
  la sesión de otro. El menú, los permisos y las pantallas se recalculan para quien entra.

### Fixed
- **Los botones de las filas (Editar, Eliminar, Anular, Quitar) no aparecían la primera vez** que
  se entraba a Clientes, Productos, Comprobantes o Usuarios; salían recién al cambiar de pantalla
  y volver. Causa: las columnas de acciones usaban ancho automático y, con la virtualización del
  DataGrid, en la primera carga no hay filas realizadas — la columna se medía contra el encabezado
  vacío (~20px) y los botones quedaban fuera. Ahora tienen ancho fijo y aparecen desde el primer
  render. Reproducido y verificado con un DataGrid de prueba.

## [1.0.0] — 2026-07-12 · Fase 7 (Empaquetado) — TODAS LAS FASES COMPLETAS

### Added
- **Ícono de la aplicación**: una **tienda** (toldo a rayas con festón, escaparate con cruceta y
  puerta con arco) sobre cuadrado redondeado indigo. 7 tamaños (16–256px), legible incluso a 16px.
  Se ve en el Explorador, la barra de tareas, todas las ventanas y el instalador.
- **Instalador** `MED100_Setup_1.0.0.exe` (Inno Setup 6, español, 60 MB): app **self-contained**
  (el cliente NO necesita instalar .NET). **Comprueba que MySQL esté instalado** y, si falta,
  avisa con el enlace de descarga antes de continuar. El `App.config` no se pisa en actualizaciones.
- **La base de datos se crea sola** en el primer arranque (esquema + roles + permisos), como en
  PrestControl: no hay que ejecutar scripts a mano.
- `scripts/db/003_crear_usuario_dedicado.sql`: usuario MySQL `med100` con permisos mínimos, para
  no correr como root en producción.
- **`docs/INSTALL.md`**: guía técnica paso a paso (MySQL, instalación, primer arranque, usuario
  dedicado, checklist, migración de PC, problemas comunes).
- **`docs/MANUAL.md`**: manual del usuario final en lenguaje sencillo, con el respaldo destacado.

## [Unreleased]

### Added — Licencia: prueba de 15 días o llave del producto (2026-08-18)
- **MED-100 se instala en modo de prueba y funciona completo durante 15 días.**
  Se cuenta desde el primer arranque, por días enteros: si se instaló un martes a
  las 3 de la tarde, el último día se acaba el miércoles siguiente a las 3 — es
  más fácil de explicar por teléfono que "a la medianoche del día 15".
- **Ventana de activación** para escribir la llave (`MED1-XXXXX-XXXXX-XXXXX`).
  Aparece sola en los últimos 5 días de prueba, y siempre desde
  Configuración → Licencia. Se puede seguir probando desde ahí hasta que venza.
- **Al vencer, la app no abre.** Solo aparece la ventana de activación con el
  teléfono de soporte y el botón de WhatsApp. Los datos quedan intactos: escribir
  la llave devuelve todo tal cual estaba (decisión de Yuber, 2026-08-18).
- **Pastilla "DEMO · N días"** en el menú lateral mientras la copia sea de prueba.
  Desaparece al activar.
- **La llave no viaja en el binario**: solo viaja su SHA-256. Abrir el `.exe` con
  un editor y buscar cadenas no la revela. El alfabeto de la llave no tiene 0, 1,
  I ni O, que son las que se confunden cuando se dicta por teléfono.

### Notas de diseño — Licencia
- **Es UNA llave igual para todos los clientes** (decisión de Yuber, 2026-08-18).
  Sirve para que la app no quede abierta de par en par, pero no impide que un
  cliente le pase la llave a otro. `CodigoLicencia.Hashes` es una **lista** justo
  por eso: el día que una llave se queme, se publica una versión con otra y se
  retira la vieja — las instalaciones ya activadas ni se enteran, porque la
  activación queda guardada en la base y el código no se vuelve a pedir.
- **La fecha de instalación se guarda dos veces**: en la tabla `licencia` y en un
  ancla cifrada (DPAPI de máquina) en `%ProgramData%\MED-100\licencia.dat`. Manda
  siempre la **más vieja** de las dos, así que borrar la base para volver a
  empezar los 15 días no funciona, y desinstalar tampoco. Perder el ancla no
  rompe nada: se reescribe con lo que diga la base.
- **Reloj movido para atrás**: `ultima_apertura_utc` se actualiza con `GREATEST`
  —asignarle la hora de ahora borraría justamente la prueba— y si el reloj queda
  más de 24 horas por detrás, la app pide activación. Se perdona un día para no
  castigar a la PC que arranca con la hora de fábrica hasta que Windows la corrige.
- **Falla abierta, a propósito.** Si MySQL no se deja leer, el estado se decide
  solo con el ancla del disco en vez de dejar la clínica sin trabajar. Y una
  licencia activada gana sobre todo lo demás: quien pagó no queda afuera por un
  reloj mal puesto.
- La activación se audita solo si hay sesión abierta: la ventana aparece **antes**
  del login y la auditoría exige usuario. En el log de Serilog queda en los dos casos.

### Migración — Licencia
- `scripts/db/008_licencia.sql` (+ `008_rollback.sql`): tabla `licencia` de fila
  única. Idempotente. Ya incluida en `001_create_schema.sql` para instalaciones nuevas.
- Las instalaciones 1.0.x se actualizan solas: `VerificadorBaseDatos.ActualizarEsquemaAsync()`
  crea la tabla al arrancar. Sin eso, la 1.1.0 se caería antes del login en cada
  PC que ya tuviera MED-100 — la licencia se consulta antes que nada.
- El instalador crea `%ProgramData%\MED-100` con permiso de modificación para
  todos los usuarios: la recepcionista de la mañana y la de la tarde suelen ser
  cuentas de Windows distintas y las dos tienen que poder escribir el ancla.

### Added — Copia en PDF de los documentos (2026-08-15)
- **Al cobrar, la factura se guarda sola como PDF** en el expediente del paciente.
  El PDF es el mismo ticket que sale por la impresora, no una versión distinta:
  se rasteriza el visual a 192 DPI y se mete en una página del ancho del papel.
  Traducirlo a texto vectorial habría significado escribir el ticket dos veces y
  que las dos versiones se desincronizaran con el primer cambio.
- **Turnos de sala**: mismo mecanismo, apagado por defecto. Es un papelito que se
  tira al salir del consultorio; un paciente frecuente juntaría decenas de PDF.
- **Dos interruptores** en Configuración → Respaldo para decidir qué se archiva.
- **Botón "Al expediente"** en Comprobantes: guarda el PDF de una factura ya
  emitida. Está para las anteriores a este cambio, y avisa si ya hay una copia.
- `VentaResultado` y `FacturaResumen` llevan ahora `ClienteId`: sin el id del
  paciente no hay forma de saber a qué expediente va la copia.

### Notas de diseño
- **El archivado automático no exige el permiso `expedientes`, a propósito.** Si
  lo exigiera, ninguna factura cobrada por un Cajero quedaría archivada —no tiene
  ese permiso— y el fallo sería invisible: la venta saldría bien y la copia
  simplemente no estaría. Ese permiso gobierna *hojear los papeles de un
  paciente*; esto otro es la app guardando copia de algo que el usuario ya tuvo
  permiso de emitir. El archivado a mano desde Comprobantes sí lo pide.
- **No se archiva** la factura de consumidor final (sin paciente no hay
  expediente), el cierre de caja (es de la clínica, no de un paciente) ni las
  reimpresiones (duplicarían el mismo comprobante).
- Si el archivado falla, **no interrumpe el cobro**: la factura ya está emitida y
  el paciente espera su papel. El fallo queda en el log y la copia se puede
  rehacer desde Comprobantes.

### Migración
- `scripts/db/007_archivar_pdf.sql` (+ `007_rollback.sql`): `archivar_factura_pdf`
  (ON) y `archivar_turno_pdf` (OFF) en `configuracion_negocio`. Idempotente.

### Added — Almacén de Expedientes (2026-08-14)
- **Módulo nuevo**: los papeles de cada paciente —cédula, carné de la ARS,
  consentimientos, referimientos, estudios que trajo— guardados para tenerlos a mano
  la próxima vez que venga. Copia el patrón que ya probó FAControl con los contratos.
  Grid de pacientes con su conteo, botón "Expedientes" por fila, subida de varios
  archivos a la vez, dos modos de ver (lista y cuadrícula) y descarga de todo en ZIP.
- **El archivo va al disco, no a la base.** Un BLOB por cada cédula escaneada hincha
  el dump hasta hacer inviable el respaldo diario; la base guarda su ficha con la ruta
  relativa a la carpeta raíz, así mover la instalación de PC no rompe nada. La carpeta
  es configurable por terminal.
- **El respaldo ahora produce dos archivos**: el `.sql` de la base y un ZIP con la
  carpeta de expedientes. Restaurar solo el `.sql` dejaría cada ficha apuntando a la nada.
- **Permiso propio `expedientes`**: Admin, Supervisor y Servicio. El Cajero no —
  cobra, y los papeles del paciente no son parte de cobrar. Eliminar y re-ubicar
  siguen siendo exclusivos del Admin.
- **Tipo de producto**: insumo, medicamento, material médico, equipo, limpieza,
  oficina u otro, con su columna en la lista. En una clínica el almacén no es solo
  "insumos". Todo lo ya cargado quedó como insumo.

### Changed — Segunda revisión de Yuber (2026-08-14)
- **Horas en formato de 12 con AM/PM** en los horarios del médico. En RD nadie dice
  "las 17:00". El helper `Hora12` resuelve el caso que rompe el "sumale 12 si es PM":
  las 12 AM son medianoche y las 12 PM el mediodía.
- **Todos los grids desbordan en vez de recortar.** 90 columnas pasaron de ancho fijo
  a `Width="Auto"` con el ancho anterior como mínimo: con ancho fijo la celda recorta
  el texto y la tabla nunca supera el ancho de la pantalla, así que el scroll
  horizontal no aparecía nunca.
- Datos centrados en el grid de Procedimientos, bajo encabezados que ya lo estaban.
- Explicación de para qué sirve "Agregar tramo" en la ficha del médico: los días de
  arriba son los fijos, el tramo es para el día suelto y para partir la jornada en
  mañana y tarde.
- El contenido de los paneles con scroll ya no queda pegado a la barra.

### Fixed
- **Las flechas de navegación de Citas se veían de distinto tamaño y la derecha
  cortada.** Eran los glifos ◀ y ▶, que no están en la fuente de la interfaz: Windows
  los sacaba de dos fuentes distintas. Ahora son un `Path` dibujado, la misma
  geometría espejada — idénticas por construcción.

### Migración
- `scripts/db/006_tipo_producto_y_expedientes.sql` (+ `006_rollback.sql`). Agrega
  `producto.tipo`, la tabla `documento_paciente` y el permiso `expedientes`. Idempotente.
  ⚠️ El rollback borra los expedientes de la base; los archivos quedan en disco pero
  sin la tabla nadie sabe de quién es cada uno. Respaldar esa carpeta antes.

### Added — Revisión de Yuber (2026-08-14)
- **Ficha del paciente**: botón "Ver detalles" (y doble clic en la fila) abre todo el paso del
  paciente por la clínica — citas, procedimientos cobrados, facturas y turnos de sala — con los
  totales de lo facturado, lo que puso él y lo que cubrió el seguro. Consultarla queda registrada
  en `auditoria`: son datos personales y la Ley 172-13 obliga a poder decir quién los miró.
- **Días fijos del médico**: siete pastillas en el formulario para marcar los días en que atiende,
  con su horario de entrada y salida. Un día que ya tenía dos tramos (mañana y tarde) no se toca —
  reescribirlo borraría el corte del almuerzo. Se muestran en el grid y en la tarjeta.
- **Código de turno por médico**: dos letras sacadas del nombre (primera del nombre + última del
  apellido: "Yuber Santana Lizardo" → `YO`). Sus turnos salen `YO-1`, `YO-2`, y en una sala con
  varios médicos se ve de un vistazo a quién le toca cada número. Se calcula solo, resuelve los
  choques con variantes (`YO2`) y se puede escribir a mano.
- **Sala de espera**: al elegir un turno, el panel derecho muestra el médico que lo atiende —
  especialidad, días, estado ahora — y **todos sus turnos del día**, sin mezclar con los de otros.
- **Descripciones en el menú principal**: cada módulo explica para qué sirve al dejar el mouse
  encima dos segundos.
- **Agendar desde el tarifario**: en la ficha del procedimiento, combos de paciente y médico que
  crean la cita directamente.
- **Procedencia conectada al sistema**: en "¿Quién lo refirió?", al elegir *médico* la lista trae
  los médicos de la clínica y al elegir *ARS* las aseguradoras. Sigue admitiendo un nombre nuevo.
- **Rol Servicio**: atiende (sala de espera, citas, pacientes, médicos, procedimientos) pero **no
  cobra**. Separar quién cobra de quién atiende es lo que hace que el cuadre signifique algo.

### Changed
- **Vendedor y Cajero se fusionaron en Cajero**: hacían lo mismo, y en una clínica no hay piso de
  venta sino un mostrador. Cajero hereda de Vendedor la edición de pacientes.
- **El ITBIS se decide en un solo lugar**: se quitó la casilla por procedimiento; queda la de
  Configuración → Cálculos e impuestos, ahora con la explicación de que el impuesto sale solo de
  los insumos (los servicios de salud van exentos).
- **Los combos de paciente listan a los registrados de entrada** (Citas, Sala de espera,
  tarifario). Antes exigían escribir dos letras y en blanco parecían rotos.
- **En Nueva cita solo aparecen los médicos que atienden ese día**: ofrecer a uno que no viene
  llevaba a un combo de horas vacío sin explicación.
- **"Dar turno" pasó a un botón** debajo de "Llamar al siguiente"; el panel derecho queda para la
  ficha del médico, que es lo que se mira todo el día.
- Ícono de la aplicación: **cruz médica** blanca sobre indigo (era una tienda, herencia del
  POS-500). También el logo del menú y el título de la ventana, que ya no dice "Punto de venta".
- Controles que seguían siendo los de fábrica y desentonaban: **CheckBox, pestañas y globos de
  ayuda** ahora siguen el lenguaje visual del resto.

### Fixed
- **Los ComboBox mostraban el objeto crudo en vez de la etiqueta** — "OpcionSexo { Valor = ,
  Etiqueta = Sin e…" en Sexo, ¿Quién lo refirió?, Citas y Sala de espera. Causa: el template propio
  del ComboBox no propagaba `ItemTemplateSelector`, que es donde WPF deja el template que fabrica
  `DisplayMemberPath`. Se corrigió el template y además se pasaron las nueve combos a
  `ItemTemplate`, que llega a la caja cerrada por el camino documentado.
- **En Citas los botones y el combo de médico se pisaban** al achicar la ventana: la barra tenía
  todo en un renglón, con los botones fijos a la derecha y una fila de controles que no se encoge.
  Ahora son dos renglones.
- **El grid de Pacientes recortaba los datos**: la columna estrella se comprimía para que entraran
  todas y el scroll horizontal nunca aparecía. Con mínimos por columna, la tabla desborda y se
  desplaza en vez de cortar.

### Migración
- `scripts/db/005_codigo_turno_y_roles.sql` (+ `005_rollback.sql`). Agrega `medico.codigo_turno`,
  crea el rol Servicio, fusiona Vendedor en Cajero y resincroniza permisos. Es idempotente.
  ⚠️ Reconstruye los permisos efectivos desde el rol: los permisos sueltos dados a mano hay que
  volver a otorgarlos desde Usuarios.


### Fixed — Escala de texto y acentos (2026-07-12, reportado por Yuber)
- **Tildes y ñ se rompían** en los encabezados de tablas y en las etiquetas de las tarjetas
  ("CAÑÓN", "Cédula", "Descripción" salían con el acento suelto). Causa: `Typography.Capitals =
  AllSmallCaps` — la fuente no tiene glifos de versalita para letras acentuadas y WPF los
  componía mal. Retirado de `Texto.MicroLabel` y `Tabla.Encabezado`.
- **El menú principal no cabía** con el texto en Mediano/Grande: "Usuarios" y "Configuración"
  quedaban fuera de la pantalla y eran inalcanzables. El sidebar ahora tiene scroll vertical.
- **Los montos de las tarjetas se cortaban a la mitad** al agrandar el texto. Ahora el número se
  encoge solo si no cabe (`Viewbox` con `StretchDirection=DownOnly`): a tamaño normal se ve igual,
  y `RD$ 12,345,678.90` entra completo incluso en Grande. Afecta Panel, Reportes, Cuadre, Almacén
  y Caducidad.
- **Las tablas recortaban la columna principal** (en Productos apenas se veía la primera letra del
  nombre): las columnas `*` colapsaban al reducirse el ancho lógico. Ahora tienen `MinWidth`.
- **Los botones de las tablas se cortaban** ("Quitar" → "quit"): las columnas de acciones pasaron
  a ancho automático, así el botón nunca queda a medias.
- **Las barras de filtros se salían** (el botón "Ver e imprimir" del cuadre quedaba incompleto):
  ahora usan `WrapPanel` y bajan de línea en vez de recortarse.
- 2 tests nuevos (97): tildes y ñ sobreviven el viaje app → MySQL → app.

### Added — Fase 6: Administración y Configuración (2026-07-12)
- **Admin de Usuarios** (SOLO Admin, regla de Yuber): crear empleados, **restablecer contraseñas
  sin conocer la anterior**, activar/desactivar y cambiar rol. Protecciones: el Admin no puede
  desactivarse a sí mismo ni quitarse los permisos de administración (quedaría fuera del sistema).
- **Permisos por usuario desde la misma pantalla**: el rol siembra los permisos por defecto
  (triggers heredados del POS-400) y el Admin marca/desmarca casillas para afinar — por ejemplo
  quitarle a un Supervisor la edición de productos, o darle a un Cajero la anulación de facturas.
  Cada cambio queda auditado con el detalle (+[permisos añadidos] / -[quitados]).
- **Configuración ampliada** con las funciones de PrestControl:
  - *Apariencia*: tamaño de texto Pequeño/Mediano/Grande, se aplica al instante y **escala toda la
    interfaz, incluidos los encabezados de las tablas**.
  - *Respaldo*: respaldar y restaurar la base de datos (mysqldump/mysql, contraseña por `MYSQL_PWD`,
    nunca en la línea de comandos). Es el camino para **cambiar de equipo** conservando todo.
    Restaurar pide doble confirmación.
  - *Exportación a Excel*: manual y **automática cada N días** a una carpeta elegida (idéntica a
    PrestControl). Una hoja por tabla; `password_hash` jamás se exporta.
  - *Cierre de caja automático*: a la hora que elija el Admin, la app cierra el cuadre del día de
    todos los cajeros. Si estaba cerrada a esa hora, el cierre ocurre al volver a abrirla.
- **Cuadre general** (nuevo, por defecto): desglose de TODOS los cajeros en una sola vista con los
  totales del negocio, y botón para cerrar todos los turnos pendientes de una vez.
- **Impresión del cierre**: SIEMPRE con vista previa antes de imprimir y con tamaño elegible
  (ticket 80mm u hoja carta), con espacio para la firma del responsable.
- 16 tests nuevos (95 en total): permisos por rol y overrides, contraseñas hasheadas, el Cajero no
  puede administrar, el Admin no puede autobloquearse, cuadre general y cierre sin duplicar.

### Changed
- **ComboBox y DatePicker modernos**: el template por defecto de WPF se veía como Windows Forms
  (rectangular, gris, texto a la izquierda). Ahora bordes redondeados, hover indigo, chevron que
  se invierte al abrir, popup con sombra y **texto centrado** en la caja y en cada opción.

### Added — Fase 5 (Analítica, 2026-07-12)
- **Panel** (permiso `panel`): KPIs de ventas de hoy, ventas del mes con **variación vs. mes
  anterior**, ticket promedio y alertas de inventario (por caducar + stock bajo, con los umbrales
  de Configuración). Gráfico de barras de ventas diarias del mes (LiveCharts) y rankings de
  top cajeros y top productos.
- **Reportes por fecha** (permiso `reportes`): atajos Hoy / Ayer / Esta semana (empieza lunes) /
  Este mes / Mes pasado / Personalizado; KPIs del período (total vendido, facturas, ticket
  promedio, **ITBIS cobrado**), desglose por método de pago, tendencia diaria, top de productos
  y ventas por cajero.
- `AnaliticaRepository`: todo agrupado por **día de negocio** (UTC-4) y **excluyendo las
  facturas anuladas**, que se informan aparte sin sumar. Los totales se calculan en SQL con
  DECIMAL; los `double` solo existen dentro del gráfico (presentación).
- 15 tests nuevos (79 en total): rangos de fecha (incluidos los bordes de semana y el cambio de
  año), variación sin dividir entre cero, totales que excluyen anuladas, rankings ordenados y
  permisos exigidos.

### Added — Fase 4 (Operaciones, 2026-07-12)
- **Buscar Comprobante**: búsqueda por número de factura o cliente y rango de fechas evaluado
  por **día de negocio** (UTC-4: una venta de las 11pm pertenece a ese día). El alcance lo
  impone `FacturaService`, no la UI: sin `comprobantes_todos` un Cajero solo ve sus facturas
  aunque manipule los filtros.
- **Reimpresión**: el ticket sale idéntico al original (mismos totales, líneas y tasa de ITBIS
  histórica). Camino único compartido con la venta (`App.MostrarTicket`).
- **Anulación de facturas** (permiso `facturas_anular`): motivo obligatorio, **devuelve el stock**
  al inventario, marca `estado='anulada'` y queda auditada con acción `anular`. La factura nunca
  se borra. Anular dos veces es imposible (el UPDATE exige `estado='emitida'`), así que el stock
  jamás se duplica.
- **Cuadre de Caja**: facturas y total vendido, desglose por método de pago (calculado en SQL),
  **tiempo activo** del turno (tabla `sesion`, contando la sesión en curso), y las anuladas
  informadas aparte sin sumar al total. El cierre es **inmutable** (UNIQUE usuario+fecha).
  Un Cajero solo ve y cierra su propio turno; con `cuadre_todos` se elige cajero.
- `IDialogService.PedirTexto` + `PedirTextoWindow` (WPF no trae input box) para el motivo de anulación.
- 9 tests de integración nuevos (61 en total): anulación devuelve stock una sola vez, sin permiso
  no toca nada, el Cajero no puede espiar facturas ajenas, cuadre excluye anuladas, cierre único.

### Fixed
- `FacturaRepository`: el SELECT del detalle concatenaba columnas después del FROM y MySQL las
  leía como tablas ("Unknown database 'f'"). Columnas y FROM ahora van separados.

### Added — Ajustes de Yuber + Configuración inicial (2026-07-12)
- **Pantalla Configuración** (EXCLUSIVA del Admin) con 4 secciones: Datos del negocio
  (nombre y RNC opcional, se imprimen en el ticket), Cálculos e impuestos (ITBIS
  activable + tasa + redondeo), Ventas y facturación (mostrar/ocultar cliente en Vender)
  e Impresión y ticket (vista previa, copias, pie). Guardar exige permiso 'configuracion'
  y queda auditado con un resumen legible de los cambios.
- **ITBIS activable/desactivable**: al apagarlo desaparece de la pantalla Vender y del
  ticket, y las facturas se emiten con `itbis = 0` e `itbis_tasa = 0`
  (`ConfiguracionNegocio.ItbisTasaEfectiva`). Columna nueva `configuracion_negocio.itbis_activo`.
- **Impresión automática al cobrar**: el ticket sale solo, sin diálogo (default). La vista
  previa es opcional; si la impresión directa falla, se abre la vista previa para reintentar
  sin tocar la venta ya registrada.

### Changed
- Buscador de Vender: separación entre el nombre del producto y el "stock: N".
- Tests de integración serializados en una colección xUnit (compartían `SesionActual`
  estático y las BD de prueba; en paralelo se pisaban entre sí).
- 52 tests verdes (7 nuevos).

### Added — Fase 3 (2026-07-12)
- VentaService: ITBIS sobre subtotal (único redondeo AwayFromZero), modos de redondeo del
  total (centavo/peso/arriba), numeración atómica FOR UPDATE (F-0001 / F-2026-0001),
  emisión en UNA transacción con stock validado en la misma sentencia y auditoría.
- Pantalla Vender: escaneo por código de barras (código exacto agrega directo y limpia la
  caja), búsqueda por nombre, carrito con +/−/quitar, cliente OPCIONAL y ocultable
  (regla Yuber, configuracion_negocio.mostrar_cliente_en_venta), efectivo/cambio en vivo,
  COBRAR con manejo de stock concurrente.
- Ticket 80mm: mismo visual para pantalla e impresora (patrón PrestControl); datos del
  negocio desde la BD (jamás hardcodeados); impresión reintentable sin tocar la venta.
- ConfiguracionNegocioService (singleton en memoria) + docs/ITBIS.md.
- 15 tests nuevos (45 en total), incluidos los obligatorios: venta simple, ITBIS sobre
  subtotal, stock insuficiente revierte todo, emisión concurrente con números únicos.

### Added — Fase 2 (2026-07-11)
- Módulo Clientes: lista con búsqueda, formulario nuevo/editar, eliminación suave; cedula
  OPCIONAL y normalizada (000-0000000-0); edición restringida al permiso clientes_editar.
- Módulo Productos: CRUD con código de barras opcional único, precio decimal, stock,
  caducidad; filtros rápidos; auditoría con antes→después en precio/stock.
- Módulo Almacén: totales por SQL (productos, unidades, valor del inventario, stock bajo).
- Módulo Caducidad: semáforo mensual heredado del POS-400 (BLOCKERS #3) con pills de color
  y contadores de críticos; orden por proximidad.
- Navegación lista↔formulario con subpáginas (el sidebar mantiene la sección activa) y
  recarga automática al entrar a cada página (IPaginaAsincrona).
- 24 tests nuevos (30 en total): CalculadoraCaducidad al 100% de ramas, NormalizarCedula.

### Added — Fase 1 (2026-07-11)
- Solución completa MED100 (8 proyectos + 2 de tests) clonada de la arquitectura PrestControl.
- Esquema `med100_db` (13 tablas, triggers de permisos) + seed de roles, probados en MySQL local.
- Login multiusuario: BCrypt cost 12, rate-limiting, wizard de cuenta inicial (crea al Admin),
  `SesionActual` con rol + permisos efectivos.
- Shell con sidebar de 11 módulos FILTRADO POR PERMISOS (Configuración solo aparece al Admin);
  navegación revalida permisos (defensa en profundidad). Páginas como placeholders.
- VerificadorBaseDatos: diagnóstico al arrancar + creación automática de la BD (schema con
  triggers + seed embebidos, ejecutados como bloques separados).
- 6 tests (3 unit SesionActual + 3 integración aprovisionamiento). App arranca y muestra login.

### Added
- Arranque del proyecto (2026-07-11): CLAUDE.md (spec de Claude Active + 2 reglas nuevas de Yuber:
  cliente opcional/ocultable en Vender, Configuración solo-Admin), DESIGN.md heredado de PrestControl,
  TODO.md, BLOCKERS.md.
