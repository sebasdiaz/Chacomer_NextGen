namespace AxxonLeads.Functions.Configuration
{
    /// <summary>
    /// Lo unico de esta integracion que depende del org y no del codigo: los logical names
    /// de las dos columnas de <c>lead</c> que no son estandar de Dataverse.
    ///
    /// Van por app setting y no hardcodeadas porque el nombre real se confirma contra el
    /// environment, y equivocarse no rompe al arrancar: rompe recien al primer Create, con
    /// un mensaje en el DLQ por cada lead que mande el satelite. Cambiarlas es editar el
    /// Bicep, no redeployar la app.
    /// </summary>
    public sealed class LeadIntakeOptions
    {
        /// <summary>Default de <see cref="IdentificationAttribute"/>: el mismo campo que usa el master matching de contacts.</summary>
        public const string DefaultIdentificationAttribute = "msdyn_identificationnumber";

        /// <summary>
        /// Columna donde se escribe el RUC/cedula (<c>identificationNumber</c> del payload).
        /// Default: <see cref="DefaultIdentificationAttribute"/>.
        /// </summary>
        public string IdentificationAttribute { get; init; } = DefaultIdentificationAttribute;

        /// <summary>
        /// Columna donde se guarda el id del lead en el sistema origen
        /// (<c>externalId</c> del payload). VACIA = sin columna, y entonces la
        /// deduplicacion contra Dataverse queda apagada: ver
        /// <see cref="DeduplicationEnabled"/>.
        /// </summary>
        public string? ExternalIdAttribute { get; init; }

        /// <summary>
        /// True cuando hay donde guardar —y por lo tanto donde buscar— el id de origen.
        ///
        /// Con esto en false la unica proteccion contra duplicados es la deteccion de
        /// duplicados de la cola, que solo cubre el reenvio del satelite (mismo MessageId).
        /// NO cubre el caso feo: el Create sale bien y despues falla el Complete, Service
        /// Bus reentrega y se crea un segundo lead. Para cerrarlo hace falta una columna
        /// de id externo en <c>lead</c> y declararla en <c>LeadExternalIdAttribute</c>.
        /// </summary>
        public bool DeduplicationEnabled => !string.IsNullOrWhiteSpace(ExternalIdAttribute);
    }
}
