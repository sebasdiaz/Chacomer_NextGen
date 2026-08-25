using AxxonCustomers.Functions.Mapping;
using AxxonCustomers.Functions.Models;
using AxxonCustomers.Functions.Services;
using AxxonCustomers.Functions.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;

namespace AxxonCustomers.Functions.Tests
{
    /// <summary>
    /// La guarda del <c>AccountNum</c>, que es lo que hace que el orden del alta funcione:
    /// la fila de LTMCustTable se clavea con el CustomerAccount, que recien existe cuando
    /// CustomerSyncService creo el customer en F&amp;O e hizo el write-back.
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
            string? accountNum)
        {
            var org       = new FakeOrganizationService();
            var contactId = Guid.NewGuid();

            var company = org.Add("cdm_company", Guid.NewGuid(),
                (LtmCustMapping.CompanyCodeAttribute, "caut"));

            var docType = org.Add(
                LtmCustMapping.VirtualDocTypeEntity, Guid.NewGuid(),
                (LtmCustMapping.VirtualDocTypeId, "RUC"),
                (LtmCustMapping.VirtualTaxPayerTypeId, "PJ"));

            org.Add(
                LtmCustMapping.VirtualAccountTypeGroupEntity, Guid.NewGuid(),
                (LtmCustMapping.VirtualAccountTypeGroupId, "Cliente Local"),
                (LtmCustMapping.VirtualAccountTypeGroupCompany, "caut"),
                (LtmCustMapping.VirtualAccountTypeGroupCustVend, LtmCustMapping.CustVendEntityCustomer));

            org.Add("contact", contactId,
                (LtmCustMapping.CompanyAttribute, company),
                (LtmCustMapping.IdentificationNumberAttribute, "80098873-6"),
                (LtmCustMapping.DocTypeAttribute, docType),
                (LtmCustSource.Contact.AccountNumberAttribute, accountNum));

            var builder = new LtmCustPayloadBuilder(
                org,
                new LtmCatalogResolver(org, new LtmCatalogCache(), NullLogger<LtmCatalogResolver>.Instance),
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
