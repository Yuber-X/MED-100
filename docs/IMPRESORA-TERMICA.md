# La impresora térmica imprime "números y cosas raras"

> Reporte del cliente (2026-08-25, clínica de Patrickcio):
> *"Y conecto la impresora y se pone a imprimir un reguero de números y cosas raras"*
> Impresora: **2CONNET PC580-01 V6 (USB)**, térmica de 80 mm.

---

## 1. Qué está pasando (y qué NO)

**No es un error de MED-100.** Es importante entenderlo antes de tocar nada,
porque la tentación es buscar el problema en el ticket.

MED-100 arma el ticket como un **dibujo** (un visual de WPF) y se lo entrega a
Windows para que lo imprima, igual que lo haría Word con una página. Windows se
lo pasa al driver de la impresora, y el driver lo traduce a los puntos que la
cabeza térmica tiene que quemar.

Cuando el papel sale con números, letras sueltas y símbolos, lo que ocurrió es
que **la impresora recibió esos bytes y los interpretó como texto**, en vez de
como un dibujo. Es decir: el dibujo llegó, pero del otro lado no había nadie que
supiera convertirlo. La impresora imprimió literalmente los bytes de la imagen.

Esto pasa por una de tres razones, todas de configuración de Windows:

| # | Causa | Qué se ve |
|---|---|---|
| 1 | La cola usa el driver **"Generic / Text Only"** | Números y símbolos, mucho papel |
| 2 | La impresora está instalada como **puerto genérico / modo raw** | Igual que arriba |
| 3 | El driver se instaló, pero la cola quedó apuntando al viejo | Igual que arriba |

La causa **1** es la más común: Windows detecta una impresora USB desconocida y
le asigna "Generic / Text Only" para que "algo" funcione. Con texto plano sale
bien; con un dibujo, sale el reguero.

> **Comprobación rápida de que el problema NO es MED-100:** abrí el Bloc de
> notas, escribí una línea e imprimila en esa impresora. Si el Bloc de notas sí
> imprime bien y MED-100 no, es exactamente el caso 1: el driver solo entiende
> texto. **Ese resultado confirma el diagnóstico.**

---

## 2. Arreglo, paso a paso

Todo esto se hace en la PC de la clínica. Se puede por AnyDesk.

### Paso 1 — Ver con qué driver está instalada

1. Menú Inicio → escribir **Panel de control** → abrirlo
2. **Hardware y sonido** → **Dispositivos e impresoras**
3. Buscá la impresora (aparece como `2CONNET PC580`, `POS-58`, `POS-80` o
   similar)
4. Clic derecho → **Propiedades de impresora** → pestaña **Opciones avanzadas**
5. Mirá el campo **Controlador**

**Si dice `Generic / Text Only`, encontraste el problema.** Seguí al paso 2.

### Paso 2 — Instalar el driver del fabricante

El cliente ya lo descargó (*"Driver para Windows de la impresora
2C-PO580-01-V6"*, 43 MB). Si no lo tiene a mano, está en la web de 2CONNET.

1. **Desconectá la impresora del USB**
2. Ejecutá el instalador del driver **como Administrador**
   (clic derecho → *Ejecutar como administrador*)
3. Cuando el instalador lo pida, **conectá la impresora** y encendela
4. Elegí el modelo **PC580 / POS-80** — el de **80 mm**, no el de 58 mm
5. Terminá la instalación y reiniciá la PC

> **El ancho importa.** Si se elige el perfil de 58 mm, el ticket sale cortado a
> lo ancho: MED-100 lo arma en 80 mm.

### Paso 3 — Dejar una sola cola, la correcta

Después de instalar suele quedar más de una impresora en la lista (la vieja mal
configurada y la nueva).

1. Volvé a **Dispositivos e impresoras**
2. **Borrá las colas viejas** de esa misma impresora (clic derecho → *Quitar
   dispositivo*). Dejá solo la que instaló el driver del fabricante
3. Sobre la que queda: clic derecho → **Establecer como impresora
   predeterminada**

### Paso 4 — Confirmar el tamaño de papel

1. Clic derecho sobre la impresora → **Preferencias de impresión**
2. **Tamaño de papel**: elegí el de **80 mm** (puede figurar como `80(72.1) x
   3276 mm`, `POS-80` o `80mm x Receipt`)
3. Aceptar

> Si el tamaño quedó en Carta o A4, el driver mete relleno en blanco y la
> impresora escupe papel de más entre ticket y ticket.

### Paso 5 — Probar desde Windows

Clic derecho → **Propiedades de impresora** → **Imprimir página de prueba**.

Tiene que salir texto legible con el logo de Windows. **Si acá sale mal,
todavía no es problema de MED-100** — repetí desde el paso 2.

### Paso 6 — Probar desde MED-100

1. Abrí MED-100 → **Configuración** → **Impresión y ticket**
2. En **Impresora**, elegí la que quedó
3. Cobrá una factura de prueba

---

## 3. Si después de todo esto sigue igual

Entonces sí hay que mirar más fino. Datos a levantar antes de seguir:

- [ ] ¿El Bloc de notas imprime bien? (separa "driver de texto" de "impresora
      rota")
- [ ] ¿La página de prueba de Windows sale bien?
- [ ] ¿Qué dice exactamente el campo **Controlador** en Opciones avanzadas?
- [ ] ¿Qué **puerto** usa? (`USB001`, `COM3`…) — Propiedades → pestaña Puertos
- [ ] Foto del papel que sale mal

Con el puerto en `COM`, la impresora está en modo serie y puede necesitar que
coincidan los baudios (lo habitual es 9600 o 115200), en Propiedades → Puertos →
Configurar puerto.

---

## 4. Nota para el desarrollador

MED-100 imprime con `PrintDialog.PrintVisual` (`MED100.Printing.ImpresoraTickets`),
que es el camino **GDI**: rasteriza el visual y se lo da al driver de Windows.
Por eso necesita un driver gráfico y no funciona contra una cola de texto plano.

La alternativa sería hablar **ESC/POS crudo** por el puerto, sin driver. Es el
camino que usan muchos POS y evita este problema de raíz, pero:

- hay que reescribir el ticket en comandos ESC/POS (otro formato, otro código),
- el ticket dejaría de ser idéntico al PDF que se archiva en el expediente
  —hoy son el mismo visual, y eso es lo que permite demostrar qué se imprimió—,
- y cada modelo de impresora tiene su dialecto.

**No se justifica por una PC mal configurada.** Si más adelante aparecen varias
clínicas con impresoras distintas y el soporte se vuelve un problema recurrente,
ahí sí vale reconsiderarlo — y conviene decidirlo con datos, no con este caso.
