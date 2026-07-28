namespace AxxonCustomers.Functions.Mapping
{
    /// <summary>
    /// Mapeo invalido. Se lanza al arranque y voltea el host a proposito: un mapeo mal
    /// escrito no falla, escribe mal en F&amp;O y nadie se entera por semanas.
    /// Trae todos los errores juntos, no el primero.
    /// </summary>
    public sealed class MappingCompilationException : Exception
    {
        public IReadOnlyList<string> Errors { get; }

        public MappingCompilationException(string mapName, IReadOnlyList<string> errors)
            : base($"El mapeo '{mapName}' tiene {errors.Count} error(es):{Environment.NewLine}" +
                   string.Join(Environment.NewLine, errors.Select(e => $"  - {e}")))
        {
            Errors = errors;
        }
    }
}
