using AxxonLeads.Functions.Configuration;
using AxxonLeads.Functions.Models;
using Microsoft.Xrm.Sdk;

namespace AxxonLeads.Functions.Services
{
    /// <summary>
    /// Arma el <see cref="Entity"/> de <c>lead</c> a partir del payload.
    ///
    /// Es una clase aparte —y sin dependencias de Dataverse ni de Service Bus— para que el
    /// mapeo se pueda testear sin org: es la parte que mas va a cambiar cuando el negocio
    /// pida un campo mas, y la que peor se revisa leyendo un metodo que ademas hace I/O.
    ///
    /// Regla unica: un campo que no viene en el mensaje NO se escribe. Nada de mandar
    /// string vacio ni null explicito — en un Create dan lo mismo, pero el dia que esto
    /// tenga que soportar Update la diferencia entre "no vino" y "vino vacio" es la
    /// diferencia entre respetar y borrar un dato que ya estaba.
    /// </summary>
    public sealed class LeadEntityBuilder
    {
        public const string LeadEntity = "lead";

        private readonly LeadIntakeOptions _options;

        public LeadEntityBuilder(LeadIntakeOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public Entity Build(LeadIntakePayload payload)
        {
            ArgumentNullException.ThrowIfNull(payload);

            var lead = new Entity(LeadEntity);

            Set(lead, "subject",      payload.Subject);
            Set(lead, "firstname",    payload.FirstName);
            Set(lead, "lastname",     payload.LastName);
            Set(lead, "companyname",  payload.CompanyName);
            Set(lead, "jobtitle",     payload.JobTitle);
            Set(lead, "emailaddress1", payload.EmailAddress1);
            Set(lead, "mobilephone",  payload.MobilePhone);
            Set(lead, "telephone1",   payload.Telephone1);
            Set(lead, "description",  payload.Description);

            Set(lead, _options.IdentificationAttribute, payload.IdentificationNumber);

            if (_options.DeduplicationEnabled)
                Set(lead, _options.ExternalIdAttribute!, payload.ExternalId);

            if (payload.LeadSourceCode is { } sourceCode)
                lead["leadsourcecode"] = new OptionSetValue(sourceCode);

            AddAddress(lead, payload.Address);

            return lead;
        }

        /// <summary>
        /// Domicilio en los campos nativos <c>address1_*</c> del lead. Cuando el lead se
        /// califica, Dataverse arrastra estos campos al contact/account que crea — que es
        /// justamente por lo que conviene que el domicilio entre por aca y no por una nota
        /// o una tabla propia.
        /// </summary>
        private static void AddAddress(Entity lead, LeadAddress? address)
        {
            if (address is null) return;

            Set(lead, "address1_name",            address.Name);
            Set(lead, "address1_line1",           address.Line1);
            Set(lead, "address1_line2",           address.Line2);
            Set(lead, "address1_line3",           address.Line3);
            Set(lead, "address1_city",            address.City);
            Set(lead, "address1_stateorprovince", address.StateOrProvince);
            Set(lead, "address1_postalcode",      address.PostalCode);
            Set(lead, "address1_country",         address.Country);
            Set(lead, "address1_telephone1",      address.Telephone);
        }

        private static void Set(Entity entity, string attribute, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;

            entity[attribute] = value.Trim();
        }
    }
}
