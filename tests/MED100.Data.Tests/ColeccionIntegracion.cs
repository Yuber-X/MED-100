namespace MED100.Data.Tests;

/// <summary>
/// Los tests de integración comparten dos recursos globales: el MySQL local
/// y el estático SesionActual (rol + permisos del usuario). xUnit paraleliza
/// clases distintas, así que sin esta colección un test que inicia sesión como
/// Cajero le quita los permisos a otro que está vendiendo. Todos van en la
/// misma colección → se ejecutan en serie.
/// </summary>
[CollectionDefinition(Nombre, DisableParallelization = true)]
public class ColeccionIntegracion
{
    public const string Nombre = "Integración MySQL";
}
