using AxxonCustomerData.Functions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace AxxonCustomerData.Functions.Services
{
    /// <summary>
    /// Lee clientes de Dataverse por RUC, contra <c>msdyn_identificationnumber</c> — el
    /// mismo campo sobre el que AxxonContacts matchea masters y el que valida la SET.
    ///
    /// Es de solo lectura: no crea, no actualiza y no dispara mensajeria. Todo lo que
    /// devuelve sale de una consulta por tabla; no hay una segunda vuelta por registro.
    /// Ese techo es deliberado — un endpoint que dispara N consultas por respuesta se
    /// vuelve el cuello de botella de Dataverse el dia que un satelite lo llame en un loop.
    /// </summary>
    public class ClienteLookupService
    {
        /// <summary>
        /// Techo por tabla. Un RUC tiene un master y un raw por legal entity, asi que el
        /// orden esperado es de unidades; 50 es holgado y evita que un RUC sucio devuelva
        /// una pagina enorme. Mismo criterio que el endpoint de fiscal.
        /// </summary>
        private const int MaxPorTabla = 50;

        private readonly IOrganizationService _service;
        private readonly ILogger<ClienteLookupService> _logger;

        public ClienteLookupService(IOrganizationService service, ILogger<ClienteLookupService> logger)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _logger  = logger  ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Devuelve los clientes cuyo <c>msdyn_identificationnumber</c> coincide con el RUC.
        /// Acepta el RUC con o sin digito verificador ("80054203-7" o "80054203").
        /// Accounts primero y, dentro de cada tabla, el master antes que los raws.
        /// </summary>
        public async Task<ClienteLookupResponse> FindByRucAsync(
            string ruc, CancellationToken cancellationToken = default)
        {
            var normalizado = ruc.Trim();

            var accounts = await FindAsync(ClienteSource.Account, normalizado, cancellationToken);
            var contacts = await FindAsync(ClienteSource.Contact, normalizado, cancellationToken);

            var clientes = accounts.Concat(contacts).ToList();

            _logger.LogInformation(
                "[ClienteLookupService] ruc={Ruc} | accounts={Accounts} contacts={Contacts}",
                normalizado, accounts.Count, contacts.Count);

            return new ClienteLookupResponse { Ruc = normalizado, Clientes = clientes };
        }

        // ────────────────────────────────────────────────────────────

        private async Task<List<ClienteLookupResult>> FindAsync(
            ClienteSource source, string ruc, CancellationToken cancellationToken)
        {
            var query = new QueryExpression(source.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(source.Columns),
                Criteria  = BuildRucFilter(ruc),
                Orders    = { new OrderExpression(ClienteAttributes.IsMaster, OrderType.Descending) },
                TopCount  = MaxPorTabla
            };

            // El codigo de la legal entity (cdm_companycode) es el unico dato del cliente
            // que no vive en su propia fila, y es justo el que sirve de clave contra F&O.
            // Va por link y no por una segunda consulta: la EntityReference de
            // msdyn_company trae el nombre, nunca el codigo.
            //
            // LeftOuter porque los masters NO tienen compania: con un inner join la
            // consulta devolveria solo los raws, que es exactamente lo contrario de lo que
            // pide un satelite que busca la vista unificada del RUC.
            query.LinkEntities.Add(new LinkEntity(
                source.EntityLogicalName,
                ClienteAttributes.CompanyEntity,
                ClienteAttributes.Company,
                ClienteAttributes.CompanyIdKey,
                JoinOperator.LeftOuter)
            {
                Columns     = new ColumnSet(ClienteAttributes.CompanyCode),
                EntityAlias = ClienteAttributes.CompanyAlias
            });

            var results = await Task.Run(
                () => _service.RetrieveMultiple(query), cancellationToken);

            return results.Entities.Select(e => Map(e, source)).ToList();
        }

        private static ClienteLookupResult Map(Entity e, ClienteSource source) => new()
        {
            Id                     = e.Id,
            Entidad                = source.EntityLogicalName,
            TipoPersona            = source.TipoPersona,
            Nombre                 = e.GetAttributeValue<string>(source.NameAttribute),
            IdentificationNumber   = e.GetAttributeValue<string>(ClienteAttributes.IdentificationNumber),
            EsMaster               = e.GetAttributeValue<bool>(ClienteAttributes.IsMaster),
            MasterId               = e.GetAttributeValue<EntityReference>(source.MasterAttribute)?.Id,
            CustomerAccount        = e.GetAttributeValue<string>(source.CustomerAccountAttribute),
            LegalEntity            = MapLegalEntity(e),
            TipoPersoneriaJuridica = Formatted(e, ClienteAttributes.TipoPersoneria),
            // Es un OptionSet, no un lookup: se lee por etiqueta. Y el atributo se llama
            // distinto en cada tabla, por eso sale de la source y no de las constantes.
            TipoDocumento          = Formatted(e, source.TipoDocumentoAttribute),
            Email                  = e.GetAttributeValue<string>(ClienteAttributes.Email),
            Telefono               = e.GetAttributeValue<string>(ClienteAttributes.Telefono),
            // statecode 0 = Active en las dos tablas. Si el atributo no vuelve (permisos a
            // nivel campo), se informa inactivo antes que inventar un activo.
            Activo                 = e.GetAttributeValue<OptionSetValue>(ClienteAttributes.StateCode)?.Value == 0
        };

        private static LegalEntityInfo? MapLegalEntity(Entity e)
        {
            var company = e.GetAttributeValue<EntityReference>(ClienteAttributes.Company);
            if (company is null)
                return null;

            var aliased = e.GetAttributeValue<AliasedValue>(
                ClienteAttributes.CompanyAlias + "." + ClienteAttributes.CompanyCode);

            return new LegalEntityInfo
            {
                Id     = company.Id,
                Codigo = aliased?.Value as string,
                Nombre = company.Name
            };
        }

        /// <summary>
        /// Etiqueta de un OptionSet. Se manda el texto y no el numero porque el consumidor
        /// es un satelite externo: el valor numerico solo tiene sentido con la metadata de
        /// Dataverse al lado.
        /// </summary>
        private static string? Formatted(Entity e, string attribute) =>
            e.FormattedValues.Contains(attribute) ? e.FormattedValues[attribute] : null;

        /// <summary>
        /// El RUC se guarda como "80054203-7", pero el caller puede mandarlo sin el digito
        /// verificador. Se cubren las dos formas con un OR: igualdad exacta (sirve cuando
        /// mandan el DV, y tambien si algun registro quedo sin el) y prefijo "ruc-" (cuando
        /// mandan solo el RUC). El guion en el prefijo es lo que evita que "8005420"
        /// arrastre a "80054203-7".
        ///
        /// Es el mismo filtro que el endpoint de fiscal, a proposito: los dos buscan sobre
        /// el mismo campo y tienen que devolver el mismo conjunto de registros.
        /// </summary>
        private static FilterExpression BuildRucFilter(string ruc) =>
            new(LogicalOperator.Or)
            {
                Conditions =
                {
                    new ConditionExpression(
                        ClienteAttributes.IdentificationNumber, ConditionOperator.Equal, ruc),
                    new ConditionExpression(
                        ClienteAttributes.IdentificationNumber, ConditionOperator.BeginsWith, ruc + "-")
                }
            };
    }
}
