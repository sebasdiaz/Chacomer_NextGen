using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace AxxonContacts.Functions.Tests.Fakes
{
    /// <summary>
    /// IOrganizationService de mentira que solo resuelve RetrieveMultiple, devolviendo los
    /// equipos que se le pasan y contando cuantas veces se consulto. Alcanza para fijar el
    /// comportamiento del resolver del owner team: que cachee, que lance si no encuentra
    /// nada y que no consulte cuando no hay equipo configurado.
    /// </summary>
    public sealed class FakeTeamOrganizationService : IOrganizationService
    {
        private readonly Entity[] _teams;

        public FakeTeamOrganizationService(params Entity[] teams) => _teams = teams;

        /// <summary>Cuantas veces se ejecuto la query de equipos.</summary>
        public int Consultas { get; private set; }

        /// <summary>La ultima query recibida, para afirmar el filtro.</summary>
        public QueryExpression? UltimaQuery { get; private set; }

        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            Consultas++;
            UltimaQuery = (QueryExpression)query;
            return new EntityCollection(_teams.ToList());
        }

        public Guid Create(Entity entity) => throw new NotSupportedException();
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new NotSupportedException();
        public void Update(Entity entity) => throw new NotSupportedException();
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();
        public OrganizationResponse Execute(OrganizationRequest request) => throw new NotSupportedException();
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
            => throw new NotSupportedException();
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
            => throw new NotSupportedException();
    }
}
