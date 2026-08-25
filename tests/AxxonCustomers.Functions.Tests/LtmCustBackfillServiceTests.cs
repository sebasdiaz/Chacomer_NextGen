using Axxon.Eip.Core.Messaging;
using AxxonCustomers.Functions.Mapping;
using AxxonCustomers.Functions.Services;
using AxxonCustomers.Functions.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;

namespace AxxonCustomers.Functions.Tests
{
    /// <summary>
    /// El backfill de LTMCustTable. Lo que se prueba aca es a quien elige y a quien deja
    /// afuera: mandar de mas ensucia el DLQ, y mandar de menos deja clientes sin su fila de
    /// localizacion sin que nadie se entere.
    /// </summary>
    public class LtmCustBackfillServiceTests
    {
        [Fact]
        public async Task Solo_encola_los_que_ya_tienen_customer_en_FO()
        {
            // El campo de write-back con valor es la senial de que el customer existe en F&O.
            // Sin el, la fila de LTMCustTable no tiene clave.
            var (service, publisher, org) = Given();
            var conCustomer = AddContact(org, accountNum: "CAUT-000012");
            AddContact(org, accountNum: null);

            var result = await service.RunAsync(LtmCustSource.Contact, dryRun: false, max: null);

            Assert.Equal(1, result.Candidates);
            Assert.Equal(1, result.Enqueued);
            Assert.Equal(conCustomer, Assert.Single(publisher.Published).RecordId);
        }

        [Fact]
        public async Task Deja_afuera_los_master()
        {
            // El master es un registro de consolidacion, no un cliente: nunca va al ERP.
            var (service, publisher, org) = Given();
            AddContact(org, accountNum: "CAUT-000012", isMaster: true);

            var result = await service.RunAsync(LtmCustSource.Contact, dryRun: false, max: null);

            Assert.Equal(0, result.Candidates);
            Assert.Empty(publisher.Published);
        }

        [Fact]
        public async Task Incluye_los_que_nunca_tuvieron_seteado_el_flag_de_master()
        {
            // Un Yes/No que nunca se seteo viene sin valor, no como false. Filtrar por
            // "distinto de true" dejaria afuera a la mayoria de los raws.
            var (service, publisher, org) = Given();
            AddContact(org, accountNum: "CAUT-000012", isMaster: null);

            var result = await service.RunAsync(LtmCustSource.Contact, dryRun: false, max: null);

            Assert.Equal(1, result.Candidates);
            Assert.Single(publisher.Published);
        }

        [Fact]
        public async Task Deja_afuera_los_que_no_tienen_legal_entity()
        {
            // Sin company no hay dataAreaId: el mensaje terminaria en el DLQ. Filtrarlo aca
            // evita ensuciar el DLQ con registros que nunca iban a andar.
            var (service, publisher, org) = Given();
            AddContact(org, accountNum: "CAUT-000012", withCompany: false);

            var result = await service.RunAsync(LtmCustSource.Contact, dryRun: false, max: null);

            Assert.Equal(0, result.Candidates);
            Assert.Empty(publisher.Published);
        }

        [Fact]
        public async Task El_dryRun_cuenta_pero_no_encola()
        {
            var (service, publisher, org) = Given();
            AddContact(org, accountNum: "CAUT-000012");
            AddContact(org, accountNum: "CAUT-000013");

            var result = await service.RunAsync(LtmCustSource.Contact, dryRun: true, max: null);

            Assert.Equal(2, result.Candidates);
            Assert.Equal(0, result.Enqueued);
            Assert.Empty(publisher.Published);
        }

        [Fact]
        public async Task El_maximo_corta_la_corrida()
        {
            var (service, publisher, org) = Given();
            AddContact(org, accountNum: "CAUT-000012");
            AddContact(org, accountNum: "CAUT-000013");
            AddContact(org, accountNum: "CAUT-000014");

            var result = await service.RunAsync(LtmCustSource.Contact, dryRun: false, max: 2);

            Assert.Equal(2, result.Enqueued);
            Assert.Equal(2, publisher.Sent.Count);
        }

        [Fact]
        public async Task Encola_en_la_cola_de_LTM_con_session_por_registro()
        {
            // La session es el registro: dos mensajes del mismo cliente no pueden procesarse
            // fuera de orden.
            var (service, publisher, org) = Given();
            var contactId = AddContact(org, accountNum: "CAUT-000012");

            await service.RunAsync(LtmCustSource.Contact, dryRun: false, max: null);

            var sent = Assert.Single(publisher.Sent);
            Assert.Equal("customer-ltm-sync", sent.Queue);
            Assert.Equal(contactId.ToString(), sent.PartitionKey);
            Assert.Equal("contact", sent.EntityType);
        }

        // ── Armado ────────────────────────────────────────────────────

        private static (LtmCustBackfillService Service, FakePublisher Publisher, FakeOrganizationService Org)
            Given()
        {
            var org       = new FakeOrganizationService();
            var publisher = new FakePublisher();

            var dispatcher = new LtmSyncDispatcher(
                publisher, "customer-ltm-sync", NullLogger<LtmSyncDispatcher>.Instance);

            var service = new LtmCustBackfillService(
                org, dispatcher, NullLogger<LtmCustBackfillService>.Instance);

            return (service, publisher, org);
        }

        private static Guid AddContact(
            FakeOrganizationService org,
            string? accountNum,
            bool? isMaster = false,
            bool withCompany = true)
        {
            var contactId = Guid.NewGuid();

            var attributes = new List<(string, object?)>
            {
                (LtmCustSource.Contact.AccountNumberAttribute, accountNum)
            };

            if (withCompany)
                attributes.Add((LtmCustMapping.CompanyAttribute,
                    new EntityReference("cdm_company", Guid.NewGuid()) { Name = "caut" }));

            if (isMaster is not null)
                attributes.Add(("axx_ismaster", isMaster.Value));

            org.Add("contact", contactId, attributes.ToArray());
            return contactId;
        }

        private sealed class FakePublisher : IEipMessagePublisher
        {
            public List<(string Queue, string EntityType, string? PartitionKey, CustomerSyncPayload Payload)>
                Sent { get; } = new();

            public IEnumerable<CustomerSyncPayload> Published => Sent.Select(s => s.Payload);

            public Task PublishAsync<TPayload>(
                string queueName,
                EipMessage<TPayload> message,
                CancellationToken cancellationToken = default)
            {
                Assert.IsType<CustomerSyncPayload>(message.Payload);

                Sent.Add((
                    queueName,
                    message.EntityType,
                    message.PartitionKey,
                    (CustomerSyncPayload)(object)message.Payload!));

                return Task.CompletedTask;
            }
        }
    }
}
