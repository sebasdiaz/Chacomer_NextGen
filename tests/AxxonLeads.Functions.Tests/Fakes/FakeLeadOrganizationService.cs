using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace AxxonLeads.Functions.Tests.Fakes
{
    /// <summary>
    /// IOrganizationService de mentira: registra los Create y resuelve RetrieveMultiple
    /// contra una lista precargada, filtrando por las condiciones de igualdad de la query.
    /// Alcanza para la deduplicacion, que es lo unico que lee esta integracion.
    /// Preferido a una libreria de mocking para no sumar dependencias al repo.
    /// </summary>
    public sealed class FakeLeadOrganizationService : IOrganizationService
    {
        private readonly List<Entity> _existing = new();

        /// <summary>Cada Create, en orden. Vacio = no se escribio nada.</summary>
        public List<Entity> Creates { get; } = new();

        /// <summary>Cada RetrieveMultiple resuelto, para verificar que la dedup consulto.</summary>
        public List<QueryExpression> Queries { get; } = new();

        /// <summary>Precarga un registro que la query puede encontrar.</summary>
        public Guid Add(string logicalName, params (string Attribute, object Value)[] attributes)
        {
            var entity = new Entity(logicalName, Guid.NewGuid());

            foreach (var (attribute, value) in attributes)
                entity[attribute] = value;

            _existing.Add(entity);
            return entity.Id;
        }

        public Guid Create(Entity entity)
        {
            var created = entity.Id == Guid.Empty ? Guid.NewGuid() : entity.Id;

            Creates.Add(entity);
            return created;
        }

        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            var expression = (QueryExpression)query;
            Queries.Add(expression);

            var matches = _existing
                .Where(e => e.LogicalName == expression.EntityName)
                .Where(e => expression.Criteria.Conditions.All(c => Matches(e, c)))
                .Take(expression.TopCount ?? int.MaxValue)
                .ToList();

            return new EntityCollection(matches);
        }

        private static bool Matches(Entity entity, ConditionExpression condition)
        {
            if (condition.Operator != ConditionOperator.Equal)
                throw new NotSupportedException(
                    $"El fake solo resuelve igualdad, y la query pidio {condition.Operator}.");

            return entity.Attributes.TryGetValue(condition.AttributeName, out var value) &&
                   Equals(value, condition.Values.FirstOrDefault());
        }

        // El resto de la interfaz no lo usa esta integracion.
        public void Update(Entity entity) => throw new NotSupportedException();
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new NotSupportedException();
        public OrganizationResponse Execute(OrganizationRequest request) => throw new NotSupportedException();
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    }
}
