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

        /// <summary>Cada Retrieve resuelto, para verificar caches.</summary>
        public List<(string LogicalName, Guid Id)> Retrieves { get; } = new();

        /// <summary>Cada RetrieveMultiple resuelto, para verificar caches.</summary>
        public List<string> RetrieveMultiples { get; } = new();

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
            Retrieves.Add((entityName, id));

            if (!_records.TryGetValue((entityName, id), out var entity))
                throw new InvalidOperationException($"El fake no tiene ningun {entityName} con id {id}.");

            return Project(entity, columnSet);
        }

        /// <summary>Se devuelven solo las columnas pedidas, como hace Dataverse.</summary>
        private static Entity Project(Entity entity, ColumnSet columnSet)
        {
            if (columnSet.AllColumns)
                return entity;

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

        /// <summary>
        /// Resuelve QueryExpression sobre el diccionario precargado: Equal, Null y NotNull,
        /// con filtros anidados (AND/OR). Alcanza para lo que consultan los mapeos —
        /// direccion primaria, catalogos de localizacion— y para la query del backfill.
        ///
        /// El paginado se ignora: se devuelve todo en una pagina con MoreRecords en false.
        /// </summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            if (query is not QueryExpression expression)
                throw new NotSupportedException("El fake solo resuelve QueryExpression.");

            RetrieveMultiples.Add(expression.EntityName);

            var matches = _records.Values
                .Where(e => string.Equals(e.LogicalName, expression.EntityName, StringComparison.OrdinalIgnoreCase))
                .Where(e => Matches(e, expression.Criteria))
                .ToList();

            if (expression.TopCount is > 0)
                matches = matches.Take(expression.TopCount.Value).ToList();

            var collection = new EntityCollection();

            foreach (var match in matches)
                collection.Entities.Add(Project(match, expression.ColumnSet));

            return collection;
        }

        private static bool Matches(Entity entity, FilterExpression filter)
        {
            var results = filter.Conditions.Select(c => Matches(entity, c))
                .Concat(filter.Filters.Select(f => Matches(entity, f)))
                .ToList();

            if (results.Count == 0)
                return true;

            return filter.FilterOperator == LogicalOperator.Or
                ? results.Any(r => r)
                : results.All(r => r);
        }

        private static bool Matches(Entity entity, ConditionExpression condition)
        {
            entity.Attributes.TryGetValue(condition.AttributeName, out var raw);

            var actual = raw switch
            {
                EntityReference reference => reference.Id,
                OptionSetValue option     => option.Value,
                _                         => raw
            };

            // Dataverse no distingue "atributo ausente" de "atributo en null": las dos cosas
            // son null para el filtro.
            switch (condition.Operator)
            {
                case ConditionOperator.Null:
                    return actual is null;

                case ConditionOperator.NotNull:
                    return actual is not null;

                case ConditionOperator.Equal:
                    break;

                default:
                    throw new NotSupportedException(
                        $"El fake soporta Equal, Null y NotNull, no {condition.Operator}.");
            }

            var expected = condition.Values.Count > 0 ? condition.Values[0] : null;

            if (actual is null || expected is null)
                return actual is null && expected is null;

            return actual is string actualText && expected is string expectedText
                ? string.Equals(actualText, expectedText, StringComparison.OrdinalIgnoreCase)
                : actual.Equals(expected);
        }
    }
}
