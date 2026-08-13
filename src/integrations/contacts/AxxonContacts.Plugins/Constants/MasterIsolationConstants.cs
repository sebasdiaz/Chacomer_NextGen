namespace AxxonContacts.Plugins.Constants
{
    /// <summary>
    /// Campos que mantienen a un registro master fuera de F&amp;O.
    ///
    /// axx_ismaster es el mismo logical name en contact y en account, pero lo que
    /// bloquea la sincronizacion difiere: el contact master se queda sin legal entity
    /// (msdyn_company null + msdyn_sellable false) y el account master si la lleva
    /// —el plugin de Dual Write la exige— y depende del customertypecode para quedar
    /// fuera del filtro del mapa.
    /// </summary>
    public static class MasterIsolationConstants
    {
        public const string IsMaster      = "axx_ismaster";
        public const string PreImageAlias = "preImage";

        public const string AccountEntityLogicalName = "account";
        public const string CustomerTypeCode         = "customertypecode";

        /// <summary>
        /// customertypecode que deja al account master fuera del mapa de Dual Write.
        /// Mismo valor que escribe AccountMasterMatchingService al crear el master.
        /// </summary>
        public const int MasterCustomerTypeCode = 12;
    }
}
