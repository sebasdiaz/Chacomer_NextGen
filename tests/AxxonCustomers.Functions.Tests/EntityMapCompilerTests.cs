using AxxonCustomers.Functions.Mapping;

namespace AxxonCustomers.Functions.Tests
{
    /// <summary>
    /// El compilador es el punto donde un error de mapeo tiene que morir. Si pasa de
    /// aca, no falla: escribe mal en F&amp;O y nadie se entera.
    /// </summary>
    public class EntityMapCompilerTests
    {
        // ── Inversion del export ──────────────────────────────────────

        [Fact]
        public void Invierte_el_export_el_campo_de_crm_es_el_origen()
        {
            var map = Given.Compile(
                Given.ExportWith(Given.Row("msdyn_identificationnumber", "IDENTIFICATIONNUMBER")),
                Given.Overlay());

            var field = map.Field("IDENTIFICATIONNUMBER");

            Assert.Equal("msdyn_identificationnumber", field.Attribute);
            Assert.Equal(FieldKind.Direct, field.Kind);
        }

        [Fact]
        public void Un_path_con_punto_compila_como_lookup()
        {
            var map = Given.Compile(
                Given.ExportWith(Given.Row("msdyn_partyid.msdyn_partynumber", "PARTYNUMBER")),
                Given.Overlay());

            var field = map.Field("PARTYNUMBER");

            Assert.Equal(FieldKind.Lookup, field.Kind);
            Assert.Equal("msdyn_partyid", field.Attribute);
            Assert.Equal("msdyn_partynumber", field.RelatedAttribute);
        }

        [Fact]
        public void Mas_de_un_nivel_de_lookup_no_compila()
        {
            var errors = Given.CompileErrors(
                Given.ExportWith(Given.Row("a.b.c", "ALGO")),
                Given.Overlay());

            Assert.Contains(errors, e => e.Contains("un nivel de lookup"));
        }

        [Fact]
        public void Una_fila_unidireccional_no_se_devuelve_a_fo()
        {
            // syncDirection 1 = solo AX -> CRM: mandarla de vuelta seria inventar.
            var map = Given.Compile(
                Given.ExportWith(Given.Row("msdyn_algo", "ALGO", syncDirection: 1)),
                Given.Overlay());

            Assert.DoesNotContain(map.Fields, f => f.TargetField == "ALGO");
        }

        // ── Value maps ────────────────────────────────────────────────

        [Fact]
        public void Invierte_el_value_map_la_clave_pasa_a_ser_el_valor_de_crm()
        {
            var map = Given.Compile(
                Given.ExportWith(Given.Row(
                    "msdyn_onholdstatus", "ONHOLDSTATUS",
                    new Dictionary<string, string> { ["no"] = "806380000", ["all"] = "806380002" })),
                Given.Overlay());

            var field = map.Field("ONHOLDSTATUS");

            Assert.Equal(FieldKind.ValueMap, field.Kind);
            Assert.Equal("no",  field.ValueMap!["806380000"]);
            Assert.Equal("all", field.ValueMap["806380002"]);
        }

        [Fact]
        public void Un_value_map_ambiguo_no_compila()
        {
            // Dos valores de F&O cayendo en el mismo valor de CRM: la inversa no existe.
            var errors = Given.CompileErrors(
                Given.ExportWith(Given.Row(
                    "msdyn_estado", "STATUS",
                    new Dictionary<string, string> { ["Activo"] = "1", ["Vigente"] = "1" })),
                Given.Overlay());

            Assert.Contains(errors, e => e.Contains("no es invertible"));
        }

        [Fact]
        public void El_overlay_puede_reemplazar_el_value_map_del_export()
        {
            // Caso real: el export trae los literales en minuscula y la API OData de F&O
            // es case-sensitive.
            var overlay = Given.Overlay();
            overlay.Fields["msdyn_onetimecustomer"] = new OverlayField
            {
                Kind = "valueMap",
                Map  = new Dictionary<string, string> { ["True"] = "Yes", ["False"] = "No" }
            };

            var map = Given.Compile(
                Given.ExportWith(Given.Row(
                    "msdyn_onetimecustomer", "ISONETIMECUSTOMER",
                    new Dictionary<string, string> { ["yes"] = "True", ["no"] = "False" })),
                overlay);

            Assert.Equal("Yes", map.Field("ISONETIMECUSTOMER").ValueMap!["True"]);
        }

        // ── Overlay ───────────────────────────────────────────────────

        [Fact]
        public void Ignore_descarta_la_fila_del_export()
        {
            var overlay = Given.Overlay();
            overlay.Ignore.Add("msdyn_sellable");

            var map = Given.Compile(
                Given.ExportWith(Given.Row("msdyn_sellable", "PARTYTYPE")),
                overlay);

            Assert.DoesNotContain(map.Fields, f => f.TargetField == "PARTYTYPE");
        }

        [Fact]
        public void Una_constante_gana_sobre_la_fila_del_export()
        {
            var overlay = Given.Overlay();
            overlay.Constants["PARTYTYPE"] = Given.Json("\"Person\"");

            var map = Given.Compile(
                Given.ExportWith(Given.Row("msdyn_sellable", "PARTYTYPE")),
                overlay);

            var field = map.Field("PARTYTYPE");

            Assert.Equal(FieldKind.Const, field.Kind);
            Assert.Equal("Person", field.ConstantValue);
            Assert.Null(field.Attribute);
        }

        [Fact]
        public void Un_override_sin_target_hereda_el_del_export()
        {
            var overlay = Given.Overlay();
            overlay.Fields["msdyn_creditrating"] = new OverlayField { Kind = "label" };

            var map = Given.Compile(
                Given.ExportWith(Given.Row("msdyn_creditrating", "CREDITRATING")),
                overlay);

            var field = map.Field("CREDITRATING");

            Assert.Equal(FieldKind.Label, field.Kind);
            Assert.Equal("msdyn_creditrating", field.Attribute);
        }

        [Fact]
        public void El_overlay_puede_agregar_un_campo_que_el_export_no_trae()
        {
            var overlay = Given.Overlay();
            overlay.Fields["msdyn_notas"] = new OverlayField { Target = "CREDMANNOTES", Kind = "direct" };

            var map = Given.Compile(Given.ExportWith(), overlay);

            Assert.Equal("msdyn_notas", map.Field("CREDMANNOTES").Attribute);
        }

        [Fact]
        public void Un_campo_nuevo_sin_target_no_compila()
        {
            var overlay = Given.Overlay();
            overlay.Fields["msdyn_notas"] = new OverlayField { Kind = "direct" };

            var errors = Given.CompileErrors(Given.ExportWith(), overlay);

            Assert.Contains(errors, e => e.Contains("'target' es obligatorio"));
        }

        [Fact]
        public void Un_kind_desconocido_no_compila()
        {
            var overlay = Given.Overlay();
            overlay.Fields["msdyn_algo"] = new OverlayField { Target = "ALGO", Kind = "telepatia" };

            var errors = Given.CompileErrors(Given.ExportWith(), overlay);

            Assert.Contains(errors, e => e.Contains("kind 'telepatia' desconocido"));
        }

        // ── Clave e idempotencia ──────────────────────────────────────

        [Fact]
        public void El_write_back_no_se_manda_en_el_post_pero_conserva_su_campo_de_fo()
        {
            // F&O genera el CustomerAccount por number sequence: mandarlo seria pisarlo.
            var map = Given.Compile(Given.ExportWith(), Given.Overlay());

            Assert.True(map.Field(Given.WriteBackTarget).ExcludeFromCreate);
            Assert.Equal(Given.WriteBackTarget, map.WriteBackTargetField);
        }

        [Fact]
        public void Un_write_back_sin_mapear_no_compila()
        {
            var overlay = Given.Overlay();
            overlay.Key.WriteBack = "campo_que_no_existe";

            var errors = Given.CompileErrors(Given.ExportWith(), overlay);

            Assert.Contains(errors, e => e.Contains("no esta mapeado"));
        }

        [Fact]
        public void Un_match_on_sin_campo_que_lo_alimente_no_compila()
        {
            var overlay = Given.Overlay();
            overlay.Key.MatchOn.Add("PARTYNUMBER");

            var errors = Given.CompileErrors(Given.ExportWith(), overlay);

            Assert.Contains(errors, e => e.Contains("matchOn 'PARTYNUMBER'"));
        }

        [Fact]
        public void Un_destino_mapeado_dos_veces_no_compila()
        {
            var errors = Given.CompileErrors(
                Given.ExportWith(
                    Given.Row("name", "NAME"),
                    Given.Row("description", "NAME")),
                Given.Overlay());

            Assert.Contains(errors, e => e.Contains("esta mapeado 2 veces"));
        }

        // ── Lectura de Dataverse ──────────────────────────────────────

        [Fact]
        public void Las_columnas_incluyen_compania_write_back_y_las_de_syncwhen()
        {
            var overlay = Given.Overlay();
            overlay.SyncWhen.Add(new OverlayCondition
            {
                Attribute     = "customertypecode",
                ExpectedValue = Given.Json("3")
            });

            var map = Given.Compile(
                Given.ExportWith(Given.Row("msdyn_partyid.msdyn_partynumber", "PARTYNUMBER")),
                overlay);

            Assert.Contains("msdyn_company",   map.Columns);
            Assert.Contains("accountnumber",   map.Columns);
            Assert.Contains("customertypecode", map.Columns);
            // De un lookup se lee el atributo base, no el path.
            Assert.Contains("msdyn_partyid",   map.Columns);
            Assert.DoesNotContain("msdyn_partyid.msdyn_partynumber", map.Columns);
        }

        [Fact]
        public void Una_condicion_de_syncwhen_se_renderiza_a_string_canonico()
        {
            var overlay = Given.Overlay();
            overlay.SyncWhen.Add(new OverlayCondition
            {
                Attribute     = "msdyn_sellable",
                ExpectedValue = Given.Json("true")
            });

            var map = Given.Compile(Given.ExportWith(), overlay);

            Assert.Equal("True", map.SyncWhen.Single().ExpectedValue);
        }

        // ── Campos excluidos del PATCH ────────────────────────────────

        [Fact]
        public void Los_campos_de_matchon_no_viajan_en_la_modificacion()
        {
            // PartyNumber y CustomerAccount son la identidad del registro en F&O:
            // mandarlos en un PATCH es pedir que el customer pase a ser otro.
            var overlay = Given.Overlay();
            overlay.Key.MatchOn.Add("PARTYNUMBER");

            var map = Given.Compile(
                Given.ExportWith(Given.Row("msdyn_partyid.msdyn_partynumber", "PARTYNUMBER")),
                overlay);

            Assert.True(map.Field("PARTYNUMBER").ExcludeFromUpdate);
            Assert.True(map.Field(Given.WriteBackTarget).ExcludeFromUpdate);
        }

        [Fact]
        public void Un_campo_declarado_inmutable_no_viaja_en_la_modificacion()
        {
            var overlay = Given.Overlay();
            overlay.Key.Immutable.Add("PARTYTYPE");

            var map = Given.Compile(
                Given.ExportWith(Given.Row("customertypecode", "PARTYTYPE")),
                overlay);

            Assert.True(map.Field("PARTYTYPE").ExcludeFromUpdate);
            // Sigue yendo en el alta: inmutable es "no se cambia", no "no se manda".
            Assert.False(map.Field("PARTYTYPE").ExcludeFromCreate);
        }

        [Fact]
        public void El_resto_de_los_campos_viaja_en_la_modificacion()
        {
            var map = Given.Compile(Given.ExportWith(Given.Row("name", "NAME")), Given.Overlay());

            Assert.False(map.Field("NAME").ExcludeFromUpdate);
        }

        [Fact]
        public void Un_inmutable_que_no_esta_mapeado_no_compila()
        {
            // Excluir de la actualizacion algo que nunca se manda es una declaracion
            // muerta: casi siempre es un nombre mal escrito.
            var overlay = Given.Overlay();
            overlay.Key.Immutable.Add("CAMPO_QUE_NO_EXISTE");

            var errors = Given.CompileErrors(Given.ExportWith(), overlay);

            Assert.Contains(errors, e => e.Contains("CAMPO_QUE_NO_EXISTE"));
        }

        // ── Reporte de errores ────────────────────────────────────────

        [Fact]
        public void Acumula_todos_los_errores_en_vez_de_cortar_en_el_primero()
        {
            var overlay = Given.Overlay();
            overlay.Key.WriteBack = "campo_que_no_existe";
            overlay.Key.MatchOn.Add("TAMPOCO_EXISTE");
            overlay.Fields["msdyn_algo"] = new OverlayField { Target = "ALGO", Kind = "telepatia" };

            var errors = Given.CompileErrors(
                Given.ExportWith(
                    Given.Row("name", "NAME"),
                    Given.Row("description", "NAME")),
                overlay);

            // Corregir un mapeo de a un error por deploy no es viable.
            Assert.True(errors.Count >= 4, $"Se esperaban 4+ errores, llegaron {errors.Count}.");
        }
    }
}
