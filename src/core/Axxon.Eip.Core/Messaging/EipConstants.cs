namespace Axxon.Eip.Core.Messaging
{
    /// <summary>Sistemas origen conocidos (valor de <see cref="EipMessage{T}.Source"/>).</summary>
    public static class EipSource
    {
        public const string Dataverse = "dataverse";
        public const string FinanceOperations = "fo";
        public const string Magento = "magento";
        public const string VTex = "vtex";
        public const string Plytix = "plytix";
    }

    /// <summary>Operaciones estandar (valor de <see cref="EipMessage{T}.Operation"/>).</summary>
    public static class EipOperation
    {
        public const string Create = "create";
        public const string Update = "update";
        public const string Delete = "delete";
        public const string Upsert = "upsert";
    }

    /// <summary>Razones de dead-letter estandar de la plataforma.</summary>
    public static class EipDeadLetterReason
    {
        /// <summary>El cuerpo del mensaje no se pudo deserializar al envelope.</summary>
        public const string DeserializationFailed = "DeserializationFailed";

        /// <summary>Violacion de una regla de negocio: reintentar no ayuda.</summary>
        public const string BusinessRuleFailed = "BusinessRuleFailed";

        /// <summary>Faltan campos obligatorios del contrato.</summary>
        public const string ContractViolation = "ContractViolation";
    }
}
