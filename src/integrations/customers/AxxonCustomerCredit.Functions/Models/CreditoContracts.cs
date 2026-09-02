using System.Text.Json.Serialization;

namespace AxxonCustomerCredit.Functions.Models
{
    /// <summary>
    /// Filtros que acepta una consulta. No todos aplican a todas las entidades: cada
    /// endpoint declara los suyos y rechaza el resto con un 400, en vez de ignorarlos.
    /// Un filtro ignorado es peor que un error: el consumidor cree que filtro y se lleva
    /// la tabla entera.
    ///
    /// Los valores se escapan con <c>FoOData.EscapeLiteral</c> antes de entrar al
    /// <c>$filter</c> — son texto que llega de afuera.
    /// </summary>
    public sealed class CreditoConsulta
    {
        /// <summary>Legal entity de F&amp;O. Vacio ⇒ todas (la lectura es cross-company).</summary>
        public string? DataAreaId { get; init; }

        /// <summary>
        /// <c>CustomerAccount</c>. No aplica a resoluciones, que no lo tienen.
        /// </summary>
        public string? Cuenta { get; init; }

        /// <summary><c>CreditId</c> del plan otorgado. Aplica a planes y cuotas.</summary>
        public string? CreditId { get; init; }

        /// <summary><c>RequestId</c> de la solicitud. Aplica a planes.</summary>
        public string? RequestId { get; init; }

        /// <summary><c>SolicitudId</c>. Aplica a resoluciones.</summary>
        public string? SolicitudId { get; init; }

        /// <summary>Tope de filas a devolver. Ya validado por el endpoint.</summary>
        public int Top { get; init; } = CreditoLimites.TopDefault;
    }

    /// <summary>Topes de la API, en un solo lugar porque los usan el endpoint y la doc.</summary>
    public static class CreditoLimites
    {
        public const int TopDefault = 100;
        public const int TopMaximo  = 1000;
    }

    /// <summary>
    /// Lo que devuelve el servicio: las filas y si quedaron mas afuera del tope.
    /// </summary>
    public sealed record CreditoResultado<T>(IReadOnlyList<T> Items, bool Truncado);

    /// <summary>
    /// Respuesta de los cuatro endpoints. La coleccion se llama siempre <c>datos</c> para
    /// que el consumidor pueda parsear las cuatro con el mismo codigo.
    /// </summary>
    public sealed class CreditoResponse<T>
    {
        [JsonPropertyName("cantidad")]
        public int Cantidad => Datos.Count;

        /// <summary>
        /// <c>true</c> si F&amp;O tenia mas filas que el tope pedido. Es explicito a
        /// proposito: cortar en silencio hace que el consumidor no se entere de que le
        /// falta la mitad de las cuotas.
        /// </summary>
        [JsonPropertyName("truncado")]
        public bool Truncado { get; init; }

        [JsonPropertyName("datos")]
        public IReadOnlyList<T> Datos { get; init; } = [];
    }
}
