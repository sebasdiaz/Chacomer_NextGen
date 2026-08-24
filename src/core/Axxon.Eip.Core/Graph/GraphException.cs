using System.Net;

namespace Axxon.Eip.Core.Graph
{
    /// <summary>
    /// Falla de una llamada a Microsoft Graph.
    ///
    /// Se lanza siempre que el status no sea de exito, en lugar de seguir de largo y
    /// explotar despues con un KeyNotFoundException al leer "id" de un body de error —
    /// que es como fallaba la implementacion original y enmascaraba el 403 real.
    /// </summary>
    public sealed class GraphException : Exception
    {
        public GraphException(string operation, HttpStatusCode statusCode, string? responseBody)
            : base($"[Graph:{operation}] respondio {(int)statusCode} ({statusCode}). " +
                   $"Detalle: {Truncate(responseBody)}")
        {
            Operation  = operation;
            StatusCode = statusCode;
        }

        public string Operation { get; }

        public HttpStatusCode StatusCode { get; }

        private static string Truncate(string? body) =>
            string.IsNullOrEmpty(body)
                ? "(sin body)"
                : body.Length > 600 ? body[..600] + "..." : body;
    }
}
