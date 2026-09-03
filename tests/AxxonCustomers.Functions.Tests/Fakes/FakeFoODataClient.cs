using System.Runtime.CompilerServices;
using Axxon.Eip.Core.FinOps;
using AxxonCustomers.Functions.Mapping;
using AxxonCustomers.Functions.Models;

namespace AxxonCustomers.Functions.Tests.Fakes
{
    /// <summary>
    /// IFoODataClient de mentira: resuelve el unico entity set que el mapeo de localizacion
    /// lee del ERP, el catalogo de estados. La escritura va por <c>ILtmCustService</c>, que
    /// se fakea aparte.
    /// </summary>
    public sealed class FakeFoODataClient : IFoODataClient
    {
        private readonly string[] _states;

        /// <param name="states">Codigos que devuelve <c>AddressStates</c> para el pais pedido.</param>
        public FakeFoODataClient(params string[] states) => _states = states;

        /// <summary>Cada entity set consultado, para verificar caches.</summary>
        public List<string> Queries { get; } = new();

        public async IAsyncEnumerable<T> QueryAsync<T>(
            FoODataQuery query,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Queries.Add(query.EntitySet);

            if (query.EntitySet != LtmCustMapping.FoStateEntitySet)
                throw new NotSupportedException($"El fake no conoce el entity set {query.EntitySet}.");

            await Task.CompletedTask;

            foreach (var state in _states)
                yield return (T)(object)new FoAddressState { State = state };
        }

        public Task<T?> FindFirstAsync<T>(FoODataQuery query, CancellationToken cancellationToken = default)
            where T : class => throw new NotSupportedException();

        public Task<TResponse> CreateAsync<TEntity, TResponse>(
            string entitySet, TEntity entity, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task UpdateAsync<TEntity>(
            string entitySet, string entityKey, TEntity entity, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
