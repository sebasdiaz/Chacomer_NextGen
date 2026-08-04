using System.Text.Json.Serialization;

namespace Axxon.Eip.Core.Messaging
{
    /// <summary>
    /// Payload de la cola de sincronizacion de customers hacia F&amp;O por API
    /// (legal entities que Dual Write no sincroniza).
    ///
    /// Es a proposito una referencia y no un snapshot: el consumidor relee el registro
    /// de Dataverse con las columnas exactas que pide el mapeo. Un snapshot que viaja en
    /// el mensaje llega parcial (los eventos Update mandan solo el delta) o viejo, y
    /// mapear desde ahi escribe mal en el ERP.
    ///
    /// Que entidad es y si es alta o modificacion los dice el envelope
    /// (<see cref="EipMessage{T}.EntityType"/> y <see cref="EipMessage{T}.Operation"/>);
    /// no se repiten aca para no tener dos fuentes de verdad.
    /// </summary>
    public sealed class CustomerSyncPayload
    {
        /// <summary>Id del registro de Dataverse (account o contact).</summary>
        [JsonPropertyName("recordId")]
        public Guid RecordId { get; set; }

        /// <summary>
        /// Legal entity destino (<c>cdm_companycode</c>). Viaja para la traza: el payload
        /// lo vuelve a resolver desde el registro al armarse.
        /// </summary>
        [JsonPropertyName("dataAreaId")]
        public string? DataAreaId { get; set; }
    }
}
