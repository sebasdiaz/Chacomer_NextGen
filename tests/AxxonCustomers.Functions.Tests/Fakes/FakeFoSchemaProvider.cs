using AxxonCustomers.Functions.Mapping;

namespace AxxonCustomers.Functions.Tests.Fakes
{
    /// <summary>
    /// Sustituye el sondeo a F&amp;O. Se le carga el listado de propiedades que tiene el
    /// entity set y resuelve el casing igual que el provider real: match sin distinguir
    /// mayusculas, null si la propiedad no existe.
    /// </summary>
    public sealed class FakeFoSchemaProvider : IFoSchemaProvider
    {
        private readonly Dictionary<string, string> _properties;

        public FakeFoSchemaProvider(params string[] properties) =>
            _properties = properties.ToDictionary(p => p, p => p, StringComparer.OrdinalIgnoreCase);

        public Task<string?> ResolvePropertyAsync(
            string entitySet,
            string name,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_properties.TryGetValue(name, out var exact) ? exact : null);
    }
}
