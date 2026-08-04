using Axxon.Eip.Core.FinOps;

namespace AxxonCustomers.Functions.Tests
{
    /// <summary>
    /// El segmento de clave del PATCH. Va en el path de la URL, asi que un valor con
    /// comilla o espacio que no se escape rompe el request (o peor: apunta a otro registro).
    /// </summary>
    public class FoODataKeyTests
    {
        [Fact]
        public void Arma_la_clave_compuesta_con_la_compania()
        {
            var key = FoOData.EntityKey(("dataAreaId", "cha"), ("CustomerAccount", "C0001"));

            Assert.Equal("(dataAreaId='cha',CustomerAccount='C0001')", key);
        }

        [Fact]
        public void Escapa_la_comilla_simple()
        {
            // OData duplica la comilla dentro del literal; despues se codifica para la URL.
            var key = FoOData.EntityKey(("CustomerAccount", "O'Brien"));

            Assert.Equal("(CustomerAccount='O%27%27Brien')", key);
        }

        [Fact]
        public void Codifica_los_caracteres_que_rompen_la_url()
        {
            var key = FoOData.EntityKey(("CustomerAccount", "C 1&2"));

            Assert.Equal("(CustomerAccount='C%201%262')", key);
        }

        [Fact]
        public void Rechaza_un_componente_sin_valor()
        {
            // Sin dataAreaId el PATCH pega en otra compania o no encuentra nada.
            Assert.Throws<ArgumentException>(() =>
                FoOData.EntityKey(("dataAreaId", ""), ("CustomerAccount", "C0001")));
        }

        [Fact]
        public void Rechaza_una_clave_vacia()
        {
            Assert.Throws<ArgumentException>(() => FoOData.EntityKey());
        }
    }
}
