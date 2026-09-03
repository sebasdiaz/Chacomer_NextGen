using AxxonCustomers.Functions.Mapping;
using AxxonCustomers.Functions.Models;
using AxxonCustomers.Functions.Services;
using AxxonCustomers.Functions.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;

namespace AxxonCustomers.Functions.Tests
{
    /// <summary>
    /// Las dos guardas que hacen que el flujo no llene el DLQ de cosas que nunca iban a andar:
    /// la del <c>AccountNum</c> —la fila de LTMCustTable se clavea con el CustomerAccount, que
    /// recien existe cuando CustomerSyncService creo el customer en F&amp;O e hizo el
    /// write-back— y la de alcance, para las legal entities sin localizacion PY.
    /// </summary>
    public class LtmCustSyncServiceTests
    {
        [Fact]
        public async Task Sin_AccountNum_no_se_sincroniza_y_no_es_un_error()
        {
            // Es el orden natural del alta, no un dato roto: el mensaje se completa sin
            // procesar y el write-back vuelve a encolar el registro.
            var (service, recordId, ltm) = Given(accountNum: null);

            var synced = await service.ProcessAsync("contact", recordId);

            Assert.False(synced);
            Assert.Empty(ltm.Created);
        }

        [Fact]
        public async Task Fuera_del_alcance_de_la_localizacion_no_se_sincroniza_y_no_es_un_error()
        {
            // A la cola llegan todos los clientes que se crean en F&O, y buena parte del
            // environment vive en legal entities que no llevan LTMCustTable. Mandarlas al DLQ
            // lo llenaria de registros que nunca iban a andar.
            var (service, recordId, ltm) = Given(accountNum: "CAUT-000012", withLocalization: false);

            var synced = await service.ProcessAsync("contact", recordId);

            Assert.False(synced);
            Assert.Empty(ltm.Created);
        }

        [Fact]
        public async Task Con_AccountNum_se_hace_el_post()
        {
            var (service, recordId, ltm) = Given(accountNum: "CAUT-000012");

            var synced = await service.ProcessAsync("contact", recordId);

            Assert.True(synced);
            var payload = Assert.Single(ltm.Created);
            Assert.Equal("CAUT-000012", payload.AccountNum);
            Assert.Equal("caut",        payload.DataAreaId);
        }

        [Fact]
        public async Task Una_entidad_que_no_es_contact_ni_account_va_al_DLQ()
        {
            var (service, recordId, _) = Given(accountNum: "CAUT-000012");

            await Assert.ThrowsAsync<NonRetryableSyncException>(
                () => service.ProcessAsync("lead", recordId));
        }

        // ── Armado ────────────────────────────────────────────────────

        private static (LtmCustSyncService Service, Guid RecordId, FakeLtmCustService Ltm) Given(
            string? accountNum,
            bool withLocalization = true)
        {
            var org       = new FakeOrganizationService();
            var contactId = Guid.NewGuid();

            var company = org.Add("cdm_company", Guid.NewGuid(),
                (LtmCustMapping.CompanyCodeAttribute, "caut"));

            if (withLocalization)
            {
                org.Add(
                    LtmCustMapping.VirtualDocTypeEntity, Guid.NewGuid(),
                    (LtmCustMapping.VirtualDocTypeCompany, "caut"),
                    (LtmCustMapping.VirtualDocTypeId, LtmCustMapping.CountryDocTypeRuc),
                    (LtmCustMapping.VirtualTaxPayerTypeId, "PN"));

                org.Add(
                    LtmCustMapping.VirtualAccountTypeGroupEntity, Guid.NewGuid(),
                    (LtmCustMapping.VirtualAccountTypeGroupId, "Cliente Local"),
                    (LtmCustMapping.VirtualAccountTypeGroupCompany, "caut"),
                    (LtmCustMapping.VirtualAccountTypeGroupCustVend,
                        new OptionSetValue(LtmCustMapping.CustVendEntityCustomer)));
            }

            org.Add("contact", contactId,
                (LtmCustMapping.CompanyAttribute, company),
                (LtmCustMapping.IdentificationNumberAttribute, "80098873-6"),
                (LtmCustSource.Contact.AccountNumberAttribute, accountNum));

            var builder = new LtmCustPayloadBuilder(
                org,
                new LtmCatalogResolver(
                    org,
                    new FakeFoODataClient("DPTO_11"),
                    new LtmCatalogCache(),
                    NullLogger<LtmCatalogResolver>.Instance),
                new FakeFoSchemaProvider(
                    LtmCustMapping.DataAreaId,
                    LtmCustMapping.AccountNum,
                    LtmCustMapping.CountryDocTypeId,
                    LtmCustMapping.CountryDocNum,
                    LtmCustMapping.StateDocNum,
                    LtmCustMapping.TaxPayerTypeId,
                    LtmCustMapping.AccountTypeGroupId,
                    LtmCustMapping.CountryRegionId,
                    LtmCustMapping.StateId),
                NullLogger<LtmCustPayloadBuilder>.Instance);

            var ltm = new FakeLtmCustService();

            var service = new LtmCustSyncService(
                org, builder, ltm, NullLogger<LtmCustSyncService>.Instance);

            return (service, contactId, ltm);
        }

        private sealed class FakeLtmCustService : ILtmCustService
        {
            public List<LtmCustPayload> Created { get; } = new();

            public Task CreateAsync(LtmCustPayload payload, CancellationToken cancellationToken = default)
            {
                Created.Add(payload);
                return Task.CompletedTask;
            }
        }
    }
}
