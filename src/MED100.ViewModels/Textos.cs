namespace MED100.ViewModels;

/// <summary>Par valor+etiqueta para ComboBox (patrón PrestControl).</summary>
public record Opcion<T>(T Valor, string Etiqueta)
{
    public override string ToString() => Etiqueta;
}

/// <summary>Página que recarga sus datos al navegar hacia ella.</summary>
public interface IPaginaAsincrona
{
    Task RefrescarAsync();
}
