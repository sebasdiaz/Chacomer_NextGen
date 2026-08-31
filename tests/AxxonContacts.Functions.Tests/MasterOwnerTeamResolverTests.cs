using AxxonContacts.Functions.Services;
using AxxonContacts.Functions.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace AxxonContacts.Functions.Tests
{
    /// <summary>
    /// Los masters ("cliente unico") tienen que quedar en la business unit del equipo
    /// configurado. Lo que se fija aca es lo que decide si eso pasa: que el equipo se
    /// resuelva por nombre una sola vez, que sin configuracion no se toque nada, y que una
    /// configuracion que no resuelve corte en vez de crear el master en la BU equivocada.
    /// </summary>
    public class MasterOwnerTeamResolverTests
    {
        private const string Equipo = "CLIENTE UNICO";

        private static Entity Team(string nombre) => new("team", Guid.NewGuid()) { ["name"] = nombre };

        private static MasterOwnerTeamResolver Resolver(IOrganizationService crm, string? teamName) =>
            new(crm, new MasterOwnerTeamCache(), teamName, NullLogger.Instance);

        [Fact]
        public async Task Sin_equipo_configurado_no_asigna_owner_ni_consulta_Dataverse()
        {
            var crm = new FakeTeamOrganizationService();

            var owner = await Resolver(crm, null).ResolveAsync();

            Assert.Null(owner);
            Assert.Equal(0, crm.Consultas);
        }

        [Fact]
        public async Task Resuelve_el_owner_team_por_nombre()
        {
            var equipo = Team(Equipo);
            var crm    = new FakeTeamOrganizationService(equipo);

            var owner = await Resolver(crm, Equipo).ResolveAsync();

            Assert.NotNull(owner);
            Assert.Equal("team", owner!.LogicalName);
            Assert.Equal(equipo.Id, owner.Id);
        }

        [Fact]
        public async Task Filtra_por_nombre_y_por_owner_team()
        {
            var crm = new FakeTeamOrganizationService(Team(Equipo));

            await Resolver(crm, Equipo).ResolveAsync();

            var condiciones = crm.UltimaQuery!.Criteria.Conditions;
            Assert.Equal("team", crm.UltimaQuery.EntityName);
            Assert.Contains(condiciones, c => c.AttributeName == "name" && (string)c.Values[0] == Equipo);
            // teamtype 0 = Owner: un access team no puede ser dueño de un registro.
            Assert.Contains(condiciones, c => c.AttributeName == "teamtype" && (int)c.Values[0] == 0);
        }

        [Fact]
        public async Task El_equipo_se_consulta_una_sola_vez()
        {
            var crm      = new FakeTeamOrganizationService(Team(Equipo));
            var resolver = Resolver(crm, Equipo);

            await resolver.ResolveAsync();
            await resolver.ResolveAsync();

            Assert.Equal(1, crm.Consultas);
        }

        [Fact]
        public async Task Si_el_equipo_no_existe_lanza_en_vez_de_crear_el_master_sin_owner()
        {
            var crm = new FakeTeamOrganizationService();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Resolver(crm, Equipo).ResolveAsync());

            Assert.Contains(Equipo, ex.Message);
            Assert.Contains("MasterOwnerTeamName", ex.Message);
        }
    }
}
