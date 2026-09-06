; =============================================================
; MediControl — ACTUALIZADOR (Inno Setup 6)
; Compilar:  ISCC.exe MED100_Update.iss
; Requiere:  ..\publish\ generado con:
;   dotnet publish src/MED100.App -c Release -r win-x64 --self-contained true -o publish
;
; POR QUÉ EXISTE
; --------------
; El instalador completo pesa cientos de megas porque arrastra MySQL, AnyDesk y
; Google Drive. Para cambiar solo la aplicación eso es absurdo: obliga a mandar
; el paquete entero por WhatsApp o Drive cada vez que se corrige una pantalla.
;
; Este .exe reemplaza SOLO la aplicación:
;   · no trae MySQL, ni AnyDesk, ni Google Drive
;   · no pregunta carpeta: usa la que ya tiene la instalación
;   · no toca la base de datos ni sus datos
;   · conserva la cadena de conexión (MED100.App.dll.config), la licencia, los
;     ajustes de cada terminal y los expedientes escaneados de los pacientes
;
; El esquema de la base lo pone al día la propia aplicación en el primer
; arranque (VerificadorBaseDatos.ActualizarEsquemaAsync): agrega tablas y
; columnas nuevas, nunca borra. Por eso el actualizador NO necesita la
; contraseña de MySQL.
;
; SI MediControl NO ESTÁ INSTALADO, este .exe se niega a correr y manda a usar
; MediControl_Setup_x.y.z.exe. Instalar "la actualización" sobre una PC limpia
; dejaría la aplicación sin MySQL, que es el error más caro de diagnosticar:
; parece un problema del programa y en realidad falta la base de datos.
;
; LA LECCIÓN DEL 2026-09-05 (FAControl)
; ------------------------------------
; Una actualización "terminó bien" y la aplicación siguió siendo la vieja: los
; archivos estaban en uso, Windows los difirió al próximo reinicio y el
; asistente igual dijo que había terminado. El cliente se enteró días después.
; De ahí salen las tres defensas de este archivo: AppMutex, CloseApplications y
; la comprobación de versión al final.
; =============================================================

#define AppNombre "MediControl"
#define AppVersion "1.2.0"
#define AppEditor "Yuber Santana"
#define AppExe "MED100.App.exe"
#define AppTelefono "849-438-0242"
#define AppMutexNombre "Global\MediControl.App.Instancia"

[Setup]
; MISMO AppId que el instalador: así Windows lo ve como la misma aplicación, el
; actualizador hereda la carpeta ya elegida y no aparece dos veces en "Agregar
; o quitar programas". NO se toca aunque cambie el nombre del producto.
AppId={{9A4C7D31-2E68-4B15-A0F9-MED100SYSTEM}
AppName={#AppNombre}
AppVersion={#AppVersion}
AppPublisher={#AppEditor}
AppSupportPhone={#AppTelefono}
; Primera defensa: si la aplicación está abierta, ni empieza.
AppMutex={#AppMutexNombre}
DefaultDirName={autopf}\{#AppNombre}
DefaultGroupName={#AppNombre}
OutputDir=Output
OutputBaseFilename=MediControl_Update_{#AppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
WizardStyle=modern
SetupIconFile=..\src\MED100.App\Assets\med100.ico
UninstallDisplayIcon={app}\{#AppExe}

; --- Lo que hace corta la actualización ---
; UsePreviousAppDir es el valor por defecto, pero se declara: es LA razón de que
; se pueda saltear la pantalla de carpeta sin riesgo de instalar en otro lado.
UsePreviousAppDir=yes
UsePreviousGroup=yes
UsePreviousTasks=yes
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
DisableWelcomePage=no

; Segunda defensa: si algún archivo quedó tomado, Inno lo detecta por Restart
; Manager, ofrece cerrar el programa y lo vuelve a abrir al terminar — en vez de
; pedir reiniciar Windows y dejar la copia a medias.
CloseApplications=yes
CloseApplicationsFilter=*.exe,*.dll
RestartApplications=yes

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Messages]
spanish.WelcomeLabel1=Actualizar [name]
spanish.WelcomeLabel2=IMPORTANTE: cierre [name] antes de continuar. Si el programa está abierto, Windows no puede reemplazar sus archivos y la actualización queda a medias.%n%nSe va a actualizar [name] a la versión {#AppVersion} en este equipo.%n%nSolo se reemplaza el programa. NO se toca la base de datos: los pacientes, las citas, las facturas y los documentos escaneados quedan igual. La licencia y los ajustes también se conservan.%n%nAl terminar, la pantalla de inicio muestra abajo "Versión {#AppVersion}": ahí se confirma que entró.
spanish.FinishedLabel=La actualización terminó. Al abrir [name] por primera vez, el sistema acomoda la base de datos solo; puede tardar unos segundos más de lo normal.%n%nCompruebe abajo en la pantalla de inicio que diga "Versión {#AppVersion}".

[Tasks]
; Va sin marcar: el acceso directo del escritorio ya existe de la instalación
; original. Está por si el cliente lo borró y lo quiere de vuelta.
Name: "escritorio"; Description: "Volver a crear el acceso directo en el escritorio"; \
  GroupDescription: "Accesos directos:"; Flags: unchecked

[Files]
; La aplicación publicada. `ignoreversion` es deliberado: los archivos
; self-contained de .NET no siempre suben de versión entre builds, y sin esto
; Inno podría dejar el viejo por creerlo igual de nuevo.
Source: "..\publish\*"; DestDir: "{app}"; \
  Excludes: "MED100.App.dll.config"; \
  Flags: ignoreversion recursesubdirs createallsubdirs

; ⚠ LA CADENA DE CONEXIÓN NO SE PISA. `onlyifdoesntexist` es lo único que
; separa una actualización limpia de dejar al cliente sin poder abrir el
; programa: si se sobrescribiera con la del desarrollo, apuntaría a una base
; que en esa PC no existe.
Source: "..\publish\MED100.App.dll.config"; DestDir: "{app}"; \
  Flags: onlyifdoesntexist uninsneveruninstall

; Los scripts de base de datos se actualizan igual. No se ejecutan solos —la app
; aplica lo que falta al arrancar—, pero tienen que estar por si hay que correr
; una migración a mano por Workbench. Se excluye 999_rollback.sql a propósito:
; BORRA la base entera y no tiene nada que hacer en la máquina del cliente.
Source: "..\scripts\db\*.sql"; DestDir: "{app}\scripts\db"; \
  Excludes: "999_rollback.sql"; \
  Flags: ignoreversion
Source: "..\docs\INSTALL.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\MANUAL.md"; DestDir: "{app}\docs"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppNombre}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Manual de usuario"; Filename: "{app}\docs\MANUAL.md"
Name: "{autodesktop}\{#AppNombre}"; Filename: "{app}\{#AppExe}"; Tasks: escritorio

[Run]
Filename: "{app}\{#AppExe}"; Description: "Abrir {#AppNombre} ahora"; \
  Flags: nowait postinstall skipifsilent

; [Code] va ÚLTIMO: todo lo que sigue a esta línea se lee como Pascal.
[Code]

{ Clave de desinstalación que escribió el instalador original. Si no está, en
  esta PC no hay MediControl y este .exe no es el que corresponde.

  El GUID va LITERAL y tiene que coincidir con el AppId de arriba. No se usa el
  preprocesador acá por dos razones: el AppId de Inno lleva las llaves dobles
  del formato de la sección [Setup] y la clave no coincidiría, y además el
  preprocesador expande las directivas INCLUSO dentro de estos comentarios. }
function RutaDesinstalacion(): String;
begin
  Result := 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\' +
            '{9A4C7D31-2E68-4B15-A0F9-MED100SYSTEM}_is1';
end;

function EstaInstalado(): Boolean;
begin
  { Los dos hives: el instalador corre como admin (HKLM), pero una instalación
    vieja "solo para mí" habría quedado en HKCU. }
  Result := RegKeyExists(HKEY_LOCAL_MACHINE, RutaDesinstalacion()) or
            RegKeyExists(HKEY_CURRENT_USER, RutaDesinstalacion());
end;

{ Tercera defensa, y la que de verdad cierra el agujero del 2026-09-05.

  Después de copiar, se lee la versión del .exe que quedó EN DISCO y se compara
  con la que este actualizador traía. Si Windows difirió los archivos al próximo
  reinicio, acá se ve: el ejecutable sigue diciendo la versión vieja.

  Sin esta comprobación el único que se entera es el cliente —días después, y
  creyendo que la versión nueva no sirve. }
procedure CurStepChanged(CurStep: TSetupStep);
var
  Instalada: String;
  Destino: String;
begin
  if CurStep <> ssPostInstall then
    Exit;

  Destino := ExpandConstant('{app}\{#AppExe}');

  if not FileExists(Destino) then
  begin
    MsgBox('La actualización no encontró {#AppNombre} en:' + #13#10 + Destino + #13#10 + #13#10 +
           'Avise al soporte antes de seguir usando el programa: {#AppTelefono}.',
           mbCriticalError, MB_OK);
    Exit;
  end;

  if not GetVersionNumbersString(Destino, Instalada) then
    Exit;   { sin datos de versión no se puede afirmar nada; no se molesta al usuario }

  { OJO: ninguna línea puede EMPEZAR con #13 — el preprocesador de Inno lee un
    '#' al principio de línea como una directiva y aborta la compilación. Por eso
    los saltos de línea van siempre pegados al texto anterior. }
  if Pos('{#AppVersion}', Instalada) <> 1 then
    MsgBox('LA ACTUALIZACIÓN NO SE COMPLETÓ.' + #13#10 + #13#10 +
           '{#AppNombre} quedó en la versión ' + Instalada +
           ' y debía quedar en la {#AppVersion}.' + #13#10 + #13#10 +
           'Casi siempre es porque el programa estaba abierto y Windows no pudo ' +
           'reemplazar sus archivos.' + #13#10 + #13#10 +
           'Qué hacer: cierre {#AppNombre} por completo (revise que no haya quedado ' +
           'abierto en otra sesión de Windows) y vuelva a ejecutar esta ' +
           'actualización.' + #13#10 + #13#10 +
           'Si vuelve a pasar, llame al soporte: {#AppTelefono}.',
           mbCriticalError, MB_OK);
end;

function InitializeSetup(): Boolean;
begin
  Result := EstaInstalado();
  if not Result then
    MsgBox('En este equipo no hay ninguna instalación de {#AppNombre}.' + #13#10 + #13#10 +
           'Este archivo es solo para ACTUALIZAR una instalación que ya funciona: ' +
           'no trae MySQL, que es la base de datos que {#AppNombre} necesita.' + #13#10 + #13#10 +
           'Para instalar por primera vez use MediControl_Setup_{#AppVersion}.exe.',
           mbCriticalError, MB_OK);
end;
