using AxxonContacts.Functions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System.ServiceModel;

namespace AxxonContacts.Functions.Services
{
    public class AccountMasterMatchingService
    {
        private const string EntityLogicalName    = "account";
        private const string IsMaster             = "axx_ismaster";
        private const string MasterAccountId      = "axx_masteraccountid";
        private const string IdentificationNumber = "msdyn_identificationnumber";
        private const string LugarComercial       = "axx_lugarcomercial";
        private const string TipoPersoneria       = "axx_tipopersoneriajuridica";
        private const int    BulkBatchSize        = 1000;

        private const string CustomerAddressEntity = "customeraddress";

        /// <summary>
        /// Campos del customeraddress 1 que se copian del raw al master.
        /// msdyn_streetnumber, axx_numero y msdyn_district viven SOLO en customeraddress
        /// (no tienen proyeccion address1_* en el account), asi que el bloque que copia
        /// BuildMasterEntity no puede arrastrarlos. Los demas se copian tambien para que
        /// el domicilio 1 del master quede completo a nivel customeraddress.
        /// </summary>
        private static readonly string[] CustomerAddressColumns =
        [
            "msdyn_streetnumber", "axx_numero", "line3",
            "msdyn_district", "latitude", "longitude", "postalcode"
        ];

        /// <summary>
        /// Bloque de domicilio que se copia del raw al master. address1_stateorprovince es
        /// el logical name real del departamento/estado (no existe address1_stateprovince).
        /// </summary>
        private static readonly string[] AddressColumns =
        [
            "address1_line1", "address1_line2", "address1_line3",
            "address1_city", "address1_county", "address1_stateorprovince",
            "address1_postalcode", "address1_country",
            "address1_latitude", "address1_longitude"
        ];

        /// <summary>
        /// Campos que se copian del raw al master pero no participan del matching, asi que
        /// el PreImage del Step no tiene por que traerlos. Se releen juntos, en un solo
        /// Retrieve, cuando el evento no los trajo.
        /// </summary>
        private static readonly string[] SecondaryColumns =
        [
            "emailaddress1", LugarComercial, TipoPersoneria
        ];

        private readonly IOrganizationService    _service;
        private readonly MasterOwnerTeamResolver _ownerTeamResolver;
        private readonly ILogger                 _logger;

        public AccountMasterMatchingService(
            IOrganizationService service,
            MasterOwnerTeamResolver ownerTeamResolver,
            ILogger logger)
        {
            _service           = service           ?? throw new ArgumentNullException(nameof(service));
            _ownerTeamResolver = ownerTeamResolver ?? throw new ArgumentNullException(nameof(ownerTeamResolver));
            _logger            = logger            ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Solo procesa eventos Create de accounts raw (no master).
        /// Si ya existe un master para el msdyn_identificationnumber → linkea los raws pendientes y retorna su referencia.
        /// Si no existe → crea el master, linkea todos los raws y retorna la referencia del nuevo master.
        /// Retorna null si el evento se ignora (trigger distinto a Create, es master, o sin identification).
        /// </summary>
        public async Task<EntityReference?> ProcessAsync(AccountEventMessage message)
        {
            ArgumentNullException.ThrowIfNull(message);

            _logger.LogInformation(
                "[AccountMasterMatchingService] Procesando Account {AccountId} | Identification={Identification} | Trigger={Trigger}",
                message.AccountId, message.MsdynIdentificationNumber, message.TriggerMessage);

            bool isCreate = string.Equals(message.TriggerMessage, "Create", StringComparison.OrdinalIgnoreCase);
            bool isUpdate = string.Equals(message.TriggerMessage, "Update", StringComparison.OrdinalIgnoreCase);

            if (!isCreate && !isUpdate)
            {
                _logger.LogInformation(
                    "[AccountMasterMatchingService] Evento '{Trigger}' ignorado.",
                    message.TriggerMessage);
                return null;
            }

            if (message.IsMaster)
            {
                _logger.LogInformation("[AccountMasterMatchingService] Account es Master. Skip.");
                return null;
            }

            // Si identification o name no llegaron en el payload (Step sin PreImage completo),
            // se buscan directamente en Dataverse.
            if (string.IsNullOrWhiteSpace(message.MsdynIdentificationNumber) || string.IsNullOrWhiteSpace(message.Name))
            {
                _logger.LogInformation(
                    "[AccountMasterMatchingService] Campos faltantes en payload. Recuperando Account {AccountId} de Dataverse.",
                    message.AccountId);
                message = await EnrichFromDataverseAsync(message);
            }

            if (string.IsNullOrWhiteSpace(message.MsdynIdentificationNumber))
            {
                _logger.LogWarning("[AccountMasterMatchingService] IdentificationNumber vacio tras enrich. Skip.");
                return null;
            }

            var existingMaster = await FindMasterByIdentificationAsync(message.MsdynIdentificationNumber);
            if (existingMaster != null)
            {
                _logger.LogInformation(
                    "[AccountMasterMatchingService] Master {MasterId} ya existe para '{Identification}'. Linkeando raws pendientes.",
                    existingMaster.Id, message.MsdynIdentificationNumber);
                await BulkAssociateRawsToMasterAsync(message.MsdynIdentificationNumber, existingMaster.ToEntityReference());
                return existingMaster.ToEntityReference();
            }

            _logger.LogInformation(
                "[AccountMasterMatchingService] Sin Master para '{Identification}'. Creando.",
                message.MsdynIdentificationNumber);

            var newMasterRef = await CreateMasterAsync(message);
            await BulkAssociateRawsToMasterAsync(message.MsdynIdentificationNumber, newMasterRef);

            _logger.LogInformation(
                "[AccountMasterMatchingService] Completado. Account={AccountId} | Master={MasterId}",
                message.AccountId, newMasterRef.Id);

            return newMasterRef;
        }

        // ────────────────────────────────────────────────────────────
        // EnrichFromDataverse
        // ────────────────────────────────────────────────────────────

        private async Task<AccountEventMessage> EnrichFromDataverseAsync(AccountEventMessage message)
        {
            try
            {
                var record = await Task.Run(() =>
                    _service.Retrieve(EntityLogicalName, message.AccountId,
                        new ColumnSet("name", IdentificationNumber, IsMaster, MasterAccountId, "msdyn_company")));

                if (string.IsNullOrWhiteSpace(message.Name))
                    message.Name = record.GetAttributeValue<string>("name");
                if (string.IsNullOrWhiteSpace(message.MsdynIdentificationNumber))
                    message.MsdynIdentificationNumber = record.GetAttributeValue<string>(IdentificationNumber);
                message.IsMaster        = record.GetAttributeValue<bool>(IsMaster);
                message.MasterAccountId = record.GetAttributeValue<EntityReference>(MasterAccountId)?.Id ?? message.MasterAccountId;
                message.MsdynCompany    ??= record.GetAttributeValue<EntityReference>("msdyn_company")?.Id;

                return message;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[AccountMasterMatchingService] No se pudo recuperar Account {AccountId} de Dataverse. Usando payload original.",
                    message.AccountId);
                return message;
            }
        }

        // ────────────────────────────────────────────────────────────
        // EnrichAddressFromDataverse
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// Completa el domicilio leyendolo del raw en Dataverse cuando no vino en el evento.
        ///
        /// Hace falta porque los campos address1_* solo llegan en el mensaje si el Step del
        /// Service Endpoint los incluye en su PreImage. Si el PreImage esta acotado a los
        /// campos de matching, el domicilio no viaja y el master se crearia vacio sin que
        /// falle nada. Se resuelve con un Retrieve extra, que solo se paga al CREAR el
        /// master (una vez por identificacion), no en cada mensaje.
        /// </summary>
        private async Task EnrichAddressFromDataverseAsync(AccountEventMessage message)
        {
            if (message.HasAddress) return;

            try
            {
                var record = await Task.Run(() =>
                    _service.Retrieve(EntityLogicalName, message.AccountId, new ColumnSet(AddressColumns)));

                message.Address1Line1           = record.GetAttributeValue<string>("address1_line1");
                message.Address1Line2           = record.GetAttributeValue<string>("address1_line2");
                message.Address1Line3           = record.GetAttributeValue<string>("address1_line3");
                message.Address1City            = record.GetAttributeValue<string>("address1_city");
                message.Address1County          = record.GetAttributeValue<string>("address1_county");
                message.Address1StateOrProvince = record.GetAttributeValue<string>("address1_stateorprovince");
                message.Address1PostalCode      = record.GetAttributeValue<string>("address1_postalcode");
                message.Address1Country         = record.GetAttributeValue<string>("address1_country");
                message.Address1Latitude        = record.GetAttributeValue<double?>("address1_latitude");
                message.Address1Longitude       = record.GetAttributeValue<double?>("address1_longitude");

                if (message.HasAddress)
                    _logger.LogInformation(
                        "[AccountMasterMatchingService] Domicilio recuperado de Dataverse para Account {AccountId}.",
                        message.AccountId);
            }
            catch (Exception ex)
            {
                // El domicilio es un dato secundario: si no se puede leer, el master igual
                // se crea. Perderlo no justifica reintentar el mensaje.
                _logger.LogWarning(ex,
                    "[AccountMasterMatchingService] No se pudo recuperar el domicilio de Account {AccountId}. " +
                    "El master se crea sin domicilio.",
                    message.AccountId);
            }
        }

        // ────────────────────────────────────────────────────────────
        // EnrichSecondaryFieldsFromDataverse
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// Completa emailaddress1, axx_lugarcomercial y axx_tipopersoneriajuridica leyendolos
        /// del raw en Dataverse cuando no vinieron en el evento.
        ///
        /// Mismo motivo que el domicilio: el PreImage del Step esta acotado a los campos de
        /// matching, asi que en un evento Update estos solo viajan si cambiaron en esa misma
        /// operacion. Sin esto el master se crearia sin ellos y sin que falle nada. Van en un
        /// unico Retrieve, que ademas se paga solo al CREAR el master, no en cada mensaje.
        /// </summary>
        private async Task EnrichSecondaryFieldsFromDataverseAsync(AccountEventMessage message)
        {
            if (!string.IsNullOrWhiteSpace(message.EmailAddress1)
                && message.AxxLugarComercial.HasValue
                && message.AxxTipoPersoneriaJuridica.HasValue)
                return;

            try
            {
                var record = await Task.Run(() =>
                    _service.Retrieve(EntityLogicalName, message.AccountId, new ColumnSet(SecondaryColumns)));

                if (string.IsNullOrWhiteSpace(message.EmailAddress1))
                    message.EmailAddress1 = record.GetAttributeValue<string>("emailaddress1");
                if (!message.AxxLugarComercial.HasValue)
                    message.AxxLugarComercial = record.GetAttributeValue<EntityReference>(LugarComercial)?.Id;
                if (!message.AxxTipoPersoneriaJuridica.HasValue)
                    message.AxxTipoPersoneriaJuridica = record.GetAttributeValue<EntityReference>(TipoPersoneria)?.Id;
            }
            catch (Exception ex)
            {
                // Datos secundarios, mismo criterio que el domicilio: si no se pueden leer,
                // el master igual se crea y no justifica reintentar el mensaje.
                _logger.LogWarning(ex,
                    "[AccountMasterMatchingService] No se pudieron recuperar los campos secundarios del raw {RawId}. " +
                    "El master se crea sin ellos.",
                    message.AccountId);
            }
        }

        // ────────────────────────────────────────────────────────────
        // FindMasterByIdentification
        // ────────────────────────────────────────────────────────────

        private async Task<Entity?> FindMasterByIdentificationAsync(string identificationNumber)
        {
            var query = new QueryExpression(EntityLogicalName)
            {
                ColumnSet = new ColumnSet(IsMaster, IdentificationNumber),
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
                    {
                        new ConditionExpression(IdentificationNumber, ConditionOperator.Equal, identificationNumber),
                        new ConditionExpression(IsMaster, ConditionOperator.Equal, true)
                    }
                },
                TopCount = 2
            };

            var results = await Task.Run(() => _service.RetrieveMultiple(query));

            if (results.Entities.Count > 1)
                _logger.LogWarning(
                    "[AccountMasterMatchingService] {Count} Masters para '{Identification}'. Usando el primero ({Id}).",
                    results.Entities.Count, identificationNumber, results.Entities[0].Id);

            return results.Entities.Count > 0 ? results.Entities[0] : null;
        }

        // ────────────────────────────────────────────────────────────
        // CreateMaster
        // ────────────────────────────────────────────────────────────

        private async Task<EntityReference> CreateMasterAsync(AccountEventMessage message)
        {
            await EnrichAddressFromDataverseAsync(message);
            await EnrichSecondaryFieldsFromDataverseAsync(message);

            var ownerTeam = await _ownerTeamResolver.ResolveAsync();
            var master    = BuildMasterEntity(message, ownerTeam);

            try
            {
                var masterId = await Task.Run(() => _service.Create(master));
                _logger.LogInformation("[AccountMasterMatchingService] Master creado. Id={Id}", masterId);
                await CopyCustomerAddressFieldsToMasterAsync(message.AccountId, masterId);
                return new EntityReference(EntityLogicalName, masterId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "[AccountMasterMatchingService] Create fallo: {Error}. Re-buscando (posible race condition).",
                    ex.Message);

                var existing = await FindMasterByIdentificationAsync(message.MsdynIdentificationNumber!);
                if (existing != null)
                {
                    _logger.LogInformation("[AccountMasterMatchingService] Race condition resuelta. Master={Id}", existing.Id);
                    return existing.ToEntityReference();
                }
                throw;
            }
        }

        // ────────────────────────────────────────────────────────────
        // CopyCustomerAddressFieldsToMaster
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// Copia los CustomerAddressColumns del customeraddress 1 del raw al del master.
        /// Dataverse crea el customeraddress 1 del master junto con el Create del
        /// registro, asi que aca solo se updatea. Se copian unicamente los campos que
        /// vinieron con dato: el Retrieve no devuelve atributos en null.
        /// </summary>
        private async Task CopyCustomerAddressFieldsToMasterAsync(Guid rawId, Guid masterId)
        {
            try
            {
                var rawAddress = await FindAddress1Async(rawId, new ColumnSet(CustomerAddressColumns));
                if (rawAddress == null) return;

                var toCopy = CustomerAddressColumns.Where(rawAddress.Contains).ToArray();
                if (toCopy.Length == 0) return;

                var masterAddress = await FindAddress1Async(masterId, new ColumnSet(false));
                if (masterAddress == null)
                {
                    _logger.LogWarning(
                        "[AccountMasterMatchingService] Master {MasterId} sin customeraddress 1. Domicilio no copiado.",
                        masterId);
                    return;
                }

                var upd = new Entity(CustomerAddressEntity, masterAddress.Id);
                foreach (var column in toCopy)
                    upd[column] = rawAddress[column];

                await Task.Run(() => _service.Update(upd));

                _logger.LogInformation(
                    "[AccountMasterMatchingService] Customeraddress del raw copiado al master {MasterId}: {Columns}.",
                    masterId, string.Join(", ", toCopy));
            }
            catch (Exception ex)
            {
                // Mismo criterio que el domicilio: dato secundario, si no se puede copiar
                // el master igual queda creado y no justifica reintentar el mensaje.
                _logger.LogWarning(ex,
                    "[AccountMasterMatchingService] No se pudo copiar el customeraddress del raw {RawId} al master {MasterId}.",
                    rawId, masterId);
            }
        }

        private async Task<Entity?> FindAddress1Async(Guid parentId, ColumnSet columns)
        {
            var query = new QueryExpression(CustomerAddressEntity)
            {
                ColumnSet = columns,
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
                    {
                        new ConditionExpression("parentid", ConditionOperator.Equal, parentId),
                        new ConditionExpression("addressnumber", ConditionOperator.Equal, 1)
                    }
                },
                TopCount = 1
            };

            var results = await Task.Run(() => _service.RetrieveMultiple(query));
            return results.Entities.Count > 0 ? results.Entities[0] : null;
        }

        // ────────────────────────────────────────────────────────────
        // BuildMasterEntity
        // ────────────────────────────────────────────────────────────

        private static Entity BuildMasterEntity(AccountEventMessage m, EntityReference? ownerTeam)
        {
            var e = new Entity(EntityLogicalName);

            e[IsMaster] = true;
            // Evita sincronizacion via Dual Write en el registro master
            e["customertypecode"] = new OptionSetValue(12);

            // Owner: el equipo configurado en MasterOwnerTeamName (el "cliente unico").
            // Se asigna en el Create y no despues con un AssignRequest para que el master
            // nunca exista en la business unit equivocada. Si no hay equipo configurado,
            // el master queda del usuario con el que corre la app.
            if (ownerTeam != null) e["ownerid"] = ownerTeam;

            // name es ApplicationRequired: usar identification como fallback si viene vacío
            var masterName = !string.IsNullOrEmpty(m.Name)
                ? m.Name
                : m.MsdynIdentificationNumber;
            SetString(e, "name", masterName);

            SetString(e, "telephone1",    m.Telephone1);
            SetString(e, "emailaddress1", m.EmailAddress1);
            SetString(e, "description",   m.Description);
            SetString(e, IdentificationNumber, m.MsdynIdentificationNumber);

            // Domicilio: el bloque address1_* del raw se copia tal cual al master.
            SetString(e, "address1_line1",           m.Address1Line1);
            SetString(e, "address1_line2",           m.Address1Line2);
            SetString(e, "address1_line3",           m.Address1Line3);
            SetString(e, "address1_city",            m.Address1City);
            SetString(e, "address1_county",          m.Address1County);
            SetString(e, "address1_stateorprovince", m.Address1StateOrProvince);
            SetString(e, "address1_postalcode",      m.Address1PostalCode);
            SetString(e, "address1_country",         m.Address1Country);
            SetDouble(e, "address1_latitude",        m.Address1Latitude);
            SetDouble(e, "address1_longitude",       m.Address1Longitude);

            // Lugar comercial: lookup a axx_lugarcomercial, se copia tal cual del raw.
            SetRef(e, LugarComercial, "axx_lugarcomercial", m.AxxLugarComercial);

            // Tipo de personeria juridica: lookup a axx_personeriajuridia, se copia tal cual del raw.
            SetRef(e, TipoPersoneria, "axx_personeriajuridia", m.AxxTipoPersoneriaJuridica);

            // msdyn_company requerido por plugin de Dual Write
            SetRef(e, "msdyn_company", "cdm_company", m.MsdynCompany);

            return e;
        }

        private static void SetString(Entity e, string field, string? value)
        {
            if (!string.IsNullOrEmpty(value)) e[field] = value;
        }

        private static void SetDouble(Entity e, string field, double? value)
        {
            if (value.HasValue) e[field] = value.Value;
        }

        private static void SetRef(Entity e, string field, string logicalName, Guid? id)
        {
            if (id.HasValue && id.Value != Guid.Empty)
                e[field] = new EntityReference(logicalName, id.Value);
        }

        // ────────────────────────────────────────────────────────────
        // BulkAssociateRawsToMaster
        // ────────────────────────────────────────────────────────────

        private async Task BulkAssociateRawsToMasterAsync(string identificationNumber, EntityReference masterRef)
        {
            var notMasterFilter = new FilterExpression(LogicalOperator.Or);
            notMasterFilter.AddCondition(IsMaster, ConditionOperator.Equal, false);
            notMasterFilter.AddCondition(IsMaster, ConditionOperator.Null);

            var criteria = new FilterExpression(LogicalOperator.And);
            criteria.AddCondition(IdentificationNumber, ConditionOperator.Equal, identificationNumber);
            criteria.AddFilter(notMasterFilter);

            var query = new QueryExpression(EntityLogicalName)
            {
                ColumnSet = new ColumnSet(MasterAccountId),
                Criteria  = criteria,
                PageInfo  = new PagingInfo { PageNumber = 1, Count = BulkBatchSize }
            };

            var raws = (await Task.Run(() => _service.RetrieveMultiple(query))).Entities;

            _logger.LogInformation(
                "[BulkAssociate] {Count} Raws para '{Identification}' → Master {MasterId}.",
                raws.Count, identificationNumber, masterRef.Id);

            if (raws.Count == 0) return;

            if (raws.Count >= BulkBatchSize)
                _logger.LogWarning("[BulkAssociate] Limite {Limit} alcanzado. Pueden quedar Raws sin procesar.", BulkBatchSize);

            var updates = new List<Entity>();
            int skip    = 0;

            foreach (var raw in raws)
            {
                var current = raw.GetAttributeValue<EntityReference>(MasterAccountId);
                if (current?.Id == masterRef.Id) { skip++; continue; }

                var upd = new Entity(EntityLogicalName, raw.Id);
                upd[MasterAccountId] = masterRef;
                updates.Add(upd);
            }

            if (updates.Count == 0)
            {
                _logger.LogInformation("[BulkAssociate] Todos los {Count} Raws ya estaban asociados.", skip);
                return;
            }

            _logger.LogInformation(
                "[BulkAssociate] {Count} Raws a linkear ({Skip} ya asociados).", updates.Count, skip);

            // Ver BulkUpdateExecutor: si algun link no se puede escribir se lanza, para
            // que el mensaje se reintente en vez de perderse en silencio.
            await BulkUpdateExecutor.ExecuteAsync(_service, _logger, "[BulkAssociate]", updates);
        }
    }
}
