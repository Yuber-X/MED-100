# ITBIS.md — Matemática fiscal de MED-100

> Fórmulas implementadas en `CalculosClinica` (métodos estáticos puros, testeados;
> `VentaService` solo delega). El ITBIS se calcula **sobre la base gravada**, nunca
> acumulando redondeos línea por línea.

## 0. 🔴 La regla que separa a MED-100 de un POS común

**Los servicios de salud están EXENTOS de ITBIS en República Dominicana.**

El POS-500, del que MED-100 es copia, le cobra 18% a todo. Acá no: la exención se
decide **por línea**.

| Tipo de línea | Lleva ITBIS | De dónde sale la decisión |
|---|---|---|
| Procedimiento (consulta, laboratorio, imágenes) | **No** | `procedimiento.exento_itbis` = 1 |
| Insumo (gasa, suero, guantes) | Sí | No es servicio de salud |

La exención viaja **copiada en `detalle.exento_itbis`**, no se deduce del tipo al
mostrar la factura: cambiarla en el catálogo no puede reescribir comprobantes viejos.

Consecuencia práctica: en una factura de consulta (RD$ 1,500 exenta) más una gasa
(RD$ 100 gravada), el ITBIS es **18.00** — el 18% de 100 — y no 288.00. Cobrarlo
sobre el total encarecería cada consulta en 270 pesos.

## 1. Definiciones

| Símbolo | Significado | Origen |
|---|---|---|
| `qᵢ` | Cantidad de la línea i | Carrito |
| `pᵢ` | Precio unitario de la línea i (DECIMAL 15,2) | `producto.precio` al momento de la venta |
| `t` | Tasa de ITBIS en % (default 18.00) | `configuracion_negocio.itbis_tasa` |
| `R(x)` | `Math.Round(x, 2, MidpointRounding.AwayFromZero)` | Redondeo comercial RD |

## 2. Fórmulas

```
subtotal_línea_i = qᵢ × pᵢ                  (exacto: enteros × 2 decimales)
subtotal         = Σ subtotal_línea_i        (sin redondeo: ya está a 2 decimales)
base_gravada     = Σ subtotal_línea_i        SOLO de las líneas NO exentas
itbis            = R(base_gravada × t / 100) (ÚNICO redondeo del impuesto)
total            = subtotal + itbis

honorario        = R(base_procedimientos × pct_médico / 100)
                   base_procedimientos = Σ subtotal de las líneas de procedimiento
                   (al médico no le toca porcentaje de los insumos)

cubierto_ars     = min(lo tecleado, total)   (recorte: evita paciente_paga negativo)
paciente_paga    = total − cubierto_ars      (cubierto + paciente_paga = total, siempre)
```

### Por qué sobre el subtotal y no por línea

Con 3 líneas de RD$ 33.33 y 18%:
- **Por línea (INCORRECTO):** R(33.33×0.18)=6.00 → ITBIS 18.00
- **Sobre subtotal (CORRECTO):** R(99.99×0.18) = R(17.9982) = **18.00** — aquí coincide,
  pero con precios como 10.05 las diferencias de centavos se acumulan factura a factura
  y el libro de ventas deja de cuadrar con DGII. El único redondeo permitido es el final.

## 3. Modo de redondeo del TOTAL (`configuracion_negocio.redondeo`)

Se aplica DESPUÉS de calcular subtotal + itbis, y solo al total:

| Modo | Fórmula | Ejemplo (total bruto 117.43) |
|---|---|---|
| `centavo` (default) | total sin cambio (ya está a 2 dec.) | 117.43 |
| `peso` | `Math.Round(total, 0, AwayFromZero)` | 117.00 |
| `arriba` | `Math.Ceiling(total)` | 118.00 |

⚠ Con `peso`/`arriba`, `total ≠ subtotal + itbis` por diseño (ajuste de caja).
Los tres valores se persisten en la factura tal como se calcularon; el ITBIS
reportable sigue siendo la columna `itbis`.

## 4. Cambio

```
cambio = efectivo_recibido − total        (solo métodos efectivo)
```
Se exige `efectivo_recibido ≥ total`. En pago `mixto` el efectivo es informativo
y el cambio no se calcula (se maneja manual). `tarjeta`/`transferencia`: NULL.

## 5. Tasa histórica

Cada factura guarda `itbis_tasa` además del monto: si DGII cambia la tasa,
las facturas viejas siguen cuadrando con la tasa con la que se emitieron.

## 6. Numeración

`factura_siguiente` se reserva con `SELECT ... FOR UPDATE` dentro de la
transacción de la venta: dos cajas simultáneas jamás obtienen el mismo número.
Formatos: `F-0001` (simple) o `F-2026-0001` (con año del día de negocio, UTC-4).
