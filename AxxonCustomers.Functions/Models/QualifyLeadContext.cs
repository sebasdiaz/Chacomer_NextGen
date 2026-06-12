namespace AxxonCustomers.Functions.Models
{
    /// <summary>
    /// Resultado de parsear el RemoteExecutionContext que Dataverse publica
    /// en la cola Service Bus ante cada QualifyLead.
    /// </summary>
    public class QualifyLeadContext
    {
        /// <summary>Nombre del mensaje SDK (se espera "QualifyLead").</summary>
        public string MessageName { get; set; } = string.Empty;

        /// <summary>Id del lead que fue calificado (InputParameters.LeadId).</summary>
        public Guid? LeadId { get; set; }

        /// <summary>
        /// Id del contact referenciado en InputParameters.OpportunityCustomerId o,
        /// en su defecto, creado al calificar (OutputParameters.CreatedEntities).
        /// Null cuando la calificacion no involucra un contact.
        /// </summary>
        public Guid? ContactId { get; set; }

        /// <summary>LogicalName del registro referenciado en OpportunityCustomerId.</summary>
        public string? CustomerLogicalName { get; set; }
    }
}
