using AxxonLeads.Functions.Models;

namespace AxxonLeads.Functions.Services
{
    /// <summary>Alta de un lead en Dataverse a partir del mensaje de un satelite.</summary>
    public interface ILeadIntakeService
    {
        Task<LeadIntakeResult> ProcessAsync(
            string source,
            LeadIntakePayload payload,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Resultado del alta. <see cref="AlreadyExisted"/> distingue el alta real de la
    /// reentrega ya procesada: las dos completan el mensaje, pero solo una escribio.
    /// </summary>
    /// <param name="LeadId">Id del lead en Dataverse.</param>
    /// <param name="AlreadyExisted">True si el lead ya estaba (dedup por id de origen).</param>
    public readonly record struct LeadIntakeResult(Guid LeadId, bool AlreadyExisted);
}
