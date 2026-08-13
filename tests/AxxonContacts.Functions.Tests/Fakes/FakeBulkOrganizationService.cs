using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace AxxonContacts.Functions.Tests.Fakes
{
    /// <summary>
    /// IOrganizationService de mentira que solo resuelve ExecuteMultiple y guarda los
    /// lotes que recibio, para poder afirmar que el reintento manda unicamente los
    /// updates que fallaron. El fallo se decide con un delegado (numero de intento +
    /// entidad), que es lo que permite simular el throttling de F&amp;O: falla una vez
    /// y despues anda.
    /// </summary>
    public sealed class FakeBulkOrganizationService : IOrganizationService
    {
        private readonly Func<int, Entity, bool> _fails;

        public FakeBulkOrganizationService(Func<int, Entity, bool> fails) => _fails = fails;

        /// <summary>Un item por llamada a Execute, con los targets de ese lote.</summary>
        public List<List<Entity>> Batches { get; } = new();

        public OrganizationResponse Execute(OrganizationRequest request)
        {
            var targets = ((ExecuteMultipleRequest)request).Requests
                .Cast<UpdateRequest>()
                .Select(r => r.Target)
                .ToList();

            Batches.Add(targets);
            var attempt = Batches.Count;

            var items = new ExecuteMultipleResponseItemCollection();

            for (var index = 0; index < targets.Count; index++)
                items.Add(new ExecuteMultipleResponseItem
                {
                    RequestIndex = index,
                    Fault = _fails(attempt, targets[index])
                        ? new OrganizationServiceFault { Message = "Dual Write core application error - throttling" }
                        : null
                });

            var response = new ExecuteMultipleResponse();
            response.Results["Responses"] = items;
            return response;
        }

        public Guid Create(Entity entity) => throw new NotSupportedException();
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new NotSupportedException();
        public void Update(Entity entity) => throw new NotSupportedException();
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();
        public EntityCollection RetrieveMultiple(QueryBase query) => throw new NotSupportedException();
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
            => throw new NotSupportedException();
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
            => throw new NotSupportedException();
    }
}
