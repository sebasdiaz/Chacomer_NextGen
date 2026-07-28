using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace AxxonCustomers.Functions.Tests.Fakes
{
    /// <summary>
    /// IOrganizationService de mentira: solo resuelve Retrieve contra un diccionario
    /// precargado y registra los Update. Alcanza para el mapeo, que solo lee lookups.
    /// Preferido a una libreria de mocking para no sumar dependencias al repo.
    /// </summary>
    public sealed class FakeOrganizationService : IOrganizationService
    {
        private readonly Dictionary<(string LogicalName, Guid Id), Entity> _records = new();

        public List<Entity> Updates { get; } = new();

        /// <summary>Registra una entidad recuperable y devuelve la referencia para usarla como lookup.</summary>
        public EntityReference Add(string logicalName, Guid id, params (string Attribute, object? Value)[] attributes)
        {
            var entity = new Entity(logicalName, id);

            foreach (var (attribute, value) in attributes)
                if (value is not null)
                    entity[attribute] = value;

            _records[(logicalName, id)] = entity;
            return new EntityReference(logicalName, id);
        }

        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            if (!_records.TryGetValue((entityName, id), out var entity))
                throw new InvalidOperationException($"El fake no tiene ningun {entityName} con id {id}.");

            if (columnSet.AllColumns)
                return entity;

            // Se devuelven solo las columnas pedidas, como hace Dataverse.
            var projection = new Entity(entity.LogicalName, entity.Id);

            foreach (var column in columnSet.Columns)
                if (entity.Attributes.TryGetValue(column, out var value))
                    projection[column] = value;

            foreach (var (key, label) in entity.FormattedValues)
                if (columnSet.Columns.Contains(key))
                    projection.FormattedValues.Add(key, label);

            return projection;
        }

        public void Update(Entity entity) => Updates.Add(entity);

        public Guid Create(Entity entity) => throw new NotSupportedException();
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();
        public OrganizationResponse Execute(OrganizationRequest request) => throw new NotSupportedException();
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
            => throw new NotSupportedException();
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
            => throw new NotSupportedException();
        public EntityCollection RetrieveMultiple(QueryBase query) => throw new NotSupportedException();
    }
}
