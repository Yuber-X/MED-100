# Manual de MED-100

> Guía del día a día, en lenguaje sencillo. No hace falta saber de computadoras.

---

## 1. Entrar al sistema

Abre **MED-100** (el ícono de la tiendita) y escribe tu usuario y contraseña.

Cada empleado tiene su propia cuenta. Lo que ves en el menú de la izquierda depende de tu
rol: un cajero ve menos opciones que el administrador, y eso es normal.

Si olvidaste tu contraseña, **el administrador** te la puede cambiar (Usuarios → Editar).

### Cambio de turno (relevo rápido)

Abajo a la izquierda, junto al botón de apagado, hay un botón de **Cambiar de usuario** 👤.
Sirve para el relevo: cierra tu sesión y pide las credenciales del compañero **sin cerrar la
aplicación**. Cada venta queda registrada a nombre de quien la hizo.

> El botón de apagado ⏻ cierra la sesión **y** la aplicación.

---

## 2. Vender (la pantalla del día a día)

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

## 3. Productos, Almacén y Caducidad

- **Productos**: dar de alta, editar precios y stock, poner fecha de caducidad.
  Los filtros de arriba te muestran rápido lo que tiene *stock bajo* o *está por caducar*.
- **Almacén**: cuánto tienes de cada cosa y **cuánto vale tu inventario** en total.
- **Caducidad**: lo que se vence primero, arriba. Los colores avisan:
  - 🟢 verde: falta bastante
  - 🟡 amarillo: 4–6 meses
  - 🟠 naranja: 2–3 meses
  - 🔴 rojo: **1 mes o menos** (o ya vencido) → ¡sácalo o remátalo!

---

## 4. Clientes

Lista, alta y edición. La **cédula es opcional**: en un colmado la mayoría de la gente no
se registra, y está bien.

---

## 5. Comprobantes (buscar una factura vieja)

Busca por **número de factura** o por **nombre del cliente**, y filtra por fechas.

- **Reimprimir**: saca otra copia del ticket, idéntico al original.
- **Anular**: si te equivocaste. Pide un **motivo** (queda en el historial), **devuelve los
  productos al inventario** y marca la factura como anulada.

> Una factura **nunca se borra**: se anula. Eso protege tu contabilidad. Solo pueden anular
> quienes tengan ese permiso.

---

## 6. Cuadre de caja (cerrar el día)

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

## 7. Panel y Reportes

- **Panel**: ventas de hoy, del mes (comparadas con el mes pasado), ticket promedio,
  alertas de inventario, gráfico de ventas por día y quién vendió más.
- **Reportes**: elige un período (hoy, esta semana, este mes, mes pasado o fechas a mano)
  y te da el total, las facturas, el **ITBIS cobrado** (útil para la declaración), el
  desglose por método de pago y los productos más vendidos.

---

## 8. Usuarios (solo el Administrador)

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

## 9. Configuración (solo el Administrador)

- **Licencia**: dice si esta copia es de prueba o ya está activada → ver abajo.
- **Apariencia**: tamaño del texto (si te cuesta leer, ponlo en Grande).
- **Datos del negocio**: nombre, RNC (opcional), dirección, teléfono → salen en cada factura.
- **Cálculos e impuestos**: el ITBIS (18%) se puede **desactivar** si tu negocio no lo cobra.
- **Ventas**: mostrar u ocultar el cliente en la pantalla de Vender.
- **Impresión**: vista previa antes de imprimir (por defecto imprime directo), copias y el
  mensaje del pie del ticket.
- **Cierre de caja automático**: la hora a la que se cierra la caja sola.
- **Respaldo** ⭐ y **Exportar a Excel** → ver abajo.

### Licencia (los 15 días de prueba)

MED-100 funciona **completo durante 15 días** desde que se instala, para que puedas
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

## 10. ⭐ Lo más importante: RESPALDAR

**Configuración → Respaldar ahora.** Guarda ese archivo `.sql` en un **USB o en la nube**.

Con ese archivo puedes recuperar TODO (ventas, facturas, productos, clientes, usuarios) si
la computadora se daña o si cambias de equipo.

**Hazlo al menos una vez por semana.** Es el seguro de tu negocio.

> **Exportar a Excel** es distinto: te da una planilla para revisar o llevarle al contador.
> Para *mudarte de computadora*, usa el **respaldo**, no el Excel.

---

## 11. Preguntas frecuentes

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
*MED-100 · desarrollado por Yuber Santana · soporte según contrato de mantenimiento.*
