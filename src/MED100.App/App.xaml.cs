using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using MED100.Common;
using MED100.Data;
using MED100.Services;
using MED100.ViewModels;
using MED100.Views;
using Serilog;

namespace MED100.App;

/// <summary>
/// Bootstrap: Serilog + contenedor de dependencias + flujo login → shell.
/// ShutdownMode es OnExplicitShutdown porque cerramos la LoginWindow
/// antes de abrir el MainWindow. Patrón heredado de PrestControl v1.0.1
/// (diagnóstico de BD antes de mostrar ventanas).
/// </summary>
public partial class App : Application
{
    // Se asigna en OnStartup, antes de cualquier uso
    private ServiceProvider _servicios = null!;

    /// <summary>
    /// La señal de "esta aplicación está abierta" que mira el instalador.
    ///
    /// El nombre tiene que ser EXACTAMENTE el mismo que AppMutexNombre en
    /// installer/MED100.iss. Inno Setup no lo abre para bloquear nada: mira si
    /// existe, y si existe se niega a instalar y le pide al usuario que cierre
    /// el programa.
    ///
    /// Existe por lo que pasó en FAControl el 2026-09-05: se actualizó con la
    /// aplicación abierta, Windows no pudo reemplazar las DLL en uso y las
    /// difirió al próximo reinicio, pero el asistente igual dijo que había
    /// terminado bien. El cliente siguió con la versión vieja creyendo que
    /// tenía la nueva, y se descubrió días después.
    ///
    /// Va con prefijo Global\ para que se vea entre sesiones de Windows: en la
    /// clínica la recepcionista de la mañana y la de la tarde entran con
    /// cuentas distintas, y una sesión bloqueada con la app abierta es
    /// justamente el caso que hay que detectar.
    ///
    /// NO se libera nunca a mano: el sistema operativo lo suelta al terminar el
    /// proceso, incluso si la aplicación se cae. Un mutex que quedara tomado
    /// tras un cierre sucio haría imposible actualizar hasta reiniciar.
    /// </summary>
    private static readonly System.Threading.Mutex InstanciaAbierta =
        new(initiallyOwned: false, name: @"Global\MediControl.App.Instancia");

    /// <summary>
    /// Red de seguridad: cualquier error que nadie haya atrapado se registra y
    /// se le muestra al usuario, en vez de cerrar la aplicación de golpe.
    ///
    /// Existe porque pasó en la suite: un error al subir un archivo cerraba el
    /// programa sin dejar rastro, y en la maquina del cliente eso es
    /// indistinguible de "el programa se rompió". Perder una pantalla es
    /// molesto; perder la aplicacion entera en medio de una venta, con el
    /// cliente esperando en el mostrador, es otra cosa.
    /// </summary>
    private void ConfigurarRedDeSeguridad()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            Log.Error(e.Exception, "Error no controlado en la interfaz");
            MessageBox.Show(
                "Ocurrió un error inesperado.\n\n" + e.Exception.Message +
                "\n\nLa aplicación sigue abierta. Si se repite, avisá al soporte: " +
                MED100.Common.Soporte.Telefono,
                AppInfo.Nombre, MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;   // no se cierra la app
        };

        // Tareas en segundo plano cuyo error nadie observó: se registran, pero
        // no se le avisa al usuario — no interrumpen nada que él esté haciendo.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error(e.Exception, "Error no observado en una tarea en segundo plano");
            e.SetObserved();
        };
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Toca el mutex para forzar su creación acá, en el arranque, y no en
        // algún momento indeterminado más adelante. Es la señal que mira el
        // instalador para saber que la aplicación está abierta.
        GC.KeepAlive(InstanciaAbierta);

        ConfigurarSerilog();
        ConfigurarRedDeSeguridad();
        _servicios = ConfigurarServicios();

        Log.Information("MED-100 iniciando");

        if (!await PrepararBaseDatosAsync())
        {
            Shutdown();
            return;
        }

        // Licencia: demo de 15 días o completa. Va ANTES del login porque el
        // bloqueo no es "no podés vender": es "esta copia no se abre".
        if (!await ComprobarLicenciaAsync())
        {
            Shutdown();
            return;
        }

        // Configuración del negocio en memoria (ITBIS, numeración, cliente en venta)
        await _servicios.GetRequiredService<ConfiguracionNegocioService>().CargarAsync();

        ConectarModulos();

        MostrarLogin(primerArranque: true);
    }

    /// <summary>
    /// Abre la ventana de login. Se usa al arrancar y también en el cambio
    /// rápido de usuario: por eso LoginWindow es transient (una ventana WPF
    /// cerrada no se puede volver a mostrar).
    /// </summary>
    private void MostrarLogin(bool primerArranque)
    {
        var login = _servicios.GetRequiredService<LoginWindow>();
        var loginVm = (LoginViewModel)login.DataContext;
        var entro = false;

        loginVm.LoginExitoso += (_, _) =>
        {
            entro = true;
            AbrirShell(login, primerArranque);
        };

        // Si cierra el login sin entrar, no hay sesión: la app termina
        login.Closed += (_, _) =>
        {
            if (!entro)
                Shutdown();
        };

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        login.Show();
    }

    private void AbrirShell(Window login, bool primerArranque)
    {
        var shell = _servicios.GetRequiredService<MainWindow>();
        var ajustes = _servicios.GetRequiredService<MED100.Common.AjustesLocales>();
        var mainVm = _servicios.GetRequiredService<MainViewModel>();

        // Tamaño de texto guardado + reacción a cambios desde Configuración
        shell.AplicarEscala(ajustes.FactorEscala);

        // Recalcula permisos, sidebar y página inicial del usuario que acaba de entrar
        mainVm.Inicializar();

        MainWindow = shell;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        shell.Show();
        login.Close();

        if (!primerArranque)
            return;

        // Solo la primera vez: suscripciones y tareas de arranque
        var configuracion = _servicios.GetRequiredService<ConfiguracionViewModel>();
        configuracion.EscalaCambiada += shell.AplicarEscala;

        // "Escribir la llave del producto" abre la MISMA ventana que sale al
        // vencerse la prueba. Un solo formulario para el código.
        configuracion.ActivacionSolicitada += () =>
        {
            var ventana = _servicios.GetRequiredService<ActivacionWindow>();
            ventana.Owner = shell;
            ventana.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ventana.ShowDialog();
        };

        shell.CambiarUsuarioSolicitado += async () => await CambiarUsuarioAsync(shell);

        // Export automático a Excel (si está activo y toca) — en segundo plano
        _ = _servicios.GetRequiredService<ExportacionService>().EjecutarAutomaticoSiTocaAsync(ajustes);

        // Aviso de caducidad al dueno (si esta activo y toca) — en segundo
        // plano. Un fallo de correo NUNCA puede impedir vender: el servicio
        // traga y registra.
        _ = _servicios.GetRequiredService<RecordatorioCaducidadService>().EjecutarAutomaticoSiTocaAsync();

        // Recordatorios de cita al paciente. Van aparte del aviso de
        // caducidad: aquel es UN correo al dueno, este es uno por paciente.
        _ = _servicios.GetRequiredService<RecordatorioCitasService>().EjecutarAutomaticoSiTocaAsync();

        // Cierre automático de caja a la hora configurada
        _servicios.GetRequiredService<CierreAutomatico>().Iniciar();
    }

    /// <summary>
    /// Cambio rápido de usuario (pedido Yuber 2026-07-12): cierra la sesión
    /// actual y pide credenciales otra vez, sin cerrar la aplicación. Útil en el
    /// relevo de turno. Si el nuevo usuario no entra, la app se cierra: nunca
    /// queda una pantalla abierta con la sesión de otro.
    /// </summary>
    private async Task CambiarUsuarioAsync(MainWindow shell)
    {
        try
        {
            await _servicios.GetRequiredService<AuthService>().LogoutAsync();
            shell.Hide();
            MostrarLogin(primerArranque: false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cambiando de usuario");
            MessageBox.Show($"No se pudo cambiar de usuario.\n\n{ex.Message}", AppInfo.Nombre,
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Registra los VMs reales de cada módulo en el shell y cablea la
    /// navegación de los formularios (lista ↔ nuevo/editar).
    /// </summary>
    private void ConectarModulos()
    {
        var main = _servicios.GetRequiredService<MainViewModel>();
        var clientes = _servicios.GetRequiredService<ClientesViewModel>();
        var clienteForm = _servicios.GetRequiredService<ClienteFormViewModel>();
        var productos = _servicios.GetRequiredService<ProductosViewModel>();
        var productoForm = _servicios.GetRequiredService<ProductoFormViewModel>();
        var vender = _servicios.GetRequiredService<VenderViewModel>();

        main.RegistrarPagina(Pagina.Vender, vender);
        main.RegistrarPagina(Pagina.Clientes, clientes);
        main.RegistrarPagina(Pagina.Medicos, _servicios.GetRequiredService<MedicosViewModel>());
        main.RegistrarPagina(Pagina.Procedimientos, _servicios.GetRequiredService<ProcedimientosViewModel>());
        main.RegistrarPagina(Pagina.Citas, _servicios.GetRequiredService<CitasViewModel>());
        main.RegistrarPagina(Pagina.Turnos, _servicios.GetRequiredService<TurnosViewModel>());
        main.RegistrarPagina(Pagina.Expedientes, _servicios.GetRequiredService<ExpedientesViewModel>());
        main.RegistrarPagina(Pagina.Productos, productos);
        main.RegistrarPagina(Pagina.Almacen, _servicios.GetRequiredService<AlmacenViewModel>());
        main.RegistrarPagina(Pagina.Caducidad, _servicios.GetRequiredService<CaducidadViewModel>());
        main.RegistrarPagina(Pagina.Fiados, _servicios.GetRequiredService<FiadosViewModel>());
        main.RegistrarPagina(Pagina.Indicaciones, _servicios.GetRequiredService<IndicacionesViewModel>());
        main.RegistrarPagina(Pagina.Panel, _servicios.GetRequiredService<PanelViewModel>());
        main.RegistrarPagina(Pagina.Reportes, _servicios.GetRequiredService<ReportesViewModel>());
        main.RegistrarPagina(Pagina.Cuadre, _servicios.GetRequiredService<CuadreViewModel>());
        main.RegistrarPagina(Pagina.Usuarios, _servicios.GetRequiredService<UsuariosViewModel>());
        main.RegistrarPagina(Pagina.Configuracion, _servicios.GetRequiredService<ConfiguracionViewModel>());

        // Agenda → caja: la cita se cobra con todo cargado y, al emitir, la
        // factura queda unida a la cita (cita.factura_id).
        var citas = _servicios.GetRequiredService<CitasViewModel>();
        citas.CobroSolicitado += cita => _ = AbrirEdicionAsync(async () =>
        {
            await vender.PrepararDesdeCitaAsync(cita);
            main.VolverA(Pagina.Vender);
        });

        // Turnos: el papelito del turno es un documento APARTE del recibo.
        // Se imprime directo en el mostrador (el paciente está esperando) y con
        // vista previa cuando es una reimpresión a pedido.
        var turnos = _servicios.GetRequiredService<TurnosViewModel>();
        turnos.ImpresionSolicitada += (turno, etiqueta, directo) =>
            ImprimirTurno(turno, etiqueta, directo);

        // Cuadre: el cierre SIEMPRE se previsualiza antes de imprimir
        var cuadre = _servicios.GetRequiredService<CuadreViewModel>();
        cuadre.ImpresionSolicitada += (cierre, tamano) => MostrarCierre(cierre, tamano);

        // Comprobantes: reimprimir usa el MISMO ticket que la venta original
        var comprobantes = _servicios.GetRequiredService<ComprobantesViewModel>();
        main.RegistrarPagina(Pagina.Comprobantes, comprobantes);
        comprobantes.ReimpresionSolicitada += factura => MostrarTicket(factura, esReimpresion: true);
        // Archivar a mano una factura vieja: las emitidas antes del archivado
        // automático (2026-08-15) no tienen copia en el expediente.
        comprobantes.ArchivadoSolicitado += factura => _ = ArchivarFacturaAMano(factura);

        // Guardar la configuración (ITBIS, cliente en venta, datos del negocio)
        // repercute de inmediato en Vender aunque ya estuviera cargada
        _servicios.GetRequiredService<ConfiguracionNegocioService>().Cambiada +=
            () => _ = vender.RefrescarAsync();

        // Vender → ticket (la venta ya está persistida; imprimir puede fallar sin riesgo)
        vender.VentaRegistrada += resultado => MostrarTicket(resultado, esReimpresion: false);

        // Pacientes: lista ↔ formulario
        clientes.NuevoSolicitado += () => _ = AbrirEdicionAsync(async () =>
        {
            // Async porque el formulario carga el catálogo de procedencias.
            await clienteForm.PrepararNuevoAsync();
            main.MostrarSubpagina(Pagina.Clientes, clienteForm, "Nuevo paciente");
        });
        clientes.EdicionSolicitada += id => _ = AbrirEdicionAsync(async () =>
        {
            await clienteForm.PrepararEdicionAsync(id);
            main.MostrarSubpagina(Pagina.Clientes, clienteForm, "Editar paciente");
        });
        clienteForm.Guardado += _ => main.VolverA(Pagina.Clientes);
        clienteForm.Cancelado += () => main.VolverA(Pagina.Clientes);

        // Pacientes: lista → ficha con el historial (citas, turnos, facturas y
        // procedimientos). Desde la ficha se puede saltar a editar sin volver.
        var pacienteFicha = _servicios.GetRequiredService<PacienteFichaViewModel>();
        clientes.FichaSolicitada += id => _ = AbrirEdicionAsync(async () =>
        {
            await pacienteFicha.CargarAsync(id);
            main.MostrarSubpagina(Pagina.Clientes, pacienteFicha, "Ficha del paciente");
        });
        pacienteFicha.Cerrado += () => main.VolverA(Pagina.Clientes);
        pacienteFicha.EdicionSolicitada += id => _ = AbrirEdicionAsync(async () =>
        {
            await clienteForm.PrepararEdicionAsync(id);
            main.MostrarSubpagina(Pagina.Clientes, clienteForm, "Editar paciente");
        });
        // Ficha → agenda: la cita se pone con el paciente ya elegido, sin
        // volver a buscarlo (pedido de la clínica 2026-08-27).
        pacienteFicha.CitaSolicitada += id => _ = AbrirEdicionAsync(async () =>
        {
            // Primero se arma el formulario y DESPUÉS se navega: Navegar dispara
            // su propio RefrescarAsync sin esperarlo, y arrancar el nuestro
            // encima dejaría dos recorriendo las mismas colecciones.
            await citas.PrepararNuevaParaPacienteAsync(id);
            main.VolverA(Pagina.Citas);
        });

        // Almacén de expedientes: lista → expediente de un paciente, y desde
        // ahí un atajo a su ficha completa (citas, facturas, procedimientos).
        var expedientes = _servicios.GetRequiredService<ExpedientesViewModel>();
        var expedientePaciente = _servicios.GetRequiredService<ExpedientePacienteViewModel>();
        expedientes.ExpedienteSolicitado += id => _ = AbrirEdicionAsync(async () =>
        {
            await expedientePaciente.CargarAsync(id);
            main.MostrarSubpagina(Pagina.Expedientes, expedientePaciente, "Expediente del paciente");
        });
        expedientePaciente.VolverSolicitado += () => main.VolverA(Pagina.Expedientes);
        expedientePaciente.FichaSolicitada += id => _ = AbrirEdicionAsync(async () =>
        {
            await pacienteFicha.CargarAsync(id);
            main.MostrarSubpagina(Pagina.Expedientes, pacienteFicha, "Ficha del paciente");
        });

        // Productos: lista ↔ formulario
        productos.NuevoSolicitado += () =>
        {
            productoForm.PrepararNuevo();
            main.MostrarSubpagina(Pagina.Productos, productoForm, "Nuevo producto");
        };
        productos.EdicionSolicitada += id => _ = AbrirEdicionAsync(async () =>
        {
            await productoForm.PrepararEdicionAsync(id);
            main.MostrarSubpagina(Pagina.Productos, productoForm, "Editar producto");
        });
        productoForm.Guardado += _ => main.VolverA(Pagina.Productos);
        productoForm.Cancelado += () => main.VolverA(Pagina.Productos);
    }

    /// <summary>
    /// Genera el ticket y lo imprime. Camino ÚNICO para la venta recién cobrada
    /// y para la reimpresión desde Comprobantes: el papel sale idéntico.
    /// Por defecto imprime directo, sin preguntar (pedido Yuber 2026-07-12);
    /// la vista previa es opcional y actúa de plan B si la impresora falla.
    /// La factura YA está en la base de datos: nada de esto la puede afectar.
    /// </summary>
    private void MostrarTicket(MED100.Models.VentaResultado factura, bool esReimpresion)
    {
        var descripcion = (esReimpresion ? "Reimpresión " : "Ticket ") + factura.NumeroFactura;
        try
        {
            var negocio = _servicios.GetRequiredService<ConfiguracionNegocioService>().Actual;
            var ajustes = _servicios.GetRequiredService<MED100.Common.AjustesLocales>();
            var visual = MED100.Printing.TicketVisualFactory.Crear(
                factura, negocio, MED100.Common.SesionActual.Nombre,
                ajustes.TicketEncabezado, ajustes.TicketPie);

            // Copia en PDF al expediente del paciente (pedido 2026-08-15).
            // SOLO al emitir: reimprimir es buscar un papel que ya existe, y
            // archivar de nuevo llenaría el expediente de duplicados de la misma
            // factura. Para las facturas viejas está el botón de Comprobantes.
            if (!esReimpresion && negocio.ArchivarFacturaPdf && factura.ClienteId is { } pacienteId)
                ArchivarPdfEnExpediente(visual, pacienteId,
                    $"Factura {factura.NumeroFactura}",
                    MED100.Models.TipoDocumentoPaciente.Factura,
                    $"Comprobante {factura.NumeroFactura} archivado al emitirlo");

            // Al reimprimir siempre se muestra la vista previa: el cajero está
            // buscando ese comprobante, no cobrando a un cliente que espera
            if (esReimpresion || ajustes.MostrarVistaPreviaTicket)
            {
                new TicketWindow(visual, descripcion, ajustes.CopiasTicket)
                {
                    Owner = MainWindow
                }.ShowDialog();
                return;
            }

            try
            {
                MED100.Printing.ImpresoraTickets.ImprimirDirecto(
                    visual, descripcion, ajustes.CopiasTicket, ajustes.ImpresoraPredeterminada);
            }
            catch (Exception exImpresion)
            {
                Log.Warning(exImpresion, "Falló la impresión directa de {Descripcion}", descripcion);
                new TicketWindow(visual, descripcion, ajustes.CopiasTicket)
                {
                    Owner = MainWindow
                }.ShowDialog();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error generando el ticket {Numero}", factura.NumeroFactura);
            MessageBox.Show(
                $"La factura {factura.NumeroFactura} está registrada, pero no se pudo " +
                $"generar el ticket.\n\n{ex.Message}", AppInfo.Nombre,
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Imprime el papelito del turno. Si falla la impresora NO se pierde el
    /// turno: ya está guardado y numerado, y se puede reimprimir desde la
    /// pantalla. Es la misma regla que rige el ticket de venta.
    /// </summary>
    private void ImprimirTurno(MED100.Models.Turno turno, string etiqueta, bool directo)
    {
        try
        {
            var negocio = _servicios.GetRequiredService<ConfiguracionNegocioService>().Actual;
            var visual = MED100.Printing.TurnoVisualFactory.Crear(turno, etiqueta, negocio);
            var descripcion = $"Turno {etiqueta}";

            // Apagado por defecto: el turno es un papelito que se tira al salir.
            // Está disponible para quien lo quiera (Configuración → Respaldo).
            if (negocio.ArchivarTurnoPdf && turno.ClienteId is { } pacienteId)
                ArchivarPdfEnExpediente(visual, pacienteId, $"Turno {etiqueta}",
                    MED100.Models.TipoDocumentoPaciente.Otro,
                    $"Turno {etiqueta} del {turno.Fecha:dd/MM/yyyy}");

            if (directo)
            {
                var ajustes = _servicios.GetRequiredService<MED100.Common.AjustesLocales>();
                MED100.Printing.ImpresoraTickets.ImprimirDirecto(visual, descripcion,
                    copias: 1, nombreImpresora: ajustes.ImpresoraPredeterminada);
                return;
            }

            new VistaPreviaWindow(visual, $"Turno {etiqueta}", descripcion)
            {
                Owner = MainWindow
            }.ShowDialog();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error imprimiendo el turno {Etiqueta}", etiqueta);
            MessageBox.Show(
                $"El turno {etiqueta} quedó guardado, pero no se pudo imprimir.\n\n{ex.Message}\n\n" +
                "Podés reimprimirlo desde la pantalla de la sala de espera.",
                AppInfo.Nombre, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Guarda el documento como PDF en el expediente del paciente.
    ///
    /// Va en segundo plano y NO avisa de nada: el documento ya se emitió y se
    /// está imprimiendo, que es lo que el usuario pidió. Si la copia falla, el
    /// fallo queda en el log y la pantalla sigue como si nada — tirarle un
    /// error encima al cajero, con el paciente esperando el papel, sería peor
    /// que perder una copia que además se puede rehacer desde Comprobantes.
    ///
    /// El PDF pasa por un archivo temporal porque el expediente guarda
    /// ARCHIVOS, no visuales de WPF. Lo COPIA, así que el temporal se borra.
    /// </summary>
    private void ArchivarPdfEnExpediente(System.Windows.FrameworkElement visual, long clienteId,
        string nombreDocumento, MED100.Models.TipoDocumentoPaciente tipo, string? notas)
    {
        // El PDF se genera ACÁ, en el hilo de UI: rasterizar un visual de WPF
        // desde otro hilo no se puede. Lo que se va al fondo es guardarlo.
        string temporal;
        try
        {
            temporal = MED100.Printing.ExportadorPdf.GuardarTemporal(visual,
                $"{nombreDocumento} {DateTime.Now:yyyyMMdd_HHmmss}", nombreDocumento);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se pudo generar el PDF de {Documento}", nombreDocumento);
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await _servicios.GetRequiredService<ExpedienteService>()
                    .ArchivarImpresoAsync(clienteId, temporal, tipo, notas);
            }
            finally
            {
                try { if (File.Exists(temporal)) File.Delete(temporal); }
                catch (IOException) { /* temporal: lo limpia Windows */ }
            }
        });
    }

    /// <summary>
    /// Guarda a mano el PDF de una factura ya emitida en el expediente del
    /// paciente (botón de Comprobantes).
    ///
    /// A diferencia del archivado automático, este SÍ habla: lo pidió una
    /// persona y tiene que saber si salió bien. Y avisa si ya había una copia,
    /// en vez de dejar dos PDF idénticos del mismo comprobante.
    /// </summary>
    private async Task ArchivarFacturaAMano(MED100.Models.VentaResultado factura)
    {
        if (factura.ClienteId is not { } clienteId)
            return;

        var expedientes = _servicios.GetRequiredService<ExpedienteService>();
        var dialogos = _servicios.GetRequiredService<MED100.Common.IDialogService>();
        var nombre = $"Factura {factura.NumeroFactura}";
        string? temporal = null;

        try
        {
            if (await expedientes.YaTieneDocumentoAsync(clienteId, nombre) &&
                !dialogos.Confirmar("Ya está archivada",
                    $"El expediente de {factura.NombreCliente} ya tiene una copia de la " +
                    $"factura {factura.NumeroFactura}.\n\n¿Guardar otra?"))
                return;

            var negocio = _servicios.GetRequiredService<ConfiguracionNegocioService>().Actual;
            var ajustes = _servicios.GetRequiredService<MED100.Common.AjustesLocales>();
            var visual = MED100.Printing.TicketVisualFactory.Crear(
                factura, negocio, MED100.Common.SesionActual.Nombre,
                ajustes.TicketEncabezado, ajustes.TicketPie);

            temporal = MED100.Printing.ExportadorPdf.GuardarTemporal(visual,
                $"{nombre} {DateTime.Now:yyyyMMdd_HHmmss}", nombre);

            await expedientes.AgregarAsync(clienteId, temporal,
                MED100.Models.TipoDocumentoPaciente.Factura,
                $"Comprobante {factura.NumeroFactura} archivado a mano");

            dialogos.Informar("Guardado en el expediente",
                $"La factura {factura.NumeroFactura} quedó guardada en el expediente de " +
                $"{factura.NombreCliente}.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            dialogos.MostrarError("Guardar en el expediente", ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error archivando a mano la factura {Numero}", factura.NumeroFactura);
            dialogos.MostrarError("Guardar en el expediente",
                $"No se pudo guardar la copia.\n\n{ex.Message}");
        }
        finally
        {
            try { if (temporal is not null && File.Exists(temporal)) File.Delete(temporal); }
            catch (IOException) { /* temporal: lo limpia Windows */ }
        }
    }

    /// <summary>
    /// Vista previa imprimible del cierre de caja. A diferencia del ticket de
    /// venta, el cierre SIEMPRE se muestra antes de imprimir (pedido de Yuber)
    /// y respeta el tamaño de papel elegido (80mm o carta).
    /// </summary>
    private void MostrarCierre(MED100.Models.CuadreGeneral cierre, MED100.Models.TamanoImpresion tamano)
    {
        try
        {
            var negocio = _servicios.GetRequiredService<ConfiguracionNegocioService>().Actual;
            var visual = MED100.Printing.CierreVisualFactory.Crear(
                cierre, negocio, MED100.Common.SesionActual.Nombre, tamano);

            new VistaPreviaWindow(visual,
                $"Cierre de caja — {cierre.Fecha:dd/MM/yyyy}",
                $"Cierre {cierre.Fecha:yyyy-MM-dd}")
            {
                Owner = MainWindow
            }.ShowDialog();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error generando el cierre de caja");
            MessageBox.Show($"No se pudo generar el cierre.\n\n{ex.Message}", AppInfo.Nombre,
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Envuelve la apertura async de un formulario para que un error no tumbe la app.</summary>
    private async Task AbrirEdicionAsync(Func<Task> abrir)
    {
        try
        {
            await abrir();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se pudo abrir el formulario de edición");
            MessageBox.Show(ex.Message, AppInfo.Nombre, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Con cuántos días de prueba restantes se le empieza a recordar al cliente
    /// que hay que comprar. Antes de eso la app no molesta: un cartel todos los
    /// días desde el primero se vuelve parte del paisaje y deja de leerse.
    /// </summary>
    private const int DiasParaAvisar = 5;

    /// <summary>
    /// Resuelve la licencia antes de mostrar el login. Devuelve false solo
    /// cuando el demo se acabó y el usuario cerró la ventana sin activar.
    /// </summary>
    private async Task<bool> ComprobarLicenciaAsync()
    {
        var estado = await _servicios.GetRequiredService<LicenciaService>().EvaluarAsync();

        if (estado.Estado == MED100.Models.EstadoLicencia.Completa)
            return true;

        // En demo con margen de sobra: se abre derecho, sin ventana de por medio
        if (estado.Estado == MED100.Models.EstadoLicencia.Demo && estado.DiasRestantes > DiasParaAvisar)
            return true;

        var ventana = _servicios.GetRequiredService<ActivacionWindow>();
        ventana.ShowDialog();
        return ventana.Continuar;
    }

    /// <summary>
    /// Diagnóstico previo al login. Los MessageBox van directo aquí (bootstrap,
    /// capa UI, aún no hay ventanas): IDialogService es para los ViewModels.
    /// Devuelve false cuando la app no debe continuar.
    /// </summary>
    private async Task<bool> PrepararBaseDatosAsync()
    {
        const string titulo = AppInfo.Nombre;
        try
        {
            var verificador = _servicios.GetRequiredService<VerificadorBaseDatos>();
            switch (await verificador.VerificarAsync())
            {
                case EstadoBaseDatos.Lista:
                    // Base de una versión anterior: le falta la tabla licencia,
                    // que se consulta antes del login. Sin esto, actualizar la
                    // app dejaría al cliente sin poder abrirla.
                    if (await verificador.ActualizarEsquemaAsync() is { } motivo)
                    {
                        Log.Error("No se pudo poner al día la base de datos: {Motivo}", motivo);
                        MessageBox.Show(
                            $"La base de datos quedó de una versión anterior de {AppInfo.Nombre} y no se " +
                            "pudo actualizar:\n\n" + motivo + "\n\n" +
                            "Tus datos están intactos. Ejecuta scripts\\db\\008_licencia.sql " +
                            "como root y vuelve a abrir MED-100.",
                            titulo, MessageBoxButton.OK, MessageBoxImage.Error);
                        return false;
                    }
                    return true;

                case EstadoBaseDatos.FaltaBaseDatos:
                    var crear = MessageBox.Show(
                        $"La base de datos de {AppInfo.Nombre} todavía no existe en este equipo.\n\n" +
                        "¿Quieres crearla ahora? Toma solo unos segundos y no afecta nada más del sistema.",
                        titulo + " — Primer arranque",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
                    if (!crear)
                        return false;

                    await verificador.CrearEsquemaAsync();
                    Log.Information("Base de datos creada automáticamente en el primer arranque");
                    MessageBox.Show(
                        "Base de datos creada correctamente. ¡Todo listo para empezar!",
                        titulo, MessageBoxButton.OK, MessageBoxImage.Information);
                    return true;

                case EstadoBaseDatos.CredencialesInvalidas:
                    MessageBox.Show(
                        "MySQL rechazó el usuario o la contraseña configurados.\n\n" +
                        "Revisa la cadena de conexión en MED100.App.dll.config.",
                        titulo, MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;

                default: // SinServidor
                    MessageBox.Show(
                        "No se pudo conectar con MySQL.\n\n" +
                        "Verifica que el servicio MySQL80 esté en ejecución " +
                        "(services.msc → MySQL80 → Iniciar) y vuelve a abrir MED-100.",
                        titulo, MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fallo preparando la base de datos al arrancar");
            MessageBox.Show(
                "No se pudo preparar la base de datos:\n\n" + ex.Message + "\n\n" +
                "Si el usuario configurado no tiene permisos para crear bases de datos, " +
                "ejecuta scripts\\db\\001_create_schema.sql y 002_seed_data.sql como root.",
                titulo, MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Cierre de sesión de respaldo si el usuario cerró la ventana sin logout
        try
        {
            var auth = _servicios.GetService<AuthService>();
            auth?.LogoutAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se pudo registrar el logout al salir");
        }

        Log.Information("MED-100 finalizado");
        Log.CloseAndFlush();
        _servicios.Dispose();
        base.OnExit(e);
    }

    private static void ConfigurarSerilog()
    {
        var carpetaLogs = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(carpetaLogs);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(carpetaLogs, "med100-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30)
            .CreateLogger();
    }

    private static ServiceProvider ConfigurarServicios()
    {
        var servicios = new ServiceCollection();

        // Data
        servicios.AddSingleton<ConexionFactory>();
        servicios.AddSingleton<VerificadorBaseDatos>();
        servicios.AddSingleton<UsuarioRepository>();
        servicios.AddSingleton<SesionRepository>();
        servicios.AddSingleton<AuditoriaRepository>();
        servicios.AddSingleton<ClienteRepository>();
        servicios.AddSingleton<HistorialRepository>();
        servicios.AddSingleton<DocumentoPacienteRepository>();
        servicios.AddSingleton<ReferidorRepository>();
        servicios.AddSingleton<CitaRepository>();
        servicios.AddSingleton<TurnoRepository>();
        servicios.AddSingleton<ArsRepository>();
        servicios.AddSingleton<MedicoRepository>();
        servicios.AddSingleton<ProcedimientoRepository>();
        servicios.AddSingleton<ProductoRepository>();
        servicios.AddSingleton<FacturaRepository>();
        servicios.AddSingleton<NcfRepository>();
        servicios.AddSingleton<FiadoRepository>();
        servicios.AddSingleton<IndicacionRepository>();
        servicios.AddSingleton<ConfiguracionNegocioRepository>();
        servicios.AddSingleton<CuadreRepository>();
        servicios.AddSingleton<AnaliticaRepository>();
        servicios.AddSingleton<ExportacionRepository>();
        servicios.AddSingleton<LicenciaRepository>();

        // Services
        servicios.AddSingleton<AuditoriaService>();
        servicios.AddSingleton<AuthService>();
        servicios.AddSingleton<ClienteService>();
        servicios.AddSingleton<ReferidorService>();
        servicios.AddSingleton<CitaService>();
        servicios.AddSingleton<TurnoService>();
        servicios.AddSingleton<ExpedienteService>();
        servicios.AddSingleton<ArsService>();
        servicios.AddSingleton<RecordatorioCitasService>();
        servicios.AddSingleton<MedicoService>();
        servicios.AddSingleton<ProcedimientoService>();
        servicios.AddSingleton<ProductoService>();
        servicios.AddSingleton<ConfiguracionNegocioService>();
        servicios.AddSingleton<NcfService>();
        servicios.AddSingleton<FiadoService>();
        servicios.AddSingleton<IndicacionService>();
        servicios.AddSingleton<VentaService>();
        servicios.AddSingleton<FacturaService>();
        servicios.AddSingleton<CuadreService>();
        servicios.AddSingleton<AnaliticaService>();
        servicios.AddSingleton<ExportacionService>();
        servicios.AddSingleton<EmailService>();
        servicios.AddSingleton<RecordatorioCaducidadService>();
        servicios.AddSingleton(sp =>
            new RespaldoService(sp.GetRequiredService<ConexionFactory>().CadenaConexion));
        servicios.AddSingleton<CierreAutomatico>();
        servicios.AddSingleton<UsuarioService>();
        // Licencia: el ancla del demo vive en %ProgramData%, fuera de la base
        servicios.AddSingleton<MED100.Common.AnclaLicencia>();
        servicios.AddSingleton<LicenciaService>();
        servicios.AddSingleton(MED100.Common.AjustesLocales.Cargar());
        servicios.AddSingleton<MED100.Common.IDialogService, DialogService>();

        // ViewModels
        servicios.AddTransient<LoginViewModel>();   // nueva en cada login (cambio de usuario)
        servicios.AddSingleton<MainViewModel>();
        servicios.AddSingleton<ClientesViewModel>();
        servicios.AddSingleton<ClienteFormViewModel>();
        servicios.AddSingleton<PacienteFichaViewModel>();
        servicios.AddSingleton<ExpedientesViewModel>();
        servicios.AddSingleton<ExpedientePacienteViewModel>();
        servicios.AddSingleton<MedicosViewModel>();
        servicios.AddSingleton<ProcedimientosViewModel>();
        servicios.AddSingleton<CitasViewModel>();
        servicios.AddSingleton<TurnosViewModel>();
        servicios.AddSingleton<ProductosViewModel>();
        servicios.AddSingleton<ProductoFormViewModel>();
        servicios.AddSingleton<AlmacenViewModel>();
        servicios.AddSingleton<CaducidadViewModel>();
        servicios.AddSingleton<FiadosViewModel>();
        servicios.AddSingleton<IndicacionesViewModel>();
        servicios.AddSingleton<VenderViewModel>();
        servicios.AddSingleton<ComprobantesViewModel>();
        servicios.AddSingleton<CuadreViewModel>();
        servicios.AddSingleton<PanelViewModel>();
        servicios.AddSingleton<ReportesViewModel>();
        servicios.AddSingleton<UsuariosViewModel>();
        servicios.AddSingleton<ConfiguracionViewModel>();
        servicios.AddTransient<ActivacionViewModel>();   // se abre y se cierra, como el login

        // Views
        servicios.AddTransient<LoginWindow>();      // una ventana cerrada no se reabre
        servicios.AddTransient<ActivacionWindow>();
        servicios.AddSingleton<MainWindow>();

        return servicios.BuildServiceProvider();
    }
}
