using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace AxxonContacts.Functions.Tests.Fakes
{
    /// <summary>
    /// IOrganizationService de mentira que solo resuelve Update, guardando la entidad tal
    /// como se la mandaron. Alcanza para afirmar el <b>tipo</b> de cada atributo que se
    /// escribe — que es donde estan las fallas mudas: Dataverse rechaza el tipo equivocado
    /// en runtime y el llamador lo degrada a warning, asi que no hay forma de verlo sin
    /// mirar la entidad que salio.
    /// </summary>
    public sealed class FakeUpdateOrganizationService : IOrganizationService
    {
        /// <summary>Las entidades recibidas por Update, en orden.</summary>
        public List<Entity> Updates { get; } = [];

        public void Update(Entity entity) => Updates.Add(entity);

        public Guid Create(Entity entity) => throw new NotSupportedException();
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new NotSupportedException();
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();
        public EntityCollection RetrieveMultiple(QueryBase query) => throw new NotSupportedException();
        public OrganizationResponse Execute(OrganizationRequest request) => throw new NotSupportedException();
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
            => throw new NotSupportedException();
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
            => throw new NotSupportedException();
    }
}
