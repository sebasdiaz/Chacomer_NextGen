using AxxonFiscal.Functions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace AxxonFiscal.Functions.Services
{
    /// <summary>
    /// Busca contacts y accounts en Dataverse por RUC, contra
    /// msdyn_identificationnumber — el mismo campo sobre el que matchea masters
    /// AxxonContacts y el que valida la SET.
    ///
    /// Es de solo lectura: no crea, no actualiza y no dispara mensajeria.
    /// </summary>
    public class PartyLookupService
    {
        private const string ContactEntity        = "contact";
        private const string AccountEntity        = "account";
        private const string IdentificationNumber = "msdyn_identificationnumber";
        private const string IsMaster             = "axx_ismaster";

        /// <summary>
        /// Techo por tabla. Un RUC tiene un master y un raw por legal entity, asi que
        /// el orden esperado es de unidades; 50 es holgado y evita que un RUC sucio
        /// devuelva una pagina enorme.
        /// </summary>
        private const int MaxPorTabla = 50;

        private readonly IOrganizationService _service;
        private readonly ILogger<PartyLookupService> _logger;

        public PartyLookupService(IOrganizationService service, ILogger<PartyLookupService> logger)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _logger  = logger  ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Devuelve las partes cuyo msdyn_identificationnumber coincide con el RUC.
        /// Acepta el RUC con o sin digito verificador ("80054203-7" o "80054203").
        /// Accounts primero, y dentro de cada tabla el master antes que los raws.
        /// </summary>
        public async Task<PartyLookupResponse> FindByRucAsync(string ruc)
        {
            var normalizado = ruc.Trim();

            var accounts = await FindAsync(AccountEntity, "name", normalizado);
            var contacts = await FindAsync(ContactEntity, "fullname", normalizado);

            var resultados = accounts.Concat(contacts)
                .OrderByDescending(r => r.EsMaster)
                .ToList();

            _logger.LogInformation(
                "[PartyLookupService] ruc={Ruc} | accounts={Accounts} contacts={Contacts}",
                normalizado, accounts.Count, contacts.Count);

            return new PartyLookupResponse { Ruc = normalizado, Resultados = resultados };
        }

        // ────────────────────────────────────────────────────────────

        private async Task<List<PartyLookupResult>> FindAsync(
            string entityLogicalName, string nameAttribute, string ruc)
        {
            var query = new QueryExpression(entityLogicalName)
            {
                ColumnSet = new ColumnSet(nameAttribute, IdentificationNumber, IsMaster),
                Criteria  = BuildRucFilter(ruc),
                Orders    = { new OrderExpression(IsMaster, OrderType.Descending) },
                TopCount  = MaxPorTabla
            };

            var results = await Task.Run(() => _service.RetrieveMultiple(query));

            return results.Entities.Select(e => new PartyLookupResult
            {
                Id                   = e.Id,
                Entidad              = entityLogicalName,
                TipoPersona          = entityLogicalName == AccountEntity ? "Juridica" : "Fisica",
                Nombre               = e.GetAttributeValue<string>(nameAttribute),
                IdentificationNumber = e.GetAttributeValue<string>(IdentificationNumber),
                EsMaster             = e.GetAttributeValue<bool>(IsMaster)
            }).ToList();
        }

        /// <summary>
        /// El RUC se guarda como "80054203-7", pero el caller puede mandarlo sin el
        /// digito verificador. Se cubren las dos formas con un OR: igualdad exacta
        /// (sirve cuando mandan el DV, y tambien si algun registro quedo sin el) y
        /// prefijo "ruc-" (cuando mandan solo el RUC). El guion en el prefijo es lo que
        /// evita que "8005420" arrastre a "80054203-7".
        /// </summary>
        private static FilterExpression BuildRucFilter(string ruc) =>
            new(LogicalOperator.Or)
            {
                Conditions =
                {
                    new ConditionExpression(IdentificationNumber, ConditionOperator.Equal, ruc),
                    new ConditionExpression(IdentificationNumber, ConditionOperator.BeginsWith, ruc + "-")
                }
            };
    }
}
