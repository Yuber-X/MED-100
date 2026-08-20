# DESIGN.md — MED-100 (Punto de Venta)

> Guía de diseño para aplicación de escritorio (WPF). **Heredada del DESIGN.md de PrestControl** (2026-07-11): misma paleta indigo, tipografía y componentes, para que ambos productos compartan lenguaje visual y se reutilicen los estilos XAML tal cual. Solo cambian la identidad y algunos patrones propios de un POS (pantalla Vender densa, ticket 80mm).

---

## 1. Identidad del producto

- **Nombre:** MED-100 (provisional — renombrar si el cliente elige otro)
- **Sector:** Retail / punto de venta
- **Público:** Farmacias, colmados y tiendas minoristas dominicanas; múltiples empleados por turno
- **Personalidad de marca:** Profesional, confiable, moderna, densa en información. Debe transmitir seriedad financiera sin sentirse burocrática.
- **Contrapersonalidad (evitar):** Cripto / neobank / "millennial fintech" / fondos oscuros / neones

## 2. Localización

- **Idioma:** Español dominicano profesional
- **Moneda:** Pesos dominicanos con formato `RD$ 15,000.00` (coma como separador de miles, punto decimal, espacio después de RD$)
- **Fecha:** `DD/MM/YYYY` para inputs, `DD-MMM-YY` para tablas compactas
- **Hora:** Formato 12h con AM/PM
- **Zona horaria:** America/Santo_Domingo

---

## 3. Paleta de colores

### Marca principal (Indigo)
| Rol | Hex | Uso |
|---|---|---|
| Indigo 600 | `#4F46E5` | Color principal, botones primarios, links, énfasis |
| Indigo 700 | `#312B94` | Hover de botones primarios, títulos de sección |
| Indigo 100 | `#E0E4FD` | Fondos suaves, badges informativos |
| Indigo 50  | `#EEF0FE` | Fondos muy sutiles, sidebars |

### Semántica financiera (semáforo de cobros)
Este es el sistema visual más importante de la aplicación. Cada estado de cuota tiene su color asignado:

| Estado | Texto | Fondo | Hex texto | Hex fondo |
|---|---|---|---|---|
| **Al día** | Verde oscuro | Verde claro | `#065F46` | `#D1FAE5` |
| **Por vencer** (≤ 7 días) | Ámbar oscuro | Ámbar claro | `#854F0B` | `#FEF3C7` |
| **Vencido** (1-15 días) | Naranja oscuro | Naranja claro | `#9A3412` | `#FED7AA` |
| **En mora** (> 15 días) | Rojo oscuro | Rojo claro | `#991B1B` | `#FEE2E2` |
| **Pagado** | Azul oscuro | Azul claro | `#1E40AF` | `#DBEAFE` |
| **Cancelado / anulado** | Gris oscuro | Gris claro | `#404040` | `#E5E5E4` |

### Neutros
| Rol | Hex |
|---|---|
| Fondo de aplicación | `#FAFAF9` |
| Fondo de card | `#FFFFFF` |
| Border sutil | `#E5E5E4` |
| Border estándar | `#D4D4D3` |
| Texto principal | `#1A1A1A` |
| Texto secundario | `#555553` |
| Texto terciario / placeholder | `#888780` |
| Texto deshabilitado | `#B8B8B4` |

---

## 4. Tipografía

### Familia
- **Interfaz general:** `Inter`, con fallback a `Segoe UI` (en WPF: `Segoe UI` es garantizado nativo)
- **Números y monedas:** `Inter` con `font-variant-numeric: tabular-nums` habilitado. Esto es **obligatorio** en tablas de dinero para que las cifras se alineen verticalmente.
- **Monoespaciada (si se necesita):** `JetBrains Mono` para IDs, códigos de cuota, referencias de transacción

### Escala tipográfica
| Nivel | Tamaño / Line height | Peso |
|---|---|---|
| Display (números grandes de dashboard) | 28px / 34px | 600 semibold |
| Título 1 (nombre de pantalla) | 22px / 28px | 600 semibold |
| Título 2 (secciones dentro de pantalla) | 16px / 22px | 600 semibold |
| Título 3 (subsecciones, headers de card) | 14px / 20px | 600 semibold |
| Cuerpo | 13px / 18px | 400 regular |
| Cuerpo enfatizado | 13px / 18px | 500 medium |
| Caption (metadatos, timestamps) | 11px / 14px | 400 regular |
| Micro-label (uppercase encabezados) | 10px / 12px | 600 semibold, letter-spacing 0.05em, UPPERCASE |

### Reglas para números
- Todos los montos monetarios usan **tabular-nums** sin excepción
- Moneda siempre precedida por `RD$` con un espacio
- Formato: `RD$ 15,000.00` (nunca `RD$15000` ni `$15,000`)
- Alineación a la derecha en columnas de tabla
- Totales en peso 600 semibold
- Números negativos en rojo `#991B1B` con signo menos (`-RD$ 500.00`), nunca en paréntesis

---

## 5. Sistema de espaciado

Base **4px**. Escala permitida: `4, 8, 12, 16, 20, 24, 32, 40, 48, 64`. No usar valores intermedios.

| Contexto | Espaciado |
|---|---|
| Padding interno de card | 20px |
| Gap entre cards | 12px |
| Gap entre secciones | 32px |
| Padding vertical de botón | 10px |
| Padding horizontal de botón | 16px |
| Padding de fila de tabla | 12px vertical |
| Gap entre label e input | 6px |

---

## 6. Bordes y esquinas

| Elemento | Radius |
|---|---|
| Pills / badges | 6px |
| Botones, inputs | 8px |
| Cards, modales, dropdowns | 12px |
| Elementos circulares (avatares, íconos de KPI) | 50% |

**Grosor de borde:** siempre `0.5px` o `1px`. Nunca mayor. En WPF, `0.5px` se logra con `BorderThickness="0.5"`.

---

## 7. Sombras

Sutiles, funcionales, nunca decorativas.

| Nivel | Uso | Valor CSS | Equivalente WPF (DropShadowEffect) |
|---|---|---|---|
| 1 | Cards, elementos elevados | `0 1px 2px rgba(0,0,0,0.05)` | `BlurRadius=2, ShadowDepth=1, Opacity=0.05` |
| 2 | Dropdowns, popovers | `0 4px 12px rgba(0,0,0,0.08)` | `BlurRadius=12, ShadowDepth=4, Opacity=0.08` |
| 3 | Modales | `0 20px 40px rgba(0,0,0,0.12)` | `BlurRadius=40, ShadowDepth=20, Opacity=0.12` |

---

## 8. Componentes

### Botones
| Tipo | Fondo | Texto | Borde |
|---|---|---|---|
| Primario | `#4F46E5` | `#FFFFFF` | ninguno |
| Secundario | `#FFFFFF` | `#4F46E5` | `1px #4F46E5` |
| Terciario / texto | transparente | `#4F46E5` | ninguno |
| Destructivo | `#DC2626` | `#FFFFFF` | ninguno |
| Deshabilitado | `#E5E5E4` | `#B8B8B4` | ninguno |

- Altura: 40px estándar, 32px compacto, 48px prominente
- Hover: oscurecer fondo 8%
- Radius: 8px
- Font: 13px medium

### Inputs
- Altura: 40px
- Border: `1px #D4D4D3`
- Padding: `10px 12px`
- Focus: border `#4F46E5` con ring `0 0 0 3px #E0E4FD`
- Placeholder: color `#888780`
- Label arriba del input (nunca al lado)

### Cards
- Fondo blanco
- Border `0.5px #E5E5E4`
- Radius 12px
- Sombra nivel 1
- Padding 20px

### Tablas (crítico para este proyecto)
Es el componente que más se usa. Debe ser denso pero legible.

- **Encabezado:** fondo `#F4F4F2`, texto micro-label uppercase 10px semibold color `#555553`, padding 12px, sin border-bottom visible
- **Filas:** altura 44px, border-bottom `0.5px #E5E5E4`
- **Hover row:** fondo `#FAFAF9`
- **Selected row:** fondo `#EEF0FE` (indigo 50)
- **Zebra striping:** NO. Ensucia visualmente y no es necesario con altura de fila adecuada.
- **Números:** alineados a la derecha, tabular-nums
- **Estado (pills):** siempre en su propia columna, alineada al centro
- **Acciones por fila:** íconos línea 16px alineados a la derecha, visibles en hover

### Pills de estado
- Padding `3px 10px`
- Radius 6px
- Font 11px semibold
- Uso obligatorio para: estado de préstamo, estado de cuota, método de pago
- Un solo estado por pill (nunca combinar)

### KPI Cards (dashboard)
Estructura vertical:
1. **Micro-label** arriba (10px uppercase, color `#888780`)
2. **Número principal** grande (28px semibold, color `#1A1A1A`, tabular-nums)
3. **Delta** debajo (12px, con flecha `↑` verde `#059669` o `↓` rojo `#DC2626`)
4. **Ícono opcional** flotando a la derecha, dentro de círculo `#EEF0FE` de 40px con ícono indigo

---

## 9. Layout de pantallas clave

### Dashboard
- Sidebar izquierdo fijo de 240px con navegación
- Área principal con padding 32px
- Fila superior: 4 KPI cards en grid horizontal (Capital colocado, Cobros del mes, Morosidad, Clientes activos)
- Fila media: panel de alertas de cobro (izquierda 60%) + gráfico de tendencia (derecha 40%)
- Fila inferior: tabla de últimos movimientos

### Listado (clientes / préstamos)
- Toolbar superior: búsqueda a la izquierda, filtros al centro, botón "Nuevo" a la derecha
- Tabla llena el resto de la pantalla
- Paginación en la esquina inferior derecha
- Contador de resultados abajo izquierda ("Mostrando 1-20 de 147")

### Ficha de cliente
- Header con nombre grande + acciones (Editar, Nuevo préstamo) a la derecha
- Cinco métricas resumen en cards horizontales (Total prestado, Total cobrado, Saldo pendiente, Cuotas al día, Cuotas vencidas)
- Tabs: Información / Préstamos / Historial de pagos / Documentos

### Formulario de préstamo nuevo
- Ancho máximo 640px, centrado
- Grupos de campos con título 14px semibold
- Vista previa en vivo de tabla de amortización debajo del formulario
- Botones fijos abajo (Cancelar / Crear préstamo)

### Tabla de amortización
- Encabezado sticky
- Columnas: N° cuota, Fecha, Capital, Interés, Cuota, Saldo restante, Estado
- Última fila (totales) con fondo `#F4F4F2` y peso semibold
- Filas de cuotas vencidas con indicador rojo lateral izquierdo (border-left `3px #DC2626`)

---

## 10. Iconografía

- **Librería:** Lucide Icons (línea, no rellenos)
- **Tamaño estándar:** 16px en tablas y botones, 20px en navegación, 24px en headers
- **Grosor de línea:** 1.5px
- **Color:** hereda del texto contextual, nunca color propio
- **Nunca:** emojis, íconos rellenos, íconos decorativos sin función

---

## 11. Voz y tono (microcopy)

- **Español dominicano profesional**, sin regionalismos exagerados
- **Directo pero cálido**, nunca corporativo hueco
- **Términos financieros correctos:** capital, interés, cuota, saldo, mora, amortización, abono, adelanto, liquidación
- **Botones en verbo imperativo:** "Registrar pago", "Crear préstamo", "Imprimir recibo"
- **Estados en adjetivo:** "Al día", "Vencido", "En mora"
- **Nunca:** "Guardar transacción", "Estado: activo", "Procesar operación"
- **Sí:** "Registrar pago", "Cliente al día", "Cobrar cuota"

---

## 12. Anti-patrones (prohibido)

- ❌ Gradientes de fondo
- ❌ Glassmorphism, blur effects, transparencias
- ❌ Colores neón, fluorescentes, saturados al máximo
- ❌ Tipografía condensada para números
- ❌ Íconos rellenos o decorativos
- ❌ Botones con esquinas totalmente redondeadas (pill shape) fuera de badges
- ❌ Sombras dramáticas, brillos, glow effects
- ❌ Emojis en la interfaz
- ❌ Modo oscuro (por ahora; puede agregarse en v2)
- ❌ Animaciones largas (>300ms) o de rebote
- ❌ Zebra striping en tablas
- ❌ Íconos de marca (Windows XP style) en botones

---

## 13. Adaptación a WPF

Este proyecto se implementará en WPF/XAML. Consideraciones al traducir el diseño:

- **Fuente Inter:** debe empaquetarse con la aplicación (`.ttf` embebida) con fallback a `Segoe UI`
- **CornerRadius:** WPF admite valores por esquina individual — usar libremente
- **DropShadowEffect:** equivalente a las sombras web indicadas en la tabla del punto 7
- **Colores en hex:** WPF acepta `#RRGGBB` directamente en atributos `Background`, `Foreground`, etc.
- **Padding vs Margin:** WPF distingue; usar `Padding` para espacio interno de contenedores y `Margin` para separación entre elementos
- **Densidad:** WPF por defecto es más denso que UWP/Fluent Design; mantener esa densidad, no forzar el estilo "tocar con dedo"
- **Toda medida en px** se traduce 1:1 a device-independent pixels (DIP) en WPF

---

## 14. Consideraciones de impresión

Los recibos de pago se imprimen físicamente. El diseño de impresión debe:

- Funcionar en **papel térmico 80mm** (recibos) y **papel carta** (reportes)
- Ser **monocromo-safe:** no depender de colores para transmitir información
- Usar la misma tipografía (Inter) para consistencia
- Jerarquía por tamaño y peso, no por color
- Incluir siempre: fecha, número de comprobante, datos del cliente, detalle, total, firma

---

## 15. Uso de este documento

Este archivo se carga en Claude Design al inicio de cada proyecto para que todos los mockups mantengan consistencia. Cuando se pida un diseño nuevo, se debe:

1. Cargar este `DESIGN.md` como contexto
2. Describir la pantalla o componente específico a diseñar
3. Referenciar componentes de esta guía por nombre ("usa una KPI card", "aplica el estado 'En mora'")
4. Iterar en pequeños incrementos, no en una sola instrucción gigante
