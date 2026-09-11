# Manual de Odonto Unión

> Guía del día a día, en lenguaje sencillo. No hace falta saber de computadoras.

---

## 1. Entrar al sistema

Abre **Odonto Unión** y escribe tu usuario y contraseña.

Cada empleado tiene su propia cuenta. Lo que ves en el menú de la izquierda depende de tu
rol: un cajero ve menos opciones que el administrador, y eso es normal.

Si olvidaste tu contraseña, **el administrador** te la puede cambiar (Usuarios → Editar).

### Cambio de turno (relevo rápido)

Abajo a la izquierda, junto al botón de apagado, hay un botón de **Cambiar de usuario** 👤.
Sirve para el relevo: cierra tu sesión y pide las credenciales del compañero **sin cerrar la
aplicación**. Cada venta queda registrada a nombre de quien la hizo.

> El botón de apagado ⏻ cierra la sesión **y** la aplicación.

---

## 2. Sala de espera (los turnos)

La puerta de la clínica. Aquí se entrega el número **sin registrar a nadie**: el
turno se da primero, el paciente se registra después y la factura viene mucho
después. Son tres momentos distintos y el sistema no los obliga a ocurrir juntos.

- **Dar turno** — entrega el siguiente número. Puedes darlo en blanco (solo el
  número) o elegir el médico.
- **Si eliges médico**, el número sale con su código —por ejemplo **YO-7**— y el
  turno queda en la lista de ese médico.
- **Llamar al siguiente** toma el que lleva más tiempo esperando. También puedes
  **llamar a uno puntual** si hace falta salirse del orden.
- Al terminar, el turno se cierra como **Atendido** o **Ausente**.
- **Asignar paciente** conecta un turno ya dado con la ficha del paciente, cuando
  se registra más tarde.
- **Reimprimir** vuelve a sacar el papelito si se perdió.

Un turno pasa por: **Esperando → Llamado → Atendido** (o **Ausente**).

---

## 3. Citas (la agenda del día)

La agenda trabaja **de día en día**: flechas para moverte y un botón **Hoy** para
volver.

Al crear una cita eliges paciente, médico, procedimiento y hora. **La pantalla te
muestra solo los huecos libres**, calculados con el horario de ese médico y la
duración de ese procedimiento — por eso vale la pena tener bien cargados los dos.

Una cita pasa por estos estados:

| Estado | Qué significa |
|---|---|
| **Programada** | Se anotó, todavía sin confirmar |
| **Confirmada** | El paciente dijo que viene |
| **Atendida** | Ya pasó por el médico |
| **Cancelada** | No se va a dar |
| **No asistió** | Tenía cita y no vino |

Si te equivocas al cambiar el estado, **Deshacer** lo devuelve al anterior.

- **Cobrar** lleva la cita a la pantalla de Cobrar con el paciente y el
  procedimiento ya cargados.
- **Enviar recordatorios** manda un correo **a cada paciente** con cita próxima.
  Es distinto del correo de caducidad, que es uno solo para el dueño; por eso
  tiene su propio interruptor en Configuración.

> Cada cita se marca como "recordatorio enviado" **solo después** de que su
> correo salió bien. Si la conexión se corta a mitad de la tanda, los que
> quedaron sin aviso se los manda en el siguiente intento.

Todas las horas son la hora de aquí. No hay que hacer ninguna cuenta.

---

## 4. Médicos

La ficha de cada médico:

- **Nombre y especialidad**
- **Código de turno** — el prefijo con que salen sus números en la sala (YO, PA...)
- **Porcentaje de honorario** — lo que le corresponde de lo que se cobra
- **Horarios fijos** — los días y las horas en que atiende. Los días se marcan
  todos juntos con casillas, y cada día puede tener su tramo.

La lista dice, de un vistazo, **quién está atendiendo ahora mismo** y qué días
atiende cada uno.

> El porcentaje de honorario **queda guardado en la ficha**, y cada vez que se
> cambia queda anotado en el historial. Todavía **no genera un reporte de
> liquidación**: hoy sirve como dato de referencia.

---

## 5. Procedimientos (el tarifario)

Lo que cobra la clínica, con su precio:

- **Código** y **nombre**
- **Precio**
- **Duración** — es la que usa la agenda para calcular los huecos. Un
  procedimiento con la duración mal puesta desordena la agenda entera.
- **ITBIS: exento o gravado** — importante para la declaración. Muchos servicios
  de salud son exentos; se marca uno por uno.
- **Activo / Inactivo** — el que deja de ofrecerse se desactiva, no se borra, para
  que las facturas viejas sigan cuadrando.

Desde el tarifario puedes **agendar directo**, sin pasar por la pantalla de Citas.

---

## 6. Expedientes

El archivo de papeles de cada paciente: radiografías, consentimientos, resultados,
lo que haga falta. Contesta la pregunta con la que nació el módulo: **"volvió este
paciente — ¿qué tenemos ya de él?"**.

La lista muestra a **todos** los pacientes con cuántos documentos tiene cada uno.

> El filtro "solo los que tienen documentos" viene **apagado a propósito**. Los
> que importan casi siempre son los que **no** tienen nada: a esos hay que
> pedirles los papeles.

Se entra al expediente del paciente y desde ahí se suben, se ven y se imprimen
los documentos.

> 🔒 Los expedientes tienen **su propio permiso**, y ese permiso gobierna hasta
> **mirarlos**. Es información de salud del paciente.

---

## 7. Cobrar (la pantalla del día a día)

1. **Escanea el código de barras** del producto con la pistola. Se agrega solo al carrito
   y la caja de búsqueda se limpia para el siguiente.
   - ¿No tiene código? Escribe parte del nombre y haz clic en el producto de la lista.
2. Ajusta cantidades con los botones **−** y **+**. Para sacar algo, **Quitar**.
3. A la derecha:
   - **Cliente**: es opcional. Si no lo eliges, la factura sale como "Consumidor final".
     (Si tu negocio no usa clientes, el administrador puede ocultar esta casilla.)
   - **Método de pago**: efectivo, tarjeta, transferencia o mixto.
   - Si es efectivo, escribe cuánto te dio el cliente y el **cambio se calcula solo**.
4. Presiona **COBRAR**. El ticket se imprime automáticamente.

> Si la impresora falla, **la venta ya quedó guardada**. Se abre una vista previa para que
> puedas reintentar la impresión. Nunca pierdes una venta por culpa de la impresora.

---

## 8. Productos, Almacén y Caducidad

- **Productos**: dar de alta, editar precios y stock, poner fecha de caducidad.
  Los filtros de arriba te muestran rápido lo que tiene *stock bajo* o *está por caducar*.
- **Almacén**: cuánto tienes de cada cosa y **cuánto vale tu inventario** en total.
- **Caducidad**: lo que se vence primero, arriba. Los colores avisan:
  - 🟢 verde: falta bastante
  - 🟡 amarillo: 4–6 meses
  - 🟠 naranja: 2–3 meses
  - 🔴 rojo: **1 mes o menos** (o ya vencido) → ¡sácalo o remátalo!

---

## 9. Pacientes

Lista, alta y edición. La **cédula es opcional**: en un colmado la mayoría de la gente no
se registra, y está bien.

---

## 10. Comprobantes (buscar una factura vieja)

Busca por **número de factura** o por **nombre del cliente**, y filtra por fechas.

- **Reimprimir**: saca otra copia del ticket, idéntico al original.
- **Anular**: si te equivocaste. Pide un **motivo** (queda en el historial), **devuelve los
  productos al inventario** y marca la factura como anulada.

> Una factura **nunca se borra**: se anula. Eso protege tu contabilidad. Solo pueden anular
> quienes tengan ese permiso.

---

## 11. Cuadre de caja (cerrar el día)

Por defecto ves el **cuadre general**: cuánto vendió cada cajero y el total del negocio,
separado por efectivo, tarjeta y transferencia. También ves **cuánto tiempo estuvo activo**
cada uno.

- **Ver e imprimir**: muestra el cierre antes de imprimir. Puedes elegir **ticket 80mm**
  (la impresora de la caja) u **hoja carta** (para archivar, trae espacio para firmar).
- **Cerrar cuadre del día**: congela los números del día. Una vez cerrado ya no cambia.

> El administrador puede configurar que la caja **se cierre sola** a una hora fija
> (Configuración → Cierre de caja automático).

Las facturas anuladas aparecen aparte y **no suman** al total: así ves que el dinero no
"desapareció".

---

## 12. Fiados (lo que queda debiendo el paciente)

Al cobrar puedes marcar **"Queda debiendo"**: el paciente paga una parte hoy y el
resto después. Cuando lo haces, el sistema **te obliga a poner una fecha de
compromiso** — el día en que quedó en pagar.

En la pantalla **Fiados** ves toda la deuda con un semáforo, igual que el de los
productos por vencer:

- **Al día** — falta más de una semana para la fecha de compromiso
- **Por vencer** — vence dentro de los próximos 7 días
- **Vencido** — ya pasó la fecha
- **En mora** — pasaron más de 15 días

Desde ahí cobras los abonos y puedes correr la fecha de compromiso si el paciente
lo pide.

> ⚠️ **Lo importante para el cuadre:** el cuadre del día suma **lo que de verdad
> entró en la caja**, no el total facturado. Lo fiado se informa aparte. Y cuando
> el paciente viene a abonar, ese dinero entra en el cuadre **del día en que
> paga**, no en el de la factura. Así un cierre ya firmado nunca cambia solo.

Las deudas por vencer entran también en el **correo automático diario**, junto a
los productos próximos a caducar.

---

## 13. Medicamentos indicados

Cuando el médico indica un medicamento, se anota aquí: **qué**, la dosis, cada
cuánto, por cuántos días y las instrucciones. Solo el nombre del medicamento es
obligatorio — a veces el médico dice "amoxicilina 500 cada 8" y a veces dice
"algo para el dolor".

Sirve para tener el historial de lo que se indicó durante el día trabajado.

> 🔒 **Esto es información de salud del paciente.** La Ley 172-13 la trata como
> dato sensible, así que la pantalla tiene su propio permiso —y ese permiso
> gobierna hasta **mirar**, no solo escribir—; toda alta y baja queda en el
> historial con nombre y apellido, y nada se borra de verdad.
>
> Se anota **qué** se indicó, nunca **por qué**: acá no van diagnósticos ni
> antecedentes.

---

## 14. Panel y Reportes

- **Panel**: ventas de hoy, del mes (comparadas con el mes pasado), ticket promedio,
  alertas de inventario, gráfico de ventas por día y quién vendió más.
- **Reportes**: elige un período (hoy, esta semana, este mes, mes pasado o fechas a mano)
  y te da el total, las facturas, el **ITBIS cobrado** (útil para la declaración), el
  desglose por método de pago y los productos más vendidos.

---

## 15. Usuarios (solo el Administrador)

Aquí creas las cuentas de tus empleados:

1. **+ Nuevo usuario** → nombre, usuario, **rol** y contraseña.
2. El rol le da automáticamente sus permisos:
   - **Cajero**: vender, ver clientes, su cuadre y sus comprobantes.
   - **Vendedor**: vender y gestionar clientes.
   - **Supervisor**: casi todo el piso de venta, sin usuarios ni configuración.
   - **Admin**: todo.
3. Abajo puedes **marcar o desmarcar permisos uno por uno**. Por ejemplo: quitarle a un
   supervisor la opción de *eliminar productos*, o darle a un cajero la de *anular facturas*.
4. Para cambiarle la contraseña a alguien que la olvidó: **Editar** → escribe la nueva.
   (No necesitas saber la anterior.)

---

## 16. Configuración (solo el Administrador)

- **Licencia**: dice si esta copia es de prueba o ya está activada → ver abajo.
- **Apariencia**: tamaño del texto (si te cuesta leer, ponlo en Grande).
- **Datos del negocio**: nombre, RNC (opcional), dirección, teléfono → salen en cada factura.
- **Cálculos e impuestos**: el ITBIS (18%) se puede **desactivar** si tu negocio no lo cobra.
- **Ventas**: mostrar u ocultar el paciente en la pantalla de Cobrar.
- **Impresión**: vista previa antes de imprimir (por defecto imprime directo), copias y el
  mensaje del pie del ticket.
- **Cierre de caja automático**: la hora a la que se cierra la caja sola.
- **Respaldo** ⭐ y **Exportar a Excel** → ver abajo.

### Licencia (los 15 días de prueba)

Odonto Unión funciona **completo durante 15 días** desde que se instala, para que puedas
probarlo con calma. Mientras dure, en el menú de la izquierda vas a ver un cartelito
**DEMO · N días** con los días que quedan.

En los últimos 5 días, al abrir el programa aparece una ventana para escribir la **llave
del producto**. Si todavía estás probando, dale a *Seguir en modo de prueba*.

Cuando se acaben los 15 días, el programa **no abre** hasta que se escriba la llave. **No
se pierde nada**: los pacientes, las facturas y todo lo cargado quedan igual, y aparecen
tal cual cuando se activa.

Para activar: **Configuración → Licencia → Activar**, escribe la llave que te dieron al
comprar y listo — se pide una sola vez. Si no la tienes a mano, la ventana trae el botón
de **WhatsApp** para pedirla.

> Si cambias el programa a otra computadora, hay que **volver a escribir la llave** en la
> computadora nueva.

---

## 17. ⭐ Lo más importante: RESPALDAR

**Configuración → Respaldar ahora.** Guarda ese archivo `.sql` en un **USB o en la nube**.

Con ese archivo puedes recuperar TODO (ventas, facturas, productos, clientes, usuarios) si
la computadora se daña o si cambias de equipo.

**Hazlo al menos una vez por semana.** Es el seguro de tu negocio.

> **Exportar a Excel** es distinto: te da una planilla para revisar o llevarle al contador.
> Para *mudarte de computadora*, usa el **respaldo**, no el Excel.

---

## 18. Preguntas frecuentes

**¿Puedo vender sin cliente?**
Sí. Es lo normal en un colmado o farmacia: sale como "Consumidor final".

**Se fue la luz en medio de una venta.**
Si no habías dado COBRAR, esa venta no existe: repítela. Si ya cobraste, está guardada.

**Vendí algo equivocado.**
Comprobantes → busca la factura → **Anular** (con el motivo). El producto vuelve al inventario.

**El cajero no ve el Panel ni los Reportes.**
Correcto: no son parte de su rol. Si quieres que los vea, el Admin puede darle esos permisos
en Usuarios.

**¿Los precios viejos cambian si subo un precio?**
No. Cada factura guarda el precio y el ITBIS con los que se emitió. El pasado no se toca.

---
*Odonto Unión · desarrollado por Yuber Santana · soporte según contrato de mantenimiento.*
